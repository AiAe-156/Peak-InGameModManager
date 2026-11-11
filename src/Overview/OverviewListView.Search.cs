#nullable enable
using TMPro;
using UnityEngine;
namespace ModConfigEnhance.Overview;
internal sealed partial class OverviewListView
{
    private TMP_InputField? _overviewSearch;
    private string _searchInput = "";
    private float _searchAfter;
    private bool _searchRefreshPending;
    private float SearchWidth => Mathf.Clamp(Width * .38f, 240, 500);
    private void BuildOverviewSearch(float width)
    {
        _overviewSearch = OverviewUi.Input(_tools, _searchInput, placeholder: Loc.T(
            "Search name, package ID, author, version, description…", "搜索名称、包 ID、作者、版本、描述……", "搜尋名稱、套件 ID、作者、版本、描述……"));
        _overviewSearch.characterLimit = 256;
        _overviewSearch.textComponent.fontSize = 18;
        OverviewUi.Place((RectTransform)_overviewSearch.transform, 8, 0, SearchWidth, 32);
        _overviewSearch.onValueChanged.AddListener(value => {
            _searchInput = value; _searchAfter = Time.unscaledTime + .25f;
            _searchRefreshPending = true;
        });
        GamepadSupport.MarkSelectable(_overviewSearch.gameObject, null, null);
    }
    private void FlushOverviewSearch()
    {
        if (!_searchRefreshPending || !ListVisible || _building || _infrastructureLoading || OverviewUi.HasModal
            || Time.unscaledTime < _searchAfter || !string.IsNullOrEmpty(Input.compositionString)) return;
        _searchQuery = _searchInput.Trim(); _relatedMods = null;
        Render(); // Rejected rebuilds retain the pending request for a later tick.
    }
    private void ClearOverviewSearch()
    {
        _searchQuery = _searchInput = ""; _searchRefreshPending = false;
        if (_overviewSearch != null) _overviewSearch.SetTextWithoutNotify("");
    }
    private bool MatchesOverviewSearch(OverviewMod mod) => OverviewSearchText.Matches(_searchQuery,
        mod.Name, mod.RawName, mod.SettingDisplayName, OverviewLanguageBridge.GetDisplayName(mod), mod.DisplayName,
        mod.Guid, mod.Id, mod.PackageNamespace + "-" + mod.PackageName, mod.SourceKey, mod.PackageName,
        mod.Author, mod.SourceNamespace, mod.PackageNamespace, mod.PluginVersion, mod.PackageVersion,
        mod.DisplayVersion, mod.PreparedVersion, mod.LatestVersion, mod.Description, mod.RawDescription,
        mod.DisplayDescription, OverviewLanguageBridge.GetDescription(mod));
}
