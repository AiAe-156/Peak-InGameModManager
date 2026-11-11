namespace MCE.UpdateWorker;

/// <summary>What the foreground window shows. Built by the worker, consumed on the UI thread; plain data only.</summary>
internal sealed class JobView
{
    public string Id = "", Kind = "update", Name = "", FromVersion = "", ToVersion = "", State = "", StateText = "", Message = "", Error = "", Details = "", PackageRoot = "";
    public double Progress;
    public long Downloaded, Total;
    public List<string> Warnings = new();
    public bool CanRetry, CanRemove, Busy, Failed, Done, Waiting;
}

internal sealed class WorkerStatusSnapshot
{
    public string Root = "";
    public List<JobView> Jobs = new();
    public string Notice = "";
    public bool Idle;
    public int Active, Waiting, Failed, Done;
    public DateTime UpdatedUtc = DateTime.UtcNow;
}

/// <summary>Window → worker requests. Actions use the same vocabulary as in-game commands: retry, remove, operation-retry, operation-cancel.</summary>
internal sealed class WorkerControlRequest
{
    public string Action = "";
    public string JobId = "";
}
