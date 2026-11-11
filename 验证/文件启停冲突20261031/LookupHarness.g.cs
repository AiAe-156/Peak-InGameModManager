using ModConfigEnhance; using ModConfigEnhance.Updating; using ModConfigEnhance.Overview;
static class LookupHarness {
    private static string ExistingFile(string original) => ExistingFileForMod(original, Path.GetFileName(original));
    private static string ExistingFileForMod(string original, string modName)
    {
        // Non-plugin package assets keep the historical .old policy.
        var copies = PluginFileNames.IsAssembly(original) ? PluginFileNames.ActionableCopies(original)
            : new[] { original, original + ".old" }.Where(File.Exists).ToList();
        if (copies.Count > 1) throw new IOException(OverviewFileConflicts.Copies(modName, PluginFileNames.IsAssembly(original) ? PluginFileNames.ExistingCopies(original) : copies));
        if (copies.Count == 0) throw new IOException(Loc.F("File does not exist: {0}", "文件不存在：{0}", "文件不存在：{0}", original));
        return copies[0];
    }

}
namespace ModConfigEnhance { static class Loc { internal static string F(string en,string zh,string tw,params object[] args)=>string.Format(en,args); } }
