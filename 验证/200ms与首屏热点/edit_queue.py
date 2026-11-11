from pathlib import Path
p=Path('src/Overview/OverviewUpdateQueue.cs');s=p.read_text(encoding='utf-8-sig')
s=s.replace('private static readonly object Gate = new();','private static readonly object Gate = new();\n    private static readonly object CommandGate = new();\n    private static readonly object WorkerGate = new();\n    private static System.Threading.Tasks.Task<ObserveResult>? _observeTask;\n    private static long _commandEpoch;\n    private sealed class ObserveResult\n    {\n        internal string Context="", Error="";\n        internal long Epoch;\n        internal double PrepareMs, PublishMs, LaunchMs;\n    }')
a=s.index('    internal static void EnsureWorker()');b=s.index('    internal static Process StartProbe',a)
s=s[:a]+'''    internal static void EnsureWorker() => EnsureWorker(Root, Paths.BepInExRootPath, null);
    private static void EnsureWorker(string root, string bepRoot, string? prepared)
    {
        // Tool IO/launch serialization must never hold the UI snapshot publication lock.
        lock (WorkerGate)
        {
            string tools = prepared ?? PrepareWorkerTools(root, bepRoot, WorkerSource());
            Directory.CreateDirectory(root);
            string lockPath = Path.Combine(root, "worker.lock");
            try { using var probe = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { return; }
            lock(Gate) { if (DateTime.UtcNow < _launchAt) return; }
            var start = new ProcessStartInfo(Path.Combine(tools, "MCE.UpdateWorker.exe"), "--root \\"" + bepRoot.TrimEnd('\\', '/') + "\\" --protocol 2") {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = tools
            };
            start.EnvironmentVariables["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(root, "tools", ".extract");
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
            string tools = WorkerProtocol.PrepareTools(source, Path.Combine(root, "tools"), sourceBoundary: boundary, cacheBoundary: boundary);
            RecoveryLauncher.Write(root, tools);
            return tools;
        }
    }
''' +s[b:]
a=s.index('        if (command.Action != "pack"');b=s.index('    internal static void SendLocalImport',a)
old=s[a:b];body=old[:old.rfind('    }')]
s=s[:a]+'''        lock(CommandGate)
        {
            System.Threading.Interlocked.Increment(ref _commandEpoch);
'''+body+'''        }
    }
'''+s[b:]
a=s.index('    internal static void Reconcile()');b=s.index('\n    internal static bool HasPendingForPackage',a)
s=s[:a]+'''    internal static void Reconcile()
    {
        Poll();
        string context = _readContext;
        if (_observeTask is {IsCompleted:true})
        {
            var result = _observeTask.GetAwaiter().GetResult(); _observeTask = null;
            if (result.Context == context && result.Epoch == System.Threading.Interlocked.Read(ref _commandEpoch))
            {
                lock(Gate) { _localError = _error = result.Error; _pollAt = DateTime.MinValue; }
                Plugin.Log.LogDebug($"[更新队列] 自动observe prepare={result.PrepareMs:F1}ms publish={result.PublishMs:F1}ms launch={result.LaunchMs:F1}ms error={result.Error}");
            }
        }
        if (_observeTask != null || DateTime.UtcNow < _observeAt || !_snapshot.Jobs.Any(j => UpdateStates.Pending(j.State))) return;
        _observeAt = DateTime.UtcNow.AddSeconds(10);
        // Capture all runtime/Unity-owned inputs here; the worker only handles immutable values and IO.
        string root = Root, bepRoot = Paths.BepInExRootPath, source = WorkerSource();
        var command = new UpdateCommand { Action = "observe", MinimumWorkerProtocol = WorkerProtocol.Current,
            SchemaVersion = 2, Id = Guid.NewGuid().ToString("N"), SourceProcessId = ProcessId, LoadedAssemblyPaths = LoadedPaths() };
        long epoch = System.Threading.Interlocked.Read(ref _commandEpoch);
        _observeTask = System.Threading.Tasks.Task.Run(() => Observe(root, bepRoot, source, context, epoch, command));
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
                var timer = Stopwatch.StartNew();
                string tools = PrepareWorkerTools(root, bepRoot, source); result.PrepareMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                string commands = Path.Combine(root, "commands"); Directory.CreateDirectory(commands);
                string file = Path.Combine(commands, DateTime.UtcNow.Ticks + "_" + command.Id + ".json");
                File.WriteAllText(file + ".tmp", JsonConvert.SerializeObject(command, Formatting.Indented)); File.Move(file + ".tmp", file);
                result.PublishMs = timer.Elapsed.TotalMilliseconds; timer.Restart();
                // The exact validated tool directory is reused inside this command; no safety checks are bypassed.
                EnsureWorker(root, bepRoot, tools); result.LaunchMs = timer.Elapsed.TotalMilliseconds;
            }
        }
        catch(Exception error) { result.Error = error.Message; }
        return result;
    }
''' +s[b:]
p.write_text(s,encoding='utf-8')
