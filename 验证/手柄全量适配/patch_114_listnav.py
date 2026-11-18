# -*- coding: utf-8 -*-
"""1.1.4 (user): list -> toolbar with Up from the first row; focus back on the overview entry after leaving."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, '..', '..', 'src'))
def edit(name, pairs):
    p = os.path.join(SRC, name)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (name, a[:90], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('Overview/OverviewListView.VirtualRows.cs', [
('''                if(!target.IsInteractable() || !target.IsActive())continue;
                EventSystem.current?.SetSelectedGameObject(target.gameObject);return;
            }
        }
    }''', '''                if(!target.IsInteractable() || !target.IsActive())continue;
                EventSystem.current?.SetSelectedGameObject(target.gameObject);return;
            }
        }
        // 1.1.4 (user): Up from the first row went nowhere, so the controller could never reach the buttons above the
        // list. Leave to the nearest control in the column headers / list tools (they lead on up to the page toolbar).
        if(direction==MoveDirection.Up)
        {
            var above=_header.GetComponentsInChildren<Selectable>(false).Concat(_tools.GetComponentsInChildren<Selectable>(false))
                .Where(s=>s.IsActive() && s.IsInteractable() && s.navigation.mode!=Navigation.Mode.None)
                .OrderBy(s=>s.transform.IsChildOf(_header)?0:1).ThenBy(s=>Mathf.Abs(s.transform.position.x-source.transform.position.x)).FirstOrDefault();
            if(above!=null){_content.anchoredPosition=new Vector2(_content.anchoredPosition.x,0);RefreshVirtualRows();EventSystem.current?.SetSelectedGameObject(above.gameObject);}
        }
    }'''),
])
edit('Overview/EntryNavLink.cs', [
('''internal sealed class EntryNavLink : MonoBehaviour
{
    private int _linkAt = -1;''', '''internal sealed class EntryNavLink : MonoBehaviour
{
    /// <summary>1.1.4 (user): coming back from the overview, the focus goes back onto the entry button.</summary>
    internal static bool ReturnFocus;
    private int _linkAt = -1;'''),
('''        mine.selectOnRight = nearest; mine.selectOnDown = nearest; self.navigation = mine;''',
 '''        mine.selectOnRight = nearest; mine.selectOnDown = nearest; self.navigation = mine;
        if (ReturnFocus) { ReturnFocus = false; if (GamepadSupport.IsGamepad) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(gameObject); }'''),
])
edit('Overview/OverviewController.cs', [
('''        if (_parent == null) return;
        if(_opening)return;''', '''        if (_parent == null) return;
        if(_opening)return;
        EntryNavLink.ReturnFocus = true;'''),
])
print('ok')
