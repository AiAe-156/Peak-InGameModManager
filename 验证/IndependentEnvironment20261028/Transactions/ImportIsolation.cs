using ModConfigEnhance.Updating;
namespace MCE.UpdateWorker;
// Import group is concurrently editing production PackImports. These tests never exercise import.
// Throwing adapter only permits compiling PackQueue without hiding an import execution failure.
internal static class PackImports
{
    internal static void Extract(string source, string destination) => throw new NotSupportedException("Import excluded from B checks");
    internal static string PlanHash(List<PackDownloadItem> items) => throw new NotSupportedException("Import excluded from B checks");
    internal static Task<PackTask> Preview(PackStorage s, PackRequest r, UpdateNetworkOptions n, HttpMessageHandler? h = null, IReadOnlyList<PackDownloadItem>? approved = null) => throw new NotSupportedException("Import excluded from B checks");
    internal static void Commit(PackStorage s, PackTask t) => throw new NotSupportedException("Import excluded from B checks");
}
