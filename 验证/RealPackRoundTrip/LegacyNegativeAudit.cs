using ModConfigEnhance.Overview;

internal static class LegacyNegativeAudit
{
    internal static async Task Run(OverviewRepository repo, PublishRequest request, List<OverviewMod> selected,
        Dictionary<string, (long Size, DateTime Time)> sourceStates)
    {
        int checks = 0;
        foreach (var mode in new[] { PublishMode.Local, PublishMode.Thunderstore })
        {
            request.Mode = mode;
            if (mode == PublishMode.Thunderstore)
            {
                var storePackage = selected.Single(m => m.Guid == "com.github.PEAKModding.PEAKLib.Items");
                request.InlineRoots.Remove(storePackage.PackageRoot);
                request.Dependencies[storePackage.PackageRoot] = "PEAKModding-PEAKLib_Items-1.6.2";
            }
            bool rejected = false;
            try { await PackPublisher.PublishAsync(repo, request.Snapshot()); }
            catch (InvalidOperationException error) when (error.Message.Contains("PlantholderOverhaul_ZH")
                && error.Message.Contains("default profile is missing an enabled dependency: Murka124.PlantholderOverhaul"))
            { rejected = true; Console.WriteLine("PASS " + mode + " actual legacy rejected: " + error.Message); }
            if (!rejected) throw new Exception(mode + " legacy must be rejected for the exact Plantholder dependency; not a round-trip");
            checks++;
        }
        foreach (var source in sourceStates)
        {
            var file = new FileInfo(source.Key);
            if (!file.Exists || file.Length != source.Value.Size || file.LastWriteTimeUtc != source.Value.Time)
                throw new Exception("Live source changed: " + source.Key);
        }
        Console.WriteLine("PASS source files unchanged (size/UTC mtime): " + sourceStates.Count); checks++;
        Console.WriteLine($"PASS: {checks} independent legacy negative checks; 0 publication round-trips claimed.");
    }
}
