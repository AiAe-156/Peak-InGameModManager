using System;
using System.IO;
using System.Windows;

namespace MCE.Desktop;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest.Run();
            if (args.Length != 6 || args[0] != "--root" || args[2] != "--task" || args[4] != "--language" || !Protocol.ValidId(args[3]))
                throw new ArgumentException("Usage: IGMM.Desktop.exe --root <BepInEx directory> --task <32 hex task ID> --language en-us|zh-cn|zh-tw");
            if (args[5] != "en-us" && args[5] != "zh-cn" && args[5] != "zh-tw") throw new ArgumentException("Unsupported language.");
            string root = Path.GetFullPath(args[1]); Protocol.NoLinks(root);
            string tools = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
            var app = new Application();
            app.Run(new ProgressWindow(root, tools, args[3], args[5]));
            return 0;
        }
        catch (Exception e)
        {
            MessageBox.Show(e.GetType().Name + ": " + Protocol.Redact(e.Message, AppDomain.CurrentDomain.BaseDirectory), "In-Game Mod Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
