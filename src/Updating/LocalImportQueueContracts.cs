#nullable enable
namespace ModConfigEnhance.Updating;

public sealed class LocalImportRequest
{
    public string OperationId { get; set; } = "";
    public string Operation { get; set; } = "preview";
    public string SourcePath { get; set; } = "";
    public string PackageFolder { get; set; } = "";
    public string PreviewId { get; set; } = "";
    public string PlanToken { get; set; } = "";
    public int SourceProcessId { get; set; }
}
