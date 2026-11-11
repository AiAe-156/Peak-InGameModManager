#nullable enable
using System;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewPage
{
    private Button _importTool = null!;
    private void BuildImportTool()
    {
        _importTool = OverviewUi.IconButton(_controls, "import", ImportTools, 34, 34);
        UiActionStyle.Set(_importTool, UiActionRole.Edit);
        AttachToolHint(_importTool.gameObject, () => Loc.T("Import mods or a modpack", "导入模组或整合包", "匯入模組或整合包"));
    }
    private void ImportTools()
    {
        _list?.HideAllHints(); ClearToolHint();
        var groups = new[] {
            new SourceToolGroup(Loc.T("Mods", "模组", "模組"), OverviewUi.Blue, new[] {
                new SourceToolOption { Entry = new UiMenuEntry {
                    Label = Loc.T("Import store mods", "导入商店模组", "匯入商店模組"),
                    Invoke = () => ManagementAction(() => ShopWindow.Open(_repo, _list?.Mods ?? Array.Empty<OverviewMod>())) } }
            }),
            new SourceToolGroup(Loc.T("Local files", "本地文件", "本機檔案"), OverviewUi.Green, new[] {
                new SourceToolOption { Entry = new UiMenuEntry {
                    Label = Loc.T("Import local DLL / ZIP / folder", "导入本地 DLL / ZIP / 文件夹", "匯入本機 DLL / ZIP / 資料夾"),
                    Invoke = () => ManagementAction(OpenLocalImport) } }
            }),
            new SourceToolGroup(Loc.T("Modpacks", "整合包", "整合包"), OverviewUi.Gold, new[] {
                new SourceToolOption { Entry = new UiMenuEntry {
                    Label = Loc.T("Import modpack", "导入整合包", "匯入整合包"),
                    Invoke = () => ManagementAction(() => {
                        _packStorageTab = false; _packPane = "import"; _packScrollY = 0;
                        OpenManagement(ManagementTab.Packs); _management?.Rebuild(ManagementTab.Packs);
                    }) } }
            })
        };
        _toolbarPopup = OverviewUi.SourceToolsDropdown(ToolAnchor(_importTool), groups, AttachToolHint);
    }
}
