#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

internal static class OverviewUpdateQueue
{
    private static readonly object Gate = new();
    private static readonly object CommandGate = new();
    private static readonly object WorkerGate = new();
    private static System.Threading.Tasks.Task<ObserveResult>? _observeTask;
    private static long _commandEpoch;
    private sealed class ObserveResult
    {
        internal string Context="", Error="";
        internal long Epoch;
        internal bool Executed;
    }
    private static UpdateSnapshot _snapshot = new();
    private static DateTime _pollAt, _observeAt, _launchAt;
    private static string _queueError="", _observeError="", _observedPending="";
    private static string _error = "", _historicalError = "", _localError = "", _feedback = "";
    private static readonly DateTime SessionStartUtc = DateTime.UtcNow;
    private static DateTime _workerAttemptUtc;
    internal static string HistoricalError => _historicalError;
    internal static string Feedback => _feedback;
    private static bool _unsafeRead;
    /// <summary>2.3.7: false until a queue read for the current context has been published. Until then
    /// <see cref="Snapshot"/> is the empty placeholder, which must not be mistaken for "the queue is empty".</summary>
    private static bool _hasRead;
    internal static bool HasRead { get { lock (Gate) return _hasRead; } }
    internal static string Root => ModConfigEnhance.Updating.ManagerIdentity.Runtime(Paths.BepInExRootPath);
    internal static UpdateSnapshot Snapshot { get { Poll(); return _snapshot; } }
    /// <summary>Network settings for any thread. Unlike <see cref="Snapshot"/> it never schedules a queue read (Poll
    /// captures Unity-owned state and is UI-thread only); it returns the last published value.</summary>
    internal static UpdateNetworkOptions Network { get { lock (Gate) return _snapshot.Network ?? new UpdateNetworkOptions(); } }
    internal static string LastError => _error;
    /// <summary>
    /// 2.4.4: highest version a finished (completed / pending-restart) update installed for this store package, or "".
    /// Reads the last published snapshot only (no Poll), so it is safe from list rendering and the update check.
    /// </summary>
    internal static string CompletedVersion(string ns, string package)
    {
        if (ns.Length == 0 || package.Length == 0) return "";
        List<UpdateJob> jobs; lock (Gate) jobs = _snapshot.Jobs?.ToList() ?? new List<UpdateJob>();
        string best = "";
        foreach (var job in jobs)
        {
            if (job.Uninstall || (job.State != UpdateStates.Completed && job.State != UpdateStates.PendingRestart)) continue;
            if (!string.Equals(job.Namespace, ns, StringComparison.OrdinalIgnoreCase) || !string.Equals(job.Package, package, StringComparison.OrdinalIgnoreCase)) continue;
            if (best.Length == 0 || ManifestCopies.Compare(job.TargetVersion, best) > 0) best = job.TargetVersion;
        }
        return best;
    }

