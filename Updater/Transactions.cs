using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal static class Transactions
{
    internal static string PathFor(UpdateJob job) => Path.Combine(job.WorkDirectory, "transaction.json");
    internal static void Save(UpdateJob job, Transaction plan)
    {
        job.RecoveryRequired = PackageRules.HasUnfinishedWrites(plan.Phase);
        Files.Save(PathFor(job), plan);
    }
    internal sealed class NeedExitException : IOException { internal NeedExitException() : base("退出游戏后执行回退。") { } }
}
