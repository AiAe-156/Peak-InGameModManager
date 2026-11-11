#nullable enable
namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewMod
{
    public ModSourceKind SourceKind;
    public string HomepageUrl = "";
    public string AuthorSource = "";
    // Prepared package version is deliberately separate from the version loaded this session.
    public string PreparedVersion = "";
    public string IdentityConflict = "";
    public string HomeUrl => HomepageUrl.Length > 0 ? HomepageUrl : ThunderstoreUpdateService.ValidPart(SourceNamespace) && ThunderstoreUpdateService.ValidPart(SourceName)
        ? ThunderstoreUpdateService.HomeUrl(SourceNamespace, SourceName) : "";
}