    private sealed class QueueRead
    {
        internal UpdateSnapshot Snapshot=new();
        internal string Error="", HistoricalError="";
    }
    private static readonly ChangedFileSnapshot<QueueRead> ReadCache=new();
    private static readonly object ReadGate=new();
    private static System.Threading.Tasks.Task? _readTask;
    private static string _readContext="";
    private static IEnumerable<string> QueueFiles(string root)
    {
        yield return Path.Combine(root,"queue.json");
        yield return Path.Combine(root,"worker-error.json");
        yield return Path.Combine(root,"resource-batch.json");
        string requests = Path.Combine(root, "update-requests");
        if (Directory.Exists(requests)) foreach (string request in Directory.GetFiles(requests, "*.json")) yield return request;
        yield return Path.Combine(root,"shop","task.json");
        string operations=Path.Combine(root,"operations");
        if(Directory.Exists(operations))foreach(string operation in Directory.GetDirectories(operations)){
            yield return Path.Combine(operation,"shop","task.json");yield return Path.Combine(operation,"error.json");
        }
        string jobs=Path.Combine(root,"jobs");
        if(Directory.Exists(jobs))
            foreach(string dir in Directory.GetDirectories(jobs))
                yield return Path.Combine(dir,"transaction.json");
        string shop=Path.Combine(root,"shop");
        if(Directory.Exists(shop))
            foreach(string path in Directory.GetFiles(shop,"transaction.json",SearchOption.AllDirectories))yield return path;
    }
    private static string ReadText(string file)
    {
        using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var reader=new StreamReader(input);return reader.ReadToEnd();
    }
    private static QueueRead ReadQueue(string root)
    {
        string file=Path.Combine(root,"queue.json"),failure=Path.Combine(root,"worker-error.json");
        var result=new QueueRead();
        if(File.Exists(file))
        {
            result.Snapshot=JsonConvert.DeserializeObject<UpdateSnapshot>(ReadText(file)) ?? throw new InvalidDataException("Empty queue");
            if(result.Snapshot.SchemaVersion!=1)throw new InvalidDataException("Unsupported queue version");
        }
        var snapshot=result.Snapshot;snapshot.Jobs??=new();snapshot.Network??=new();
        foreach(var job in snapshot.Jobs.Where(j=>j.State!=UpdateStates.Completed && j.State!=UpdateStates.RolledBack && j.State!=UpdateStates.Superseded))
        {
            if(!Guid.TryParseExact(job.Id,"N",out _))throw new InvalidDataException("Invalid update job ID");
            string transaction=Path.Combine(root,"jobs",job.Id,"transaction.json");
            if(File.Exists(transaction))job.RecoveryRequired=PackageRules.HasUnfinishedWrites(Newtonsoft.Json.Linq.JObject.Parse(ReadText(transaction)).Value<string>("Phase")??"");
        }
        string shopPath=Path.Combine(root,"shop","task.json");
        if(File.Exists(shopPath))
        {
            var task=JsonConvert.DeserializeObject<ShopTask>(ReadText(shopPath));
            if(task!=null && !task.Dismissed && Guid.TryParseExact(task.Id,"N",out _))snapshot.Jobs.Add(ShopQueueProjection.ToJob(task));
        }
        string operations=Path.Combine(root,"operations");
        if(Directory.Exists(operations))foreach(string operation in Directory.GetDirectories(operations))
        {
            string taskPath=Path.Combine(operation,"shop","task.json"),errorPath=Path.Combine(operation,"error.json");
            var task=File.Exists(taskPath)?JsonConvert.DeserializeObject<ShopTask>(ReadText(taskPath)):null;
            var job=task!=null?ShopQueueProjection.ToJob(task):new UpdateJob{State=UpdateStates.Queued,DisplayName="Queued mod operation"};
            job.Id="operation:"+Path.GetFileName(operation);job.StoreOperation=false;
            if(task?.DependencyWarnings.Count>0&&task.State=="uninstall-preview")job.Message="Review dependency impact in this operation's details.";
            if(File.Exists(errorPath)){job.Error=Newtonsoft.Json.Linq.JObject.Parse(ReadText(errorPath)).Value<string>("Error")??"";job.State=UpdateStates.Failed;}
            snapshot.Jobs.Add(job);
        }
        // File mtime alone is not evidence of a failure in this session. Preserve old diagnostics for inspection.
        if (snapshot.Error.Length > 0)
        {
            bool current = File.Exists(file) && File.GetLastWriteTimeUtc(file) >= SessionStartUtc
                && snapshot.WorkerProtocol == WorkerProtocol.Current && snapshot.WorkerBuild.Length > 0;
            if (current) result.Error = snapshot.Error; else result.HistoricalError = snapshot.Error;
        }
        if (File.Exists(failure))
        {
            var diagnostic = Newtonsoft.Json.Linq.JObject.Parse(ReadText(failure));
            string message = diagnostic.Value<string>("Message") ?? "";
            string stamp = diagnostic.Value<string>("UpdatedUtc") ?? "";
            // A protocol2 queue published after our launch/command is positive startup/processing evidence.
            DateTime success = File.Exists(file) && snapshot.WorkerProtocol == WorkerProtocol.Current && snapshot.WorkerBuild.Length > 0
                && File.GetLastWriteTimeUtc(file) >= _workerAttemptUtc && _workerAttemptUtc != DateTime.MinValue
                ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
            if (result.Error.Length == 0 && UpdateQueuePolicy.CurrentFailure(stamp, SessionStartUtc, _workerAttemptUtc, success)) result.Error = message;
            else if (message.Length > 0) result.HistoricalError += (result.HistoricalError.Length > 0 ? "\n" : "") + stamp + "\n" + message;
        }
        return result;
    }
    private static void RefreshRead(string root,string context)
    {
        QueueRead? value;string error;
        lock(ReadGate)
        {
            ReadCache.Refresh(context,()=>QueueFiles(root),()=>ReadQueue(root));
            value=ReadCache.Value;error=ReadCache.Error;
        }
        lock(Gate)
        {
            if(context!=_readContext)return;
            _unsafeRead=error.Length>0 || value==null;
            if(value!=null) { _hasRead=true;_snapshot=value.Snapshot;_queueError=value.Error;_error=_localError.Length > 0 ? _localError : _observeError.Length>0 ? _observeError : _queueError;_historicalError=value.HistoricalError; }
            if(_unsafeRead)_error=_queueError="Could not read the update queue; the previous view was kept: "+error;
            ReportQueueChanges(_snapshot, error);
        }
    }

