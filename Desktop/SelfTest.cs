using System;
using System.IO;
using System.Text;

namespace MCE.Desktop;
internal static class SelfTest
{
    internal static int Run()
    {
        // Isolated under the project output, never the game/MCE runtime directory.
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        int checks = 0;
        void Check(bool condition) { if (!condition) throw new Exception("Self-test failed at " + checks); checks++; }
        Check(Protocol.ValidId(new string('a', 32)));
        Check(!Protocol.ValidId("../" + new string('a', 32)));
        Check(!Protocol.ValidId(new string('a', 32) + "\n"));
        string path = Path.Combine(dir, "pack-task.json");
        File.WriteAllText(path, "{\"SchemaVersion\":1,\"Id\":\"" + new string('b', 32) + "\",\"State\":\"waiting-exit\",\"Request\":{\"Operation\":\"confirm-switch\"},\"Proposed\":{\"Name\":\"Fixture\"},\"UnknownFutureField\":true}", Encoding.UTF8);
        using (var shared = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            Check(Protocol.ReadTask(dir).State == "waiting-exit");
        var plan = Protocol.Json().Deserialize<PlanState>("{\"Removed\":[\"a\"],\"Placed\":[\"b\"],\"Originals\":[{},{}],\"Desired\":[{}]}");
        Check(plan.Done == 2 && plan.Total == 3);
        string redacted = Protocol.Redact("root=" + dir + "\ntoken=secret\nhttps://name:pass@host/path?key=secret", dir);
        Check(!redacted.Contains("secret") && !redacted.Contains("name:pass") && !redacted.Contains(dir));
        Check(Protocol.Quote(@"C:\fixture path\") == "\"C:\\fixture path\"");
        bool rejected = false;
        try { Protocol.ValidateTools(dir); } catch (IOException) { rejected = true; }
        Check(rejected);
        rejected = false;
        File.WriteAllText(path, "{\"SchemaVersion\":3,\"Id\":\"" + new string('b', 32) + "\"}");
        try { Protocol.ReadTask(dir); } catch (IOException) { rejected = true; }
        Check(rejected);
        Check(!Directory.Exists(Path.Combine(dir, "commands")));
        Directory.CreateDirectory(Path.Combine(dir, "shop"));
        File.WriteAllText(Path.Combine(dir, "shop", "task.json"), "{\"SchemaVersion\":1,\"Id\":\"" + new string('c', 32) + "\",\"State\":\"waiting-exit\",\"Request\":{\"Operation\":\"layout-confirm\"},\"Originals\":[{}],\"Desired\":[{},{}]}");
        var installation = Protocol.ReadTask(dir, new string('c', 32));
        Check(installation.Request.Operation == "layout-confirm" && installation.Desired.Length == 2);
        Check(installation.Proposed.Name == "Folder organization");
        File.WriteAllText(Path.Combine(dir, "result.txt"), "PASS " + checks + " checks; no worker launched or queue created.");
        return 0;
    }
}
