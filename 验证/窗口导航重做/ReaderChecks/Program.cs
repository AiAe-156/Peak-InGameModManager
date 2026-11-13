using System.Reflection;
using ModConfigEnhance;
using ModConfigEnhance.Overview;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

int count=0;
void Check(bool value,string name) { count++; if(!value) throw new Exception(name); }
void Frame() {
    Time.frameCount++; Gamepad.current!.buttonEast.wasPressedThisFrame=false;
    Gamepad.current.dpad.up.isPressed=Gamepad.current.dpad.down.isPressed=false;
    GamepadIntent.ScrollX=GamepadIntent.ScrollY=0;
}
void Tick(ControllerReadingSurface reader) => typeof(ControllerReadingSurface).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(reader,null);
AxisEventData Move(ControllerReadingSurface reader,MoveDirection direction) { var data=new AxisEventData{moveDir=direction}; reader.OnMove(data); return data; }
EventSystem.current=new(); Gamepad.current=new();
var scroll=new ScrollRect{content=new(){rect=new(){width=400,height=2000}},viewport=new(){rect=new(){width=400,height=400}}};
var reader=new ControllerReadingSurface{Scroll=scroll};
var highlight=new GamepadSupport.FocusHighlight(); reader.gameObject.Add(highlight);
EventSystem.current.currentSelectedGameObject=reader.gameObject; reader.OnEnable();
int calls=0,links=0; float amount=0;
reader.TryMovePage=n=>{calls++;amount=n;return true;}; reader.Links=()=>links++;
Check(!reader.IsBrowsing,"initial state must navigate");
Check(!Move(reader,MoveDirection.Down).used&&calls==0&&reader.NavigationMoves==1,"unlocked directions navigate without scrolling");
GamepadIntent.ScrollY=1;
Check(!reader.ScrollRightStick()&&calls==0,"unlocked right stick cannot browse");
reader.OnSubmit(new());
Check(reader.IsBrowsing&&highlight.Engaged&&links==0,"first A locks reading without opening links");
Check(BackGuard.Resets==1,"A clears an earlier double-back attempt");
Gamepad.current.dpad.down.isPressed=true;
Check(Move(reader,MoveDirection.Down).used&&amount==270,"locked dpad uses 3x step");
Gamepad.current.dpad.up.isPressed=false;
Move(reader,MoveDirection.Up);
Check(amount==-90,"left-stick move keeps original step");
int before=calls; Check(Move(reader,MoveDirection.Left).used&&calls==before,"locked horizontal direction stays in reader");
reader.TryMovePage=n=>false;
Check(Move(reader,MoveDirection.Down).used&&reader.IsBrowsing,"edge does not unlock or leave reader");
reader.OnSubmit(new()); Check(links==1&&reader.IsBrowsing,"second A can use page links without unlocking");

// Page-back handling first, then the component Update: both orders must consume only the unlock.
Frame(); Check(ControllerReadingSurface.ConsumeBack(),"back first unlocks"); Tick(reader);
Check(!reader.IsBrowsing&&!highlight.Engaged&&GamepadSupport.SwallowBackUntil==Time.frameCount+1,"back consumes current/next frame and leaves focus");
Check(EventSystem.current.currentSelectedGameObject==reader.gameObject,"unlock retains selected reader");
Check(!ControllerReadingSurface.ConsumeBack(),"later back request is handled by page guard");
Frame(); reader.OnSubmit(new()); Gamepad.current.buttonEast.wasPressedThisFrame=true; Tick(reader);
Check(!reader.IsBrowsing&&GamepadSupport.SwallowBackUntil==Time.frameCount+1,"component Update first also consumes B");
Frame(); reader.OnSubmit(new()); var cancel=new BaseEventData();reader.OnCancel(cancel);
Check(cancel.used&&!reader.IsBrowsing,"cancel event unlocks without leaving");

// Reader Update and the containing page may both try to scroll in one frame; move once only.
Frame(); reader.TryMovePage=n=>{calls++;amount=n;return true;};reader.OnSubmit(new());
GamepadIntent.ScrollY=1;before=calls;Tick(reader);reader.ScrollRightStick();
Check(calls==before+1&&Math.Abs(amount+15)<.001,"right stick uses original 900px/s once per frame");
Frame(); GamepadIntent.ScrollY=1;Check(reader.ScrollRightStick(),"next frame can scroll again");
reader.OnDeselect(new());Check(!reader.IsBrowsing&&!highlight.Engaged,"moving focus clears lock");
Frame();EventSystem.current.currentSelectedGameObject=reader.gameObject;reader.OnSubmit(new());GamepadSupport.IsGamepad=false;Tick(reader);
Check(!reader.IsBrowsing,"switching input device clears lock");GamepadSupport.IsGamepad=true;
reader.OnDisable();Check(ControllerReadingSurface.ForScroll(scroll)==null,"disabled reader unregisters");

// Default read-only dialog: vertical and horizontal bounds clamp normally while locked.
Frame();var dialog=new ControllerReadingSurface{Scroll=scroll};scroll.horizontal=true;scroll.content!.rect=new(){width=1000,height=2000};
EventSystem.current.currentSelectedGameObject=dialog.gameObject;dialog.OnEnable();dialog.OnSubmit(new());
Gamepad.current.dpad.down.isPressed=true;Move(dialog,MoveDirection.Down);
Check(Math.Abs(scroll.verticalNormalizedPosition-(1-270f/1600))<.0001,"default dialog vertical step");
Gamepad.current.dpad.right.isPressed=true;Move(dialog,MoveDirection.Right);
Check(Math.Abs(scroll.horizontalNormalizedPosition-.45f)<.0001,"default dialog horizontal step");
dialog.OnDisable();Check(!dialog.IsBrowsing,"disable releases browsing");
Console.WriteLine($"PASS {count} actual reader state and input checks (Unity boundary stubbed)");
