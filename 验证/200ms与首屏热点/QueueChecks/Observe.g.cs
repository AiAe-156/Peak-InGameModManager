using System;using System.Diagnostics;using System.IO;partial class Harness {
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
                result.Executed=true;
                string tools = PrepareWorkerTools(root, bepRoot, source); result.PrepareMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                lock(Gate) { if(context != _readContext) { result.Executed=false;return result; } }
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
}