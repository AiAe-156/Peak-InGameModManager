#nullable enable
using System;
using System.IO;
using System.Linq;
using ModConfigEnhance.Updating;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;
/// <summary>One uninstall entrance, with the actual dependency list reviewed in place.</summary>
internal sealed class UninstallDialog : MonoBehaviour
{
    private ShopRequest _request = new();
    private TextMeshProUGUI _message = null!, _caption = null!;
    private Button _confirm = null!;
    private string _batchId = "";
    private bool _submitted;
    private float _poll;
    internal static void Open(string name, ShopRequest request, RectTransform? parent = null)
    {
        RectTransform? panel; Action close;
        if (parent == null) { panel = OverviewUi.CreateModalPanel("MCE_UninstallConfirm", 760, 420); close = OverviewUi.RequestCloseModal; }
        else {
            var shade = OverviewUi.Rect("UninstallOverlay", parent); OverviewUi.Fill(shade); shade.gameObject.AddComponent<Image>().color = ModConfigLook.Surface(new Color(0,0,0,.75f)); shade.SetAsLastSibling();
            panel = OverviewUi.Panel("Confirmation", shade, OverviewUi.Brown); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f,.5f); panel.sizeDelta = new Vector2(Math.Min(760,parent.rect.width-40),Math.Min(420,parent.rect.height-40)); close = () => Destroy(shade.gameObject);
        }
        if (panel == null) return;
        var dialog = panel.gameObject.AddComponent<UninstallDialog>(); dialog._request = request;
        var title = OverviewUi.Text(panel, Loc.LocalizeMessage("Uninstall mod"),24); OverviewUi.Place(title.rectTransform,20,12,panel.rect.width-40,34);
        dialog._message = OverviewUi.Text(panel,name+"\n\n"+Loc.LocalizeMessage("Confirm uninstall to queue it independently. Configuration and recovery backups are retained; dependent mods are listed in the queue job."),17);
        dialog._message.textWrappingMode = TextWrappingModes.Normal; OverviewUi.Place(dialog._message.rectTransform,20,58,panel.rect.width-40,panel.rect.height-124);
        dialog._confirm = OverviewUi.Button(panel,Loc.LocalizeMessage("Confirm uninstall"),dialog.Submit,210,34,UiActionRole.Danger);
        OverviewUi.Place((RectTransform)dialog._confirm.transform,20,panel.rect.height-50,210,34); dialog._caption=dialog._confirm.GetComponentInChildren<TextMeshProUGUI>();
        var cancel=OverviewUi.Button(panel,Loc.LocalizeMessage("Close"),close,108,34); OverviewUi.Place((RectTransform)cancel.transform,244,panel.rect.height-50,108,34);
        // 1.1.4 (gamepad review): start on the safe button, not on "Confirm uninstall".
        if (GamepadSupport.IsGamepad) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(cancel.gameObject);
    }
    private void Submit()
    {
        try {
            if (_submitted) return;
            _request.Operation="preview-uninstall"; _request.IgnoreDependencyWarnings=false;
            _batchId=Guid.NewGuid().ToString("N");OverviewUpdateQueue.SubmitUpdates(new[]{new UpdateCommand{Id=_batchId,Action="shop",Shop=_request}});
            _submitted=true; _confirm.interactable=false;
            _message.text=Loc.LocalizeMessage("Uninstall queued. Configuration and backups are retained; progress is in Downloads and operations.");
        } catch(Exception error) { _message.text=Loc.LocalizeMessage(error.Message); _confirm.interactable=true; }
    }
    private void Update()
    {
        if (!_submitted || Time.unscaledTime < _poll) return; _poll=Time.unscaledTime+.4f;
        try {
            // The only thing left to watch here is a rejected command; dependency notes live in the queue job.
            string rejected=Path.Combine(OverviewUpdateQueue.Root,"command-rejections",_batchId+".json");
            if(File.Exists(rejected))_message.text=Loc.LocalizeMessage(JObject.Parse(File.ReadAllText(rejected)).Value<string>("Error")??"");
        } catch(Exception error) when(error is IOException or Newtonsoft.Json.JsonException){_message.text=Loc.LocalizeMessage(error.Message);}
    }
}
