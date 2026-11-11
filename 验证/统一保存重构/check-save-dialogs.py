from pathlib import Path
root=Path(__file__).resolve().parents[2]
base=root/'src/Overview'
ui=(base/'OverviewUi.cs').read_text(encoding='utf-8-sig')
shared=(base/'OverviewUi.SaveDialogs.cs').read_text(encoding='utf-8-sig')
review=(base/'OverviewUi.ProfileReview.cs').read_text(encoding='utf-8-sig')
page=(base/'OverviewPage.Profiles.cs').read_text(encoding='utf-8-sig')
local=(base/'ConfigurationSaveRequest.cs').read_text(encoding='utf-8-sig')
assert 'SaveDialogHeader(panel, title' in ui and 'SaveDialogBody(panel, body' in ui
assert 'ShowPresetSaveChoice(' in page and 'ShowPresetSaveChoice(' in local
assert 'MCE_FirstPresetSave' not in page and 'CreateModalPanel("MCE_PresetSaveChoice"' not in local
assert 'SaveDialogHeader(panel, title' in review and 'SaveDialogAction(panel' in review
assert 'text.richText = formatted' in shared and 'ContentSizeFitter.FitMode.PreferredSize' in shared
assert 'scroll.viewport = viewport; scroll.content = content' in shared
assert 'Mathf.Min(1, (panel.rect.width - 40) / 496)' in shared
assert 'CloseConfirmation(false); save();' in ui and 'CloseConfirmation(false); discard();' in ui
assert '_suspendedModal.interactable = false' in ui and 'CloseModal(); if (ModalParent' not in ui[ui.index('private static void ShowUnsavedConfirmation'):ui.index('private static void CloseConfirmation')]
# Fixed-footer action coordinates stay inside the panel at narrow and normal widths.
for width in [380,480,740,1080]:
 scale=min(1,(width-40)/496)
 for right,size in [(20,154),(184,154),(348,92),(20,190),(220,100)]:
  edge=20+max(0,right-20)*scale
  size*=scale
  assert width-edge-size>=19 and width-edge<=width-19
print('PASS 9 save-dialog source contracts and 20 narrow-window action bounds')
