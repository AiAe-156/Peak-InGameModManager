namespace BepInEx { internal static class Paths {internal static string BepInExRootPath="";} }
namespace ModConfigEnhance.Overview {
internal static class PackLibrary {internal static bool Managed=false;internal static string Workspace="";}
internal enum ModSourceKind {Pending,Store,Local}
internal static class ModSources {internal static long ChoiceRevision=0;internal static bool ExplicitLocal(OverviewMod m)=>m.SourceKind==ModSourceKind.Local;}
internal static class OverviewPageCache {internal static int Inventory=0;}
}
namespace ModConfigEnhance.Updating {
public sealed class ShopLedger {public List<Package> Packages=new();}
public sealed class Package {public string Key="",Version="";public List<Payload> Files=new();}
public sealed class Payload {public string Path="";}
}