    private static readonly Dictionary<string,string> _reportedJobStates = new(StringComparer.Ordinal);
    private static string _reportedReadError = "";
    /// <summary>Normal-layer one-liner per job state change (deduped), so a reading of the BepInEx log tells the whole update story.</summary>
    private static void ReportQueueChanges(UpdateSnapshot snapshot, string readError)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var job in snapshot.Jobs)
        {
            seen.Add(job.Id);
            string signature = job.State + "\u001f" + job.Message + "\u001f" + job.Error;
            if (_reportedJobStates.TryGetValue(job.Id, out var previous) && previous == signature) continue;
            bool first = !_reportedJobStates.ContainsKey(job.Id);
            _reportedJobStates[job.Id] = signature;
            if (first && job.State == UpdateStates.Queued) continue; // already announced at submission
            // A record that ended before this session is history, not news. Re-announcing last session's
            // rollback/clear/completion on every launch buried the entries that do need attention.
            if (first && (job.State == UpdateStates.Completed || job.State == UpdateStates.Removed ||
                job.State == UpdateStates.RolledBack || job.State == UpdateStates.Superseded)) continue;
            string name = job.DisplayName.Length > 0 ? job.DisplayName : job.Package;
            string line = $"{name} {job.InstalledVersion}→{job.TargetVersion} · {JobStateLabel(job.State)}";
            if (job.Message.Length > 0) line += " · " + Plain(job.Message);
            // A record that is only being archived (cleared/superseded) keeps its old error text; report that as a
            // note rather than re-raising a red error for something that is not failing now.
            bool failing = job.State == UpdateStates.Failed || job.State == UpdateStates.Stopped;
            if (job.Error.Length > 0 && failing) Diagnostics.ManagerLog.Error(line + " · " + Plain(job.Error).Split('\n')[0]);
            else if (job.Error.Length > 0) Diagnostics.ManagerLog.Info(line + " · 先前错误：" + Plain(job.Error).Split('\n')[0]);
            else Diagnostics.ManagerLog.Info(line);
        }
        foreach (string gone in _reportedJobStates.Keys.Where(id => !seen.Contains(id)).ToArray()) _reportedJobStates.Remove(gone);
        string reading = readError.Length > 0 ? readError : snapshot.Error.Length > 0 ? snapshot.Error : "";
        if (reading != _reportedReadError)
        {
            _reportedReadError = reading;
            if (reading.Length > 0) Diagnostics.ManagerLog.Error("队列读取/助手诊断：" + Plain(reading).Split('\n')[0]);
        }
    }

    /// <summary>Background-thread safe: only the shared message catalogue is touched, never Unity or Loc.</summary>
    private static string Plain(string text)
        => ModConfigEnhance.Updating.UpdateMessages.TryDecode(text, out var term, out var argument) && term != null ? term.SimplifiedChinese + argument : text;
    private static string JobStateLabel(string state) => state switch
    {
        UpdateStates.Queued => "排队中",
        UpdateStates.Resolving => "读取版本",
        UpdateStates.Downloading => "下载中",
        UpdateStates.Paused => "已暂停",
        UpdateStates.Verifying => "检查包内容",
        UpdateStates.Preparing => "准备写入",
        UpdateStates.WaitingExit => "下载成功 ✓ · 等待退出游戏后部署",
        UpdateStates.Deploying => "正在安装",
        UpdateStates.PendingRestart => "下载成功 ✓ · 已部署 ✓ · 等待重启生效",
        UpdateStates.Completed => "已生效 ✓",
        UpdateStates.Stopped => "已停止",
        UpdateStates.Failed => "更新失败",
        UpdateStates.Removed => "已移除记录",
        UpdateStates.RollingBack => "回退中",
        UpdateStates.RollbackWaiting => "退出后回退",
        UpdateStates.RolledBack => "已回退",
        UpdateStates.Superseded => "已被接替",
        _ => state
    };
    private static string ReadContext(string root)=>root+"|"+SourceUi.DependencyContext;
    internal static void Poll()
    {
        // Capture the environment only on the UI thread; parsing never holds the UI/publication lock.
        lock(Gate)
        {
            string root=Root,context=ReadContext(root);
            if(_readContext!=context) { _readContext=context;_snapshot=new();_unsafeRead=true;_hasRead=false;_pollAt=DateTime.MinValue; }
            if(DateTime.UtcNow<_pollAt || _readTask is {IsCompleted:false})return;
            _pollAt=DateTime.UtcNow.AddMilliseconds(400);
            _readTask=System.Threading.Tasks.Task.Run(()=>RefreshRead(root,context));
        }
    }
    private static void PollSafety()
    {
        string root=Root,context=ReadContext(root);
        lock(Gate)_readContext=context;
        RefreshRead(root,context);
    }

    internal static void EnsureWorker()
    {
        EnsureWorker(Root, Paths.BepInExRootPath, null);
        lock(Gate) { _localError = ""; _error = ""; }
    }
    private static void EnsureWorker(string root, string bepRoot, string? prepared)
    {
        // Tool IO/launch serialization must never hold the UI snapshot publication lock.
        lock (WorkerGate)
        {
            string tools = prepared ?? PrepareWorkerTools(root, bepRoot, WorkerSource());
            Directory.CreateDirectory(root);
            string lockPath = Path.Combine(root, "worker-instance.lock");
            try { using var probe = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return; }
            lock(Gate) { if (DateTime.UtcNow < _launchAt) return; }
            var start = new ProcessStartInfo(Path.Combine(tools, ModConfigEnhance.Updating.ManagerIdentity.WorkerExecutable), "--root \"" + bepRoot.TrimEnd('\\', '/') + "\" --game-root \"" + Paths.GameRootPath.TrimEnd('\\', '/') + "\" --protocol 2 --language " + Loc.T("en-us", "zh-cn", "zh-tw") + (Plugin.DebugLogging.Value ? " --debug" : "")) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = tools
            };
            Diagnostics.ManagerLog.Debug("启动更新助手：" + Path.GetFileName(start.FileName) + " " + start.Arguments);
            lock(Gate) _workerAttemptUtc = DateTime.UtcNow;
            using var launched = Process.Start(start) ?? throw new IOException("Could not start the update worker.");
            lock(Gate) _launchAt = DateTime.UtcNow.AddSeconds(3);
        }
    }

    private static string WorkerSource() => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "Tools", "Updater");
    internal static string PrepareWorkerTools() => PrepareWorkerTools(Root, Paths.BepInExRootPath, WorkerSource());
    private static string PrepareWorkerTools(string root, string bepRoot, string source)
    {
        lock(WorkerGate)
        {
            string index = Path.Combine(root, "packs", "index.json");
            string boundary = Path.GetDirectoryName(bepRoot.TrimEnd('\\', '/'))!;
            WorkerProtocol.RejectLinks(boundary, index);
            if (File.Exists(index))
            {
                var data = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(index));
                WorkerProtocol.RequireProtocol(data.Value<int?>("SchemaVersion") ?? 1, data.Value<int?>("MinimumWorkerProtocol") ?? 1);
            }
            WorkerProtocol.RequireResourceTools(source);
            string tools = WorkerProtocol.PrepareTools(source, Path.Combine(root, "tools"), sourceBoundary: boundary, cacheBoundary: boundary);
            RecoveryLauncher.Write(root, tools, Paths.GameRootPath);
            return tools;
        }
    }
    internal static Process StartProbe(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid probe ID");
        string tools = PrepareWorkerTools();
        var start = new ProcessStartInfo(Path.Combine(tools, ModConfigEnhance.Updating.ManagerIdentity.WorkerExecutable), "--root \"" + Paths.BepInExRootPath.TrimEnd('\\', '/') + "\" --protocol 2 --probe " + id) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = tools };
        start.EnvironmentVariables["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(Root, "tools", ".extract");
        return Process.Start(start) ?? throw new IOException("Could not start the connection test.");
    }
    private static List<string> LoadedPaths() => AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic)
        .Select(a => { try { return a.Location; } catch { return ""; } }).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static int ProcessId { get { using var process = Process.GetCurrentProcess(); return process.Id; } }
    private static void Post(UpdateCommand command, Action<string>? posted = null)
    {
        lock(CommandGate)
        {
            System.Threading.Interlocked.Increment(ref _commandEpoch);
        if (command.Action != "pack" && command.Action != "observe" && command.Action != "network") PackLibrary.EnsureAvailable();
        // Validate before publishing any command, then launch from that same validated copy: preparing twice hashed the
        // whole helper release six times on the main thread for every click.
        string root = Root, bepRoot = Paths.BepInExRootPath;
        string tools = PrepareWorkerTools(root, bepRoot, WorkerSource());
        string queueFile = Path.Combine(Root,"queue.json");
        if (File.Exists(queueFile))
        {
            var worker = JsonConvert.DeserializeObject<UpdateSnapshot>(ReadText(queueFile)) ?? throw new InvalidDataException("Empty worker queue.");
            bool alive = false;
            if (worker.WorkerProcessId > 0)
                try { using var process = Process.GetProcessById(worker.WorkerProcessId); alive = !process.HasExited; }
                catch (ArgumentException) { }
            WorkerProtocol.RequireResourceWorker(worker, alive);
        }
        command.MinimumWorkerProtocol = WorkerProtocol.Current;
        command.SchemaVersion = 2; // Old workers reject even when the index remains legacy.
        if (command.Id.Length == 0) command.Id = Guid.NewGuid().ToString("N"); command.SourceProcessId = ProcessId; command.LoadedAssemblyPaths = LoadedPaths();
        string commands = Path.Combine(Root, "commands"); Directory.CreateDirectory(commands);
        string file = Path.Combine(commands, DateTime.UtcNow.Ticks + "_" + command.Id + ".json");
        string temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(command, Formatting.Indented)); File.Move(temporary, file);
        _localError = ""; _error = "";
        posted?.Invoke(Path.GetFileName(file));
        Diagnostics.ManagerLog.Debug($"发送命令 {command.Action} id={command.Id.Substring(0, Math.Min(8, command.Id.Length))} job={(command.JobId.Length > 0 ? command.JobId.Substring(0, Math.Min(8, command.JobId.Length)) : "-")} 文件={Path.GetFileName(file)} 载入程序集={(command.LoadedAssemblyPaths?.Count ?? 0)}");
        try { EnsureWorker(root, bepRoot, tools); lock(Gate) { _localError = ""; _error = ""; } }
        catch (Exception error) { _localError = _error = error.Message; Diagnostics.ManagerLog.Error("更新助手启动失败：" + error.Message, error); } // The durable command is already published; do not misreport it as unsubmitted.
        _pollAt = DateTime.MinValue;
        }
    }
    internal static void SendLocalImport(LocalImportRequest request)
    {
        Diagnostics.ManagerLog.Info($"本地导入：{request.Operation} · 来源 {request.SourcePath}" + (request.PackageFolder.Length > 0 ? " · 目录 " + request.PackageFolder : ""));
        if (request.Operation == "preview") { SubmitUpdates(new[] { new UpdateCommand { Id = Guid.NewGuid().ToString("N"), Action = "local-import", LocalImport = request } }); return; }
        request.SourceProcessId = ProcessId; Post(new UpdateCommand { Action = "local-import", LocalImport = request });
    }
    internal static void SendLayout(LayoutQueueRequest request)
    {
        Diagnostics.ManagerLog.Info($"目录整理：{request.Operation} · 模式 {request.Mode} · {request.Groups.Count} 个分组 / {request.Mods.Count} 个模组");
        Diagnostics.ManagerLog.Debug($"整理快照 {request.AppliedSnapshotPath} 哈希 {request.AppliedSnapshotHash}");
        request.SourceProcessId = ProcessId; Post(new UpdateCommand { Action = "layout", Layout = request });
    }
    internal static void SendShop(ShopRequest request)
    {
        Diagnostics.ManagerLog.Info($"商店操作：{request.Operation} · {request.Namespace}-{request.Package} {request.Version}");
        if (request.Operation is "preview-install" or "preview-uninstall")
        { SubmitUpdates(new[] { new UpdateCommand { Id = Guid.NewGuid().ToString("N"), Action = "shop", Shop = request } }); return; }
        request.SourceProcessId=ProcessId; Post(new UpdateCommand{Action="shop",Shop=request});
    }
    internal static void SendPack(PackRequest request)
    {
        request.SourceProcessId = ProcessId;
        Post(new UpdateCommand { Action = "pack", Pack = request });
    }
    internal static void Send(string action, string jobId, bool restoreParameters = false, string resourceBatchId = "")
    {
        try { Post(new UpdateCommand { Action = action, JobId = jobId, RestoreParameters = restoreParameters, ResourceBatchId = resourceBatchId }); }
        catch (Exception error) { _localError = _error = error.Message; Plugin.Log.LogError("[更新队列] " + error); Diagnostics.ManagerLog.Error("队列操作失败：" + action + " · " + error.Message, error); }
    }
    internal static void SetNetwork(string mode, string address, Action<string>? posted = null)
    {
        if (mode != "system" && mode != "direct" && mode != "custom") throw new ArgumentException("Unknown proxy mode");
        if (mode == "custom" && (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException(Loc.T("Use an HTTP(S) proxy address without credentials.", "请使用不带账户密码的 HTTP(S) 代理地址。", "請使用不含帳號密碼的 HTTP(S) 代理位址。"));
        try { Post(new UpdateCommand { Action = "network", Network = new UpdateNetworkOptions { Mode = mode, ProxyAddress = mode == "custom" ? address : "" } }, posted); }
        catch (Exception error) { _localError = _error = error.Message; throw; }
    }
    // Existing worker protocol: files move here only after Handle has finished.
    // This is an acknowledgement of processing, NOT an error/success receipt.
    internal static bool NetworkCommandProcessed(string id) => Path.GetFileName(id) == id && id.EndsWith(".json", StringComparison.Ordinal) &&
        File.Exists(Path.Combine(Root, "processed", id));
    internal static void Reconcile()
    {
        Poll();
        string context = _readContext;
        if (_observeTask is {IsCompleted:true})
        {
            var result = _observeTask.GetAwaiter().GetResult(); _observeTask = null;
            // An observation that did not reach the helper must be retried, not counted as asked.
            if (!result.Executed || result.Error.Length > 0) _observedPending = "";
            if (result.Context == context && result.Epoch == System.Threading.Interlocked.Read(ref _commandEpoch))
            {
                lock(Gate) {
                    if(result.Executed) { _observeError=result.Error;_error=_localError.Length>0?_localError:_observeError.Length>0?_observeError:_queueError;_pollAt=DateTime.MinValue; }
                }
            }
        }
        if (_observeTask != null || DateTime.UtcNow < _observeAt || !_snapshot.Jobs.Any(j => UpdateStates.Pending(j.State))) return;
        // A pending-restart job is confirmed by what this launch loaded, which does not change within it: ask once per
        // launch for the same set, instead of starting the helper every 10 seconds for as long as the game runs.
        var pending = _snapshot.Jobs.Where(j => UpdateStates.Pending(j.State)).ToArray();
        string observed = string.Join("|", pending.Select(j => j.Id + ":" + j.State).OrderBy(x => x, StringComparer.Ordinal));
        if (pending.All(j => j.State == UpdateStates.PendingRestart) && observed == _observedPending) return;
        _observeAt = DateTime.UtcNow.AddSeconds(10);
        _observedPending = observed;
        // Capture all runtime/Unity-owned inputs here; the worker only handles immutable values and IO.
        try
        {
        string root = Root, bepRoot = Paths.BepInExRootPath, source = WorkerSource();
        var command = new UpdateCommand { Action = "observe", MinimumWorkerProtocol = WorkerProtocol.Current,
            SchemaVersion = 2, Id = Guid.NewGuid().ToString("N"), SourceProcessId = ProcessId, LoadedAssemblyPaths = LoadedPaths() };
        long epoch = System.Threading.Interlocked.Read(ref _commandEpoch);
        _observeTask = System.Threading.Tasks.Task.Run(() => Observe(root, bepRoot, source, context, epoch, command));
        }
        catch(Exception error) { _observedPending=""; lock(Gate) { _observeError=error.Message;_error=_localError.Length>0?_localError:_observeError; } }
    }
    private static ObserveResult Observe(string root, string bepRoot, string source, string context, long epoch, UpdateCommand command)
    {
        var result = new ObserveResult {Context=context, Epoch=epoch};
        try
        {
            lock(CommandGate)
            {
                // An explicit command supersedes an observation which has not started yet.
                if(epoch != System.Threading.Interlocked.Read(ref _commandEpoch)) return result;
                result.Executed=true;
                string tools = PrepareWorkerTools(root, bepRoot, source);
                lock(Gate) { if(context != _readContext) { result.Executed=false;return result; } }
                string commands = Path.Combine(root, "commands"); Directory.CreateDirectory(commands);
                string file = Path.Combine(commands, DateTime.UtcNow.Ticks + "_" + command.Id + ".json");
                File.WriteAllText(file + ".tmp", JsonConvert.SerializeObject(command, Formatting.Indented)); File.Move(file + ".tmp", file);
                // The exact validated tool directory is reused inside this command; no safety checks are bypassed.
                EnsureWorker(root, bepRoot, tools);
            }
        }
        catch(Exception error) { result.Error = error.Message; }
        return result;
    }

    internal static bool HasPendingForPackage(string packageRoot)
    {
        PollSafety();
        return _unsafeRead || _snapshot.Jobs.Any(j => SamePath(j.PackageRoot, packageRoot) && UpdateStates.BlocksPublication(j));
    }
    internal static string PreparedVersion(string guid, string packageRoot)
    {
        Poll();
        return _snapshot.Jobs.LastOrDefault(j => SamePath(j.PackageRoot, packageRoot) && j.PluginGuids.Contains(guid, StringComparer.OrdinalIgnoreCase)
            && (j.State == UpdateStates.PendingRestart || j.State == UpdateStates.WaitingExit))?.TargetVersion ?? "";
    }
    internal static void EnsurePublicationSafe(IEnumerable<string> packageRoots)
    {
        PollSafety();
        if (_unsafeRead) throw new InvalidOperationException(_error);
        var recovery = _snapshot.Jobs.FirstOrDefault(j => j.RecoveryRequired);
        if (recovery != null) throw new InvalidOperationException(Loc.F("Finish recovering this package before publishing: {0}", "请先完成此包的文件恢复，再生成发布包：{0}", "請先完成此套件的檔案復原，再產生發布包：{0}", recovery.DisplayName));
        foreach (string root in packageRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            bool candidate = Directory.Exists(root) ? OverviewRepository.SafeFiles(root).Any(OverviewCatalogue.IsUpdateCandidate) : OverviewCatalogue.IsUpdateCandidate(root);
            if (HasPendingForPackage(root) || candidate)
                throw new InvalidOperationException(Loc.F("Finish or undo this package's pending update and restart before publishing: {0}", "此资料包有未完成或待重启的更新，请完成或撤销并重启后再发布：{0}", "此資料包有未完成或待重新啟動的更新，請完成或撤銷並重新啟動後再發布：{0}", root));
        }
    }
    private static bool SamePath(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    internal static UpdateCommand ComponentUpdateCommand(InfrastructurePackages.Package package, string? targetVersion = null)
    {
        string latest = targetVersion ?? package.Latest;
        int split = package.Key.IndexOf('-');
        if (!package.IdentityConfirmed || split < 1 || !ThunderstoreUpdateService.IsNewer(latest, package.Version))
            throw new InvalidOperationException("No verified component update target.");
        if (ManifestCopies.Contradicts(package.Version, latest, CompletedVersion(package.Key.Substring(0, split), package.Key.Substring(split + 1))))
            throw new InvalidOperationException("No verified component update target.");
        return new UpdateCommand { Id = Guid.NewGuid().ToString("N"), Action = "shop", Shop = new ShopRequest { Operation = "preview-install", Namespace = package.Key.Substring(0, split),
            Package = package.Key.Substring(split + 1), Version = latest } }; 
    }
    internal static void Enqueue(OverviewRepository repo, IReadOnlyList<OverviewMod> mods, Action<string> status)
    {
        EnqueueCore(repo, mods, status, mods.Count != 1, null);
        OverviewUi.ShowText(Loc.LocalizeMessage("Update queue notice"), Feedback);
    }
    internal static void SetFeedback(string message, Action<string> status) { _feedback = message; status(message); }
    internal static void EnqueueAll(OverviewRepository repo, IReadOnlyList<OverviewMod> mods, Action<string> status, IEnumerable<UpdateCommand>? components = null) => EnqueueCore(repo, mods, status, true, components);
    private static void EnqueueCore(OverviewRepository repo, IReadOnlyList<OverviewMod> mods, Action<string> status, bool batch, IEnumerable<UpdateCommand>? components)
    {
        int added = 0, skipped = 0; var reasons = new List<string>();
        var commands = components?.ToList() ?? new List<UpdateCommand>();
        foreach (var component in commands) reasons.Add(component.Shop!.Namespace + "-" + component.Shop.Package + " → " + component.Shop.Version);
        added = commands.Count;
        foreach (var mod in mods.GroupBy(m => m.PackageRoot, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(m => m.HasUpdate).First()))
        {
            try
            {
                // Update all operates on known available updates, not every installed row.
                if (batch && !mod.HasUpdate) continue;
                if (!mod.SourceConfirmed || mod.SourceKind != ModSourceKind.Store || !ThunderstoreUpdateService.ValidPart(mod.SourceNamespace) || !ThunderstoreUpdateService.ValidPart(mod.SourceName))
                    throw new InvalidOperationException(Loc.T("Confirm the store source first.", "请先确认商店来源。", "請先確認商店來源。"));
                if (mod.IdentityConflict.Length > 0) throw new InvalidOperationException(mod.IdentityConflict);
                if (!Directory.Exists(mod.PackageRoot)) throw new InvalidOperationException(Loc.T("Automatic updates require a separate package directory.", "自动更新需要独立的模组目录。", "自動更新需要獨立模組目錄。"));
                if (!repo.IsUpdateAllowed(mod.Rule)) { skipped++; reasons.Add(mod.Name + Loc.T(": update checks are not allowed for this mod", "：此模组未获更新检查许可", "：此模組未獲更新檢查許可")); continue; }
                if (HasPendingForPackage(mod.PackageRoot)) { skipped++; reasons.Add(mod.Name + Loc.T(": already in the update queue", "：已在更新队列中", "：已在更新佇列中")); continue; }
                if (!Version.TryParse(mod.PackageVersion, out var installed)) throw new InvalidOperationException(Loc.T("The installed store-package version is unknown. Verify the source first.", "当前商店包版本不明，请先核对来源。", "目前商店套件版本不明，請先核對來源。"));
                if (!UpdateQueuePolicy.LatestTarget(mod.HasUpdate, mod.PackageVersion, mod.LatestVersion, out string target, out string reason))
                { skipped++; reasons.Add(mod.Name + "：" + Loc.LocalizeMessage(reason)); continue; }
                // Core/patcher entries must use the existing store ComponentActions preview/confirmation, not ordinary writes.
                if (!UpdateWritePolicy.PackageRootAllowed(Paths.BepInExRootPath, mod.PackageRoot) || PackageRules.IsLoader(mod.SourceKey))
                {
                    skipped++; reasons.Add(mod.Name + Loc.T(": protected component; open its store version and review the component action preview (Core stays read-only)", "：受保护组件；请打开商店对应版本，审阅组件操作预览（Core 仍只读）", "：受保護元件；請開啟商店對應版本，審閱元件操作預覽（Core 仍唯讀）")); continue;
                }
                commands.Add(BuildUpdateCommand(repo, mod, target, false)); added++; reasons.Add(mod.Name + "：" + mod.PackageVersion + " → " + target);
            }
            catch (Exception error) { skipped++; reasons.Add(mod.Name + ": " + Loc.LocalizeMessage(error.Message)); }
        }
        if (commands.Count > 0)
        {
            try { SubmitUpdates(commands); }
            catch (Exception error) { skipped += added; added = 0; reasons.Add(Loc.LocalizeMessage(error.Message)); }
        }
        _feedback = Loc.F("Submitted {0} updates; skipped {1}. Downloads start independently of configuration saves.", "已提交 {0} 项更新，跳过 {1} 项。下载独立开始，无需保存配置。", "已提交 {0} 項更新，略過 {1} 項。下載獨立開始，無需儲存設定。", added, skipped) + (reasons.Count > 0 ? "\n" + string.Join("\n", reasons) : "");
        status(_feedback);
    }
    // Explicit author-version restore is never used by Enqueue/EnqueueAll (update-latest).
    internal static void EnqueueModpackVersion(OverviewRepository repo, OverviewMod mod, Action<string> status)
    {
        try
        {
            string target = AuthorVersion(repo, mod);
            if (target.Length == 0) throw new InvalidOperationException(Loc.T("No modpack version was recorded.", "未记录整合包指定版本。", "未記錄整合包指定版本。"));
            EnqueueTarget(repo, mod, target, status);
        }
        catch (Exception error) { status(error.Message); }
    }
    internal static void EnqueueTarget(OverviewRepository repo, OverviewMod mod, string target, Action<string> status)
    {
        try {
            var action=ComponentActions.Select(mod.PackageVersion,target);
            if(!mod.SourceConfirmed||mod.SourceKind!=ModSourceKind.Store||!ThunderstoreUpdateService.ValidPart(mod.SourceNamespace)||!ThunderstoreUpdateService.ValidPart(mod.SourceName))throw new InvalidOperationException("Confirm the store source first.");
            if(mod.IdentityConflict.Length>0)throw new InvalidOperationException(mod.IdentityConflict);
            if(!repo.IsUpdateAllowed(mod.Rule))throw new InvalidOperationException("Updates are not allowed for this mod.");
            if(!Directory.Exists(mod.PackageRoot))throw new InvalidOperationException("Independent package directory required.");
            if(HasPendingForPackage(mod.PackageRoot))throw new InvalidOperationException("Resolve the existing queue entry first.");
            OverviewUi.ConfirmText(Loc.T("Confirm selected version","确认选中版本","確認選取版本"),mod.Name+"\n"+mod.PackageVersion+" → "+target+"\n"+action+"\n"+Loc.T("Files and enablement are backed up; configuration is retained.","备份文件和启停状态，保留配置。","備份檔案和啟停狀態，保留設定。"),()=>{SubmitUpdates(new[] { BuildUpdateCommand(repo,mod,target,action==ComponentAction.Downgrade,action==ComponentAction.Reinstall) });status(Loc.T("Selected version queued.","选中版本已加入队列。","選取版本已加入佇列。"));});
        } catch(Exception error){status(error.Message);}
    }
    private static UpdateCommand BuildUpdateCommand(OverviewRepository repo, OverviewMod mod, string target, bool downgrade, bool reinstall=false)
    {
        if (!UpdateWritePolicy.PackageRootAllowed(Paths.BepInExRootPath, mod.PackageRoot) || PackageRules.IsLoader(mod.SourceKey))
            throw new InvalidOperationException(Loc.T("Protected component: use the store component action preview; Core stays read-only.", "受保护组件：请使用商店组件操作预览；Core 仍只读。", "受保護元件：請使用商店元件操作預覽；Core 仍唯讀。"));
        var job = new UpdateJob {
            Id = Guid.NewGuid().ToString("N"), Namespace = mod.SourceNamespace, Package = mod.SourceName, DisplayName = OverviewLanguageBridge.GetDisplayName(mod),
            PackageRoot = Path.GetFullPath(mod.PackageRoot), InstalledVersion = mod.PackageVersion, TargetVersion = target, RunningVersion = mod.Loaded ? mod.PluginVersion : "",
            Homepage = mod.HomeUrl, Enabled = mod.TargetEnabled, AllowDowngrade = downgrade, Reinstall = reinstall, PluginGuids = new List<string>(mod.PluginGuids),
            AssemblyPaths = new List<string>(mod.AssemblyPaths), SourceProcessId = ProcessId, LoadedAssemblyPaths = LoadedPaths(), CreatedUtc = DateTime.UtcNow.ToString("O")
        };
        if (!job.PluginGuids.Contains(mod.Guid, StringComparer.OrdinalIgnoreCase)) job.PluginGuids.Add(mod.Guid);
        var guids = new HashSet<string>(job.PluginGuids, StringComparer.OrdinalIgnoreCase);
        job.ConfigurationFiles = repo.Configs.LoadedEntries().Where(p => guids.Contains(p.Key.Split('\n')[0]))
            .Select(p => p.Value.ConfigFile.ConfigFilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new UpdateCommand { Id = Guid.NewGuid().ToString("N"), Action = "add", Job = job };
    }
    internal static void StartPendingUpdates()
    {
        if (ResourceOperationDraft.AwaitingAcknowledgement) { EnsureWorker(); return; }
        var updates = ResourceOperationDraft.PendingUpdates();
        SubmitUpdates(updates);
        ResourceOperationDraft.RemoveSubmittedUpdates(updates.Select(c => c.Id));
    }
    internal static string SubmitUpdates(IEnumerable<UpdateCommand> updates)
    {
        var commands = updates.ToList();
        if (commands.Count == 0) return "";
        foreach (var command in commands)
        {
            command.SourceProcessId = ProcessId; command.LoadedAssemblyPaths = LoadedPaths();
            if (command.Shop != null) command.Shop.SourceProcessId = ProcessId;
            if (command.LocalImport != null) command.LocalImport.SourceProcessId = ProcessId;
        }
        // Explicit Update is download consent, never a configuration/profile save.
        string id = Guid.NewGuid().ToString("N");
        var described = commands.Select(DescribeCommand).ToArray();
        Diagnostics.ManagerLog.Info($"提交更新 {commands.Count} 项：" + string.Join("；", described));
        Diagnostics.ManagerLog.Debug($"update-batch id={id} 载入程序集 {commands[0].LoadedAssemblyPaths?.Count ?? 0} 个 · 活动整合包 {PackLibrary.Read().ActivePackId}");
        Post(new UpdateCommand { Id = id, Action = "update-batch", ResourcePackId = PackLibrary.Read().ActivePackId, ResourceCommands = commands });
        return id;
    }
    /// <summary>One readable line per submitted item for the normal log layer.</summary>
    private static string DescribeCommand(UpdateCommand command)
    {
        if (command.Job != null) return (command.Job.DisplayName.Length > 0 ? command.Job.DisplayName : command.Job.Package) + " " + command.Job.InstalledVersion + "→" + cmdTarget(command.Job.TargetVersion);
        if (command.Shop != null) return command.Shop.Operation + " " + command.Shop.Namespace + "-" + command.Shop.Package + " " + command.Shop.Version;
        if (command.LocalImport != null) return "local-import " + command.LocalImport.PackageFolder;
        return command.Action;
        static string cmdTarget(string target) => target.Length > 0 ? target : "最新";
    }
    internal static string AuthorVersion(OverviewRepository repo, OverviewMod mod)
    {
        var profiles = repo.Profiles.ListProfiles();
        var profile = repo.ViewedProfile.readOnly ? repo.ViewedProfile : profiles.FirstOrDefault(p => p.readOnly && p.id == repo.ViewedProfile.source);
        var pack = PublishedPack.Active;
        if (profile == null && pack != null)
            profile = profiles.FirstOrDefault(p => p.readOnly && p.directory.StartsWith(pack.Root, StringComparison.OrdinalIgnoreCase)
                && (p.id == pack.DefaultProfile || p.id.EndsWith(":" + pack.DefaultProfile, StringComparison.Ordinal)));
        var recorded = profile?.mods.FirstOrDefault(m => string.Equals(m.guid, mod.Guid, StringComparison.OrdinalIgnoreCase));
        if (recorded == null) return "";
        if (!Version.TryParse(recorded.packageVersion, out _)) throw new InvalidOperationException(Loc.T("The author profile has no verified package version for this mod.", "作者方案未记录此模组的已确认包版本。", "作者方案未記錄此模組的已確認套件版本。"));
        return recorded.packageVersion;
    }
}

internal sealed partial class OverviewListView
{
    internal void QueueSelectedUpdates() => OverviewUpdateQueue.Enqueue(_repo, Selected(), _status);
}
