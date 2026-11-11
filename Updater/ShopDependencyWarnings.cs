using ModConfigEnhance.Updating;
namespace MCE.UpdateWorker;

internal sealed partial class ShopQueue
{
    internal List<string> ConsumerIssues(ShopTask task)=>CheckConsumers(task,task.Uninstall);
    void AddDependencyIssues(ShopTask task, IEnumerable<string> issues)
    {
        if (analysisOnly) return; // Combined batch tree is validated after all payloads are prepared.
        var values = issues.Distinct().ToList();
        // Duplicate identities are installation ambiguity, not a dependency warning.
        var identityIssues = values.Where(i => i.StartsWith("Duplicate plugin GUID:", StringComparison.Ordinal)).ToList();
        task.Blockers.AddRange(identityIssues);
        values.RemoveAll(i => identityIssues.Contains(i));
        if (task.Uninstall)
        {
            foreach (string issue in values)
                if (!task.DependencyWarnings.Contains(issue)) task.DependencyWarnings.Add(issue);
        }
        else task.Blockers.AddRange(values);
    }
    internal static string DependencyWarningToken(IEnumerable<string> issues)
    {
        string content = string.Join("\n", issues.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
        return FrameworkCompat.Hex(FrameworkCompat.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
    }
    void RequireDependencyConsent(ShopTask task, IEnumerable<string> issues)
    {
        var values = issues.Distinct().ToList();
        if (values.Count == 0) return;
        if (values.Any(i => i.StartsWith("Duplicate plugin GUID:", StringComparison.Ordinal)))
            throw new IOException(string.Join("\n", values));
        if (task.Uninstall && task.Request.IgnoreDependencyWarnings)
        {
            // Older direct clients may consent at preview time. New UI/batches always
            // bind consent to the exact warning set and revalidate it on execution.
            if (task.Request.DependencyWarningToken.Length == 0) return;
            if (task.Request.DependencyWarningToken == DependencyWarningToken(task.DependencyWarnings)
                && values.All(v => task.DependencyWarnings.Contains(v, StringComparer.Ordinal))) return;
        }
        throw new IOException("Dependency warning; explicit consent required:\n" + string.Join("\n", values));
    }
}
