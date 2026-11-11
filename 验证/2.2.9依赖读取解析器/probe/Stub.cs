namespace ModConfigEnhance.Updating;
public static class PackageRules { public static bool IsLoader(string identity) => identity.Equals("BepInEx-BepInExPack", StringComparison.OrdinalIgnoreCase) || identity.Equals("BepInEx-BepInExPack_PEAK", StringComparison.OrdinalIgnoreCase); }
