#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PEAKLib.ModConfig;
using PEAKLib.ModConfig.Components;
using PEAKLib.ModConfig.SettingOptions.SettingUI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Zorro.ControllerSupport;
using Zorro.Core;
using Zorro.UI;
using Modal = Zorro.UI.Modal.Modal;
using Object = UnityEngine.Object;

namespace ModConfigEnhance;

/// <summary>
/// 手柄支持（1.0.6 两级面板重做）。PEAK 的手柄 UI 是纯焦点导航：摇杆/十字键发 Move，
/// A 发 Submit 触发 Selectable 的 onClick，没有虚拟光标。ModConfig 的 ModSettings 页是
/// <see cref="PeakChildPage"/>，没实现 INavigationPage —— 手柄进页面后 EventSystem 没有任何选中，
/// 官方 PageNavigationHandler / NavigationContainerHandler 对它返回 null，整页是死的。
///
/// 按「左栏 1 级 / 右栏 2 级」两级锁定模型接管本页（鼠标路径完全不动）：
///   · <see cref="PanelFocus"/>：层级状态机 + 每帧兜底。锁左栏时焦点只能落在页面 - 右栏选项区；
///     锁右栏时只能在 menu.Content 子树内。进页默认锁左栏。A 键按用户模型往下钻：
///     模组行 A=选中展开分区；已选中模组行再 A=分区>1 时焦点移到第一分区行 / 否则直接进右栏；
///     分区行 A=选中分区并进右栏。B 键（GetParentPage 补丁拦截）：滑条调值态→退出调值、
///     输入框编辑中→退出编辑、锁右栏→回左栏（焦点回到来路行）、锁左栏→原返回。
///   · <see cref="NavChain"/>：把列表内的行 / 分区行 / 复选框 / 表头 / 右栏单元格钉成显式导航——
///     行内按 x 聚成「格」（堆叠的 默认/清空 同列），上下 = 同列纵贯（本行默认→本行清空→下行默认），
///     左右 = 同行相邻格（uGUI Automatic 会让右移越过行内复选框直奔滚动条/右栏）。
///   · <see cref="SliderGate"/> + Slider.OnMove / stepSize 补丁：选中滑条先按 A 才允许调值
///     （官方 SelectableSlider 同款语义），未激活时方向键纯导航；步进 cap 到区间 0.5%
///     （原版 stepSize=range*0.1，0-10000 的滑条一步 1000）；按住 1s 后 10/s、3s 后 40/s。
///   · 按键绑定框：A 进入捕获（提示「长按某个键 1 秒换绑」），同一键持续按住 ≥1s 才写入——
///     原版捕获一启动下一帧任何键（含手柄键）都直接写入，选中后随便按就改了。
///   · <see cref="FocusScrollIntoView"/>：选中项滚进视野（每帧按当前矩形追，MoveTowards 到位即停，
///     不 ForceRebuild —— 逐帧重建布局是下拉抖动的根源）。
///   · <see cref="FocusHighlight"/>：米白描边焦点框（自建控件原本没有任何焦点视觉）。
///   · <see cref="SidebarTabHotkeys"/>：LB/RB 切模组（仅锁左栏时），切完焦点落到新选中行 →
///     滚动自动跟上（原版 SelectRelative 不动焦点，所以之前切走了也看不见）。
/// </summary>
internal static class GamepadSupport
{
    // ── 公共入口 ────────────────────────────────────────────────────

    private static bool _inputHandlerBroken;
    private static bool _noDeviceLogged;
    private static bool _lastInputInited;
    private static bool _lastInputIsGamepad;
    private static bool _lastInputWarned;

    private const float StickSqDeadzone = 0.0225f; // 0.15² 摇杆死区
    private const float TriggerThreshold = 0.1f;

    /// <summary>
    /// 当前是不是手柄方案：看「最后一次真实输入」来自手柄还是键鼠，并要求系统里真有手柄设备。
    /// 游戏的 <c>PlayerInput</c> 不自动切方案（插着手柄碰鼠标，方案仍停在 Gamepad），
    /// 所以由 <see cref="InputHandlerUpdatePostfix"/> 每帧跟踪——鼠标一动立刻回到键鼠态，
    /// PanelFocus 不再跟鼠标抢焦点（「指针锁死」的根治）。跟踪器没挂上时退回游戏方案判定。
    /// </summary>
    internal static bool IsGamepad
    {
        get
        {
            if (_inputHandlerBroken)
            {
                return false;
            }

            try
            {
                bool gamepad = _lastInputInited
                    ? _lastInputIsGamepad
                    : InputHandler.GetCurrentUsedInputScheme() != InputScheme.KeyboardMouse;
                if (!gamepad)
                {
                    return false;
                }

                if (Gamepad.current == null)
                {
                    if (!_noDeviceLogged)
                    {
                        _noDeviceLogged = true;
                        Plugin.Log.LogWarning("[手柄] 最后输入被判为手柄但系统里没有手柄设备，按键鼠处理（若接了手柄请动一下摇杆）。");
                    }

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _inputHandlerBroken = true;
                Plugin.Log.LogWarning($"[手柄] Zorro InputHandler 不可用，手柄支持停用: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 挂在 InputHandler.Update 的 postfix：每帧看谁的输入在活动，记进 <see cref="_lastInputIsGamepad"/>。
    /// 只认明确的玩家操作件——陀螺仪 / 加速度计噪声不遍历 allControls 就不会进来。
    /// 同帧键鼠与手柄都有输入时键鼠优先（鼠标一动立刻退出手柄态，正是修复目标）。
    /// </summary>
    private static void InputHandlerUpdatePostfix()
    {
        try
        {
            if (!_lastInputInited)
            {
                _lastInputInited = true;
                _lastInputIsGamepad = InputHandler.GetCurrentUsedInputScheme() == InputScheme.Gamepad
                                      && Gamepad.current != null;
                return;
            }

            if (KeyboardMouseActive())
            {
                _lastInputIsGamepad = false;
            }
            else if (GamepadActive())
            {
                _lastInputIsGamepad = true;
            }
        }
        catch (Exception ex)
        {
            if (!_lastInputWarned)
            {
                _lastInputWarned = true;
                Plugin.Log.LogWarning($"[手柄] 最后输入设备跟踪失败: {ex.Message}");
            }
        }
    }

    private static bool KeyboardMouseActive()
    {
        Keyboard? keyboard = Keyboard.current;
        if (keyboard != null && keyboard.anyKey.isPressed)
        {
            return true;
        }

        Mouse? mouse = Mouse.current;
        if (mouse == null)
        {
            return false;
        }

        return mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed
            || mouse.delta.ReadValue().sqrMagnitude > 0.01f
            || mouse.scroll.ReadValue().y != 0f;
    }

    private static bool GamepadActive()
    {
        Gamepad? gamepad = Gamepad.current;
        if (gamepad == null)
        {
            return false;
        }

        if (gamepad.buttonSouth.isPressed || gamepad.buttonEast.isPressed
            || gamepad.buttonNorth.isPressed || gamepad.buttonWest.isPressed
            || gamepad.leftShoulder.isPressed || gamepad.rightShoulder.isPressed
            || gamepad.leftStickButton.isPressed || gamepad.rightStickButton.isPressed
            || gamepad.startButton.isPressed || gamepad.selectButton.isPressed
            || gamepad.dpad.up.isPressed || gamepad.dpad.down.isPressed
            || gamepad.dpad.left.isPressed || gamepad.dpad.right.isPressed)
        {
            return true;
        }

        return gamepad.leftStick.ReadValue().sqrMagnitude > StickSqDeadzone
            || gamepad.rightStick.ReadValue().sqrMagnitude > StickSqDeadzone
            || gamepad.leftTrigger.ReadValue() > TriggerThreshold
            || gamepad.rightTrigger.ReadValue() > TriggerThreshold;
    }

    private static InputAction? _navAction;
    private static bool _navResolved;

    /// <summary>UI/Navigate 动作（摇杆方向），给滑条按住连调计时用；解析不到时退回引擎自带 repeat。</summary>
    internal static InputAction? NavAction
    {
        get
        {
            if (_navResolved)
            {
                return _navAction;
            }

            _navResolved = true;
            try
            {
                _navAction = InputSystem.actions.FindAction("UI/Navigate") ?? InputSystem.actions.FindAction("Navigate");
                // 只认 Vector2 方向型；按钮型导航读不出向量，交给引擎自带 repeat
                if (_navAction != null && _navAction.expectedControlType != "Vector2")
                {
                    _navAction = null;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[手柄] Navigate 动作解析失败: {ex.Message}");
            }

            return _navAction;
        }
    }

    /// <summary>树形布局建完后挂上本页的手柄支持。</summary>
    internal static void Attach(ModSettingsMenu menu, PeakHorizontalTabs modTabs)
    {
        try
        {
            PeakChildPage page = menu.MainPage;
            ScrollRect listScroll = modTabs.GetComponent<ScrollRect>();

            PanelFocus focus = page.gameObject.AddComponent<PanelFocus>();
            focus.Menu = menu;
            focus.Page = page;

            SidebarTabHotkeys hotkeys = listScroll.gameObject.AddComponent<SidebarTabHotkeys>();
            hotkeys.Menu = menu;
            hotkeys.Page = page;

            NavChain chain = listScroll.gameObject.AddComponent<NavChain>();
            chain.Page = page;
            chain.Content = listScroll.content;
            chain.Sidebar = true;

            NavChain optionsChain = menu.Content.gameObject.AddComponent<NavChain>();
            optionsChain.Page = page;
            optionsChain.Content = (RectTransform)menu.Content;
            optionsChain.Sidebar = false;

            // 页面固定件只有焦点框（不在任何滚动区里）：返回键、筛选下拉、搜索框
            if (page.BackButton != null)
            {
                MarkSelectable(page.BackButton.gameObject, null, null);
            }

            if (menu.FilterDropdown != null && menu.FilterDropdown.Dropdown != null)
            {
                MarkSelectable(menu.FilterDropdown.Dropdown.gameObject, null, null);
            }

            Transform? search = page.transform.Find("SearchInput");
            TMP_InputField? input = search != null ? search.GetComponentInChildren<TMP_InputField>(true) : null;
            if (input != null)
            {
                MarkSelectable(input.gameObject, null, null);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[手柄] 挂载失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 给一个交互件补手柄表现：焦点框（highlight）+ 选中滚入视野（scroll 非空时）。
    /// <paramref name="scrollTarget"/> 是滚动时对准的矩形——行内复选框对准整行而不是小方块。
    /// </summary>
    internal static void MarkSelectable(GameObject go, ScrollRect? scroll, RectTransform? scrollTarget, bool highlight = true)
    {
        if (highlight && go.GetComponent<FocusHighlight>() == null)
        {
            go.AddComponent<FocusHighlight>();
        }

        // 下拉：展开项导航交给 DropdownNavFix 重链成纯上下
        if (go.GetComponent<TMP_Dropdown>() != null && go.GetComponent<DropdownNavFix>() == null)
        {
            go.AddComponent<DropdownNavFix>();
        }

        // 输入框：选中不直接进编辑（A=TMP 自己的 OnSubmit 激活；鼠标点击走 OnPointerClick 不受影响）
        TMP_InputField? input = go.GetComponent<TMP_InputField>();
        if (input != null)
        {
            input.shouldActivateOnSelect = false;
            if (go.GetComponent<InputFieldGate>() == null)
            {
                go.AddComponent<InputFieldGate>();
            }
        }

        // 按键绑定框：捕获中时焦点框亮橙（武装态提示）
        if (go.GetComponent<InputBindingSettingUI>() != null && go.GetComponent<CaptureGlow>() == null)
        {
            go.AddComponent<CaptureGlow>();
        }

        if (scroll == null)
        {
            return;
        }

        FocusScrollIntoView follow = go.GetComponent<FocusScrollIntoView>() ?? go.AddComponent<FocusScrollIntoView>();
        follow.Scroll = scroll;
        follow.Target = scrollTarget != null ? scrollTarget : (RectTransform)go.transform;
    }

    /// <summary>右侧选项单元格：全员滚动跟随 + 焦点框；滑条再加 A 键调值门控。</summary>
    internal static void MarkCell(SettingsUICell cell, ScrollRect? scroll)
    {
        if (cell == null || cell.m_settingsContentParent == null)
        {
            return;
        }

        foreach (Selectable selectable in cell.m_settingsContentParent.GetComponentsInChildren<Selectable>(true))
        {
            // 下拉展开项的 Toggle、滚动条不归我们滚
            if (selectable is Toggle or Scrollbar)
            {
                continue;
            }

            MarkSelectable(selectable.gameObject, scroll, (RectTransform)cell.transform);
            if (selectable is Slider)
            {
                selectable.gameObject.AddComponent<SliderGate>();
            }
        }
    }

    /// <summary>行已接过 submit 监听的标记（UpdateSectionTabs 重建/重跑会反复经过，防重复 AddListener）。</summary>
    private sealed class RowSubmitWired : MonoBehaviour
    {
    }

    /// <summary>模组行：追加「已选中再按 A → 往分区/右栏钻」语义。挂在 Decorate 里。</summary>
    internal static void WireModRow(GameObject row)
    {
        Button? button = row.GetComponent<Button>();
        if (button == null || row.GetComponent<RowSubmitWired>() != null)
        {
            return;
        }

        row.AddComponent<RowSubmitWired>();
        button.onClick.AddListener(() => OnModRowSubmitted(row));
    }

    /// <summary>分区行：追加「A → 进右栏」语义。挂在 UpdateSectionTabsPostfix 里。</summary>
    internal static void WireSectionRow(GameObject row)
    {
        Button? button = row.GetComponent<Button>();
        if (button == null || row.GetComponent<RowSubmitWired>() != null)
        {
            return;
        }

        row.AddComponent<RowSubmitWired>();
        button.onClick.AddListener(() => OnSectionRowSubmitted(row));
    }

    /// <summary>模组行 onClick 追加（Decorate 时挂）：原 handler 先跑完 TABS.Select，这里只看手柄语义。</summary>
    internal static void OnModRowSubmitted(GameObject row)
    {
        if (!IsGamepad)
        {
            return;
        }

        PanelFocus? focus = row.GetComponentInParent<PanelFocus>();
        ModdedTABSButton? button = row.GetComponent<ModdedTABSButton>();
        if (focus == null || button == null || focus.Menu == null)
        {
            return;
        }

        if (focus.LastSubmittedModRow == button)
        {
            // 已选中的行再按 A：分区展开了 → 焦点落到第一分区行；没展开 → 直接进右栏
            focus.LastSubmittedModRow = null;
            GameObject? firstSection = FirstActiveSectionRow(focus.Menu);
            if (firstSection != null)
            {
                EventSystem.current?.SetSelectedGameObject(firstSection);
            }
            else
            {
                focus.EnterOptions();
            }
        }
        else
        {
            focus.LastSubmittedModRow = button;
        }
    }

    /// <summary>分区行 onClick 追加（UpdateSectionTabsPostfix 时挂）：选中分区后直接进右栏。</summary>
    internal static void OnSectionRowSubmitted(GameObject row)
    {
        if (IsGamepad)
        {
            row.GetComponentInParent<PanelFocus>()?.EnterOptions();
        }
    }

    private static GameObject? FirstActiveSectionRow(ModSettingsMenu menu)
    {
        PeakHorizontalTabs? tabs = menu.SectionTabController;
        if (tabs == null || !tabs.gameObject.activeInHierarchy)
        {
            return null;
        }

        foreach (GameObject row in tabs.Tabs)
        {
            if (row != null && row.activeInHierarchy && row.GetComponent<ModdedTABSButton>() != null)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>下拉在展开时焦点会跑到页面外的弹层上，此刻兜底与肩键都该收手。</summary>
    private static bool AnyDropdownOpen(Transform page)
    {
        foreach (TMP_Dropdown dropdown in page.GetComponentsInChildren<TMP_Dropdown>(false))
        {
            if (dropdown.IsExpanded)
            {
                return true;
            }
        }

        return false;
    }

    private static bool _captureBroken;

    /// <summary>拿捕获服务实例（失败一次后熔断，不重复报错）。</summary>
    private static InputBindingCaptureService? CaptureService()
    {
        if (_captureBroken)
        {
            return null;
        }

        try
        {
            return ModConfigPlugin.instance != null ? ModConfigPlugin.instance.InputBindingCapture : null;
        }
        catch (Exception ex)
        {
            _captureBroken = true;
            Plugin.Log.LogWarning($"[手柄] 按键捕获状态读取失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>ModConfig 正在捕按键时不要抢焦点 / 切模组。</summary>
    private static bool CaptureActive()
    {
        return CaptureService()?.IsCapturing == true;
    }

    /// <summary>页面上方有 Modal / MenuWindow 打开时，选择归它们的容器管。</summary>
    private static bool Overlaid()
    {
        return Modal.IsOpen || MenuWindow.AllActiveWindows.Count > 0 || Overview.OverviewUi.HasModal;
    }

    // ── Harmony 补丁 ────────────────────────────────────────────────

    /// <summary>挂在 TryApply 里：滑条误触门控 + 步进上限 + B 键分级返回。单个失败只警告。</summary>
    internal static void ApplyPatches(Harmony harmony)
    {
        try
        {
            MethodInfo? onMove = AccessTools.Method(typeof(Slider), "OnMove", new[] { typeof(AxisEventData) });
            if (onMove != null)
            {
                harmony.Patch(onMove, prefix: new HarmonyMethod(typeof(GamepadSupport), nameof(SliderOnMovePrefix)));
            }
            else
            {
                Plugin.Log.LogWarning("[手柄] 找不到 Slider.OnMove，滑条误触门控不可用。");
            }

            MethodInfo? stepGetter = AccessTools.PropertyGetter(typeof(Slider), "stepSize");
            if (stepGetter != null)
            {
                harmony.Patch(stepGetter, postfix: new HarmonyMethod(typeof(GamepadSupport), nameof(SliderStepSizePostfix)));
            }
            else
            {
                Plugin.Log.LogWarning("[手柄] 找不到 Slider.stepSize，步进上限不可用。");
            }

            MethodInfo? getParent = AccessTools.Method(typeof(PeakChildPage), "GetParentPage", Type.EmptyTypes);
            if (getParent != null)
            {
                harmony.Patch(getParent, prefix: new HarmonyMethod(typeof(GamepadSupport), nameof(GetParentPagePrefix)));
            }
            else
            {
                Plugin.Log.LogWarning("[手柄] 找不到 PeakChildPage.GetParentPage，B 键分级返回不可用。");
            }

            // 按键绑定捕获：原版「捕获一启动，任何键 GetKeyDown 一下就写入」（含手柄键）——
            // 改成同一键持续按住 ≥1s 才提交。对键鼠同样生效（点一下误改的问题同因）。
            MethodInfo? captureUpdate = AccessTools.Method(typeof(InputBindingCaptureService), "Update");
            if (captureUpdate != null)
            {
                harmony.Patch(captureUpdate, prefix: new HarmonyMethod(typeof(GamepadSupport), nameof(BindingCaptureUpdatePrefix)));
            }
            else
            {
                Plugin.Log.LogWarning("[手柄] 找不到 InputBindingCaptureService.Update，绑定长按确认不可用。");
            }

            MethodInfo? capturePrompt = AccessTools.Method(typeof(InputBindingSettingUI), "ShowCapturePrompt");
            if (capturePrompt != null)
            {
                harmony.Patch(capturePrompt, postfix: new HarmonyMethod(typeof(GamepadSupport), nameof(BindingCapturePromptPostfix)));
            }

            // 最后输入设备跟踪：PlayerInput 不自动切方案，动鼠标时方案仍停在 Gamepad（指针锁死根源），
            // 每帧在 InputHandler.Update 后统计真实输入，IsGamepad 改查它。挂不上时 IsGamepad 退回原方案判定。
            MethodInfo? inputUpdate = AccessTools.Method(typeof(InputHandler), "Update", Type.EmptyTypes);
            if (inputUpdate != null)
            {
                harmony.Patch(inputUpdate, postfix: new HarmonyMethod(typeof(GamepadSupport), nameof(InputHandlerUpdatePostfix)));
            }
            else
            {
                Plugin.Log.LogWarning("[手柄] 找不到 InputHandler.Update，最后输入设备跟踪未挂载。");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[手柄] 补丁挂载失败: {ex.Message}");
        }
    }

    private const float BindHoldSeconds = 1f;
    private static KeyCode _bindHeldKey = KeyCode.None;
    private static float _bindHeldTime;

    /// <summary>
    /// 接管捕获 Update（整段替换）：Escape/Pause 取消照旧；KeyCode 分支从「GetKeyDown 立即提交」
    /// 改成「同一键持续按住 ≥1s 才提交」——原实现里捕获一启动，下一帧任何键（含手柄 JoystickButton）
    /// 都直接写入，手柄选中后按个键就被改了。Path 捕获走自己的 RebindingOperation，
    /// 原方法此处同样只检查取消，行为一致。
    /// </summary>
    private static bool BindingCaptureUpdatePrefix(InputBindingCaptureService __instance)
    {
        // 键鼠保持原版「按下即写」；长按确认只解决手柄选中后按键误写的问题
        if (!IsGamepad)
        {
            return true;
        }

        if (__instance.captureKind == InputBindingCaptureService.CaptureKind.None)
        {
            _bindHeldKey = KeyCode.None;
            return false;
        }

        if (Input.GetKeyDown(KeyCode.Escape) || (__instance.pauseAction != null && __instance.pauseAction.WasPressedThisFrame()))
        {
            _bindHeldKey = KeyCode.None;
            _bindHeldTime = 0f;
            __instance.CancelCurrent();
            return false;
        }

        if (__instance.captureKind != InputBindingCaptureService.CaptureKind.KeyCode)
        {
            _bindHeldKey = KeyCode.None;
            _bindHeldTime = 0f;
            return false;
        }

        KeyCode pressed = KeyCode.None;
        KeyCode[] keyCodes = InputBindingCaptureService.KeyCodes;
        for (int i = 0; i < keyCodes.Length; i++)
        {
            if (Input.GetKey(keyCodes[i]))
            {
                pressed = keyCodes[i];
                break;
            }
        }

        if (pressed == KeyCode.None)
        {
            _bindHeldKey = KeyCode.None;
            _bindHeldTime = 0f;
            return false;
        }

        if (pressed != _bindHeldKey)
        {
            _bindHeldKey = pressed;
            _bindHeldTime = 0f;
        }

        _bindHeldTime += Time.unscaledDeltaTime;
        if (_bindHeldTime >= BindHoldSeconds)
        {
            _bindHeldKey = KeyCode.None;
            _bindHeldTime = 0f;
            __instance.CompleteKeyCode(pressed);
        }

        return false;
    }

    /// <summary>捕获提示语：把「SELECT A KEY」换成长按说明（跟随界面语言）。仅手柄写；
    /// 键鼠提示由本地化补丁的 postfix 负责（它后注册后执行，也不能让它盖住本提示）。</summary>
    private static void BindingCapturePromptPostfix(InputBindingSettingUI __instance)
    {
        if (!IsGamepad || __instance.KeyText == null)
        {
            return;
        }

        __instance.KeyText.text = Loc.T("Hold a key for 1s to rebind", "长按某个键 1 秒换绑", "長按某個鍵 1 秒換綁");
    }

    /// <summary>未按 A 激活的滑条：方向输入只导航、不改值（左右本来是调值，正是误触源头）。</summary>
    private static bool SliderOnMovePrefix(Slider __instance, AxisEventData eventData)
    {
        if (!IsGamepad)
        {
            return true;
        }

        SliderGate? gate = __instance.GetComponent<SliderGate>();
        if (gate == null)
        {
            return true;
        }

        if (gate.Engaged)
        {
            // 已激活：左右由 SliderGate.Update 的计时器驱动（按下沿 + 1.5s 后 10/s），
            // Navigate 动作在时吃掉事件防引擎双步进；动作缺失则退回引擎步进（stepSize 已被 cap）。
            bool horizontal = eventData.moveDir is MoveDirection.Left or MoveDirection.Right;
            return !(horizontal && NavAction != null);
        }

        // 未激活：纯导航（绕过 Slider.FindSelectableOnXxx 的同轴 null 屏蔽，直接按方向找）
        Vector3 dir = eventData.moveDir switch
        {
            MoveDirection.Left => Vector3.left,
            MoveDirection.Up => Vector3.up,
            MoveDirection.Down => Vector3.down,
            _ => Vector3.right,
        };
        Selectable? next = __instance.FindSelectable(dir);
        if (next != null)
        {
            EventSystem.current?.SetSelectedGameObject(next.gameObject);
        }

        return false;
    }

    /// <summary>步进上限：最多区间的 0.5%（原版 0.1，0-10000 的滑条一步 1000）。整数档最少 1 步。</summary>
    private static void SliderStepSizePostfix(Slider __instance, ref float __result)
    {
        if (__instance.GetComponent<SliderGate>() == null)
        {
            return;
        }

        float range = __instance.maxValue - __instance.minValue;
        if (range <= 0f)
        {
            return;
        }

        __result = __instance.wholeNumbers
            ? Mathf.Max(1f, Mathf.Round(range * 0.005f))
            : Mathf.Min(__result, range * 0.005f);
    }

    /// <summary>
    /// B 键分级返回：PeakChildPage.GetParentPage 是所有返回路径（B / Start / Esc / 返回按钮）
    /// 的统一出口。手柄方案下本页吃掉它的条件由 <see cref="PanelFocus.ConsumeBack"/> 判：
    /// 滑条调值中 → 退出调值；输入框编辑中 → 退出编辑；锁右栏 → 回左栏。
    /// 吃掉后返回 (自身, 空过渡) —— TransistionToPage 对同页提前 return（日志留一行
    /// "Trying to transition to current page"，无害）。鼠标路径完全不受影响（IsGamepad 挡着）。
    /// </summary>
    private static bool GetParentPagePrefix(PeakChildPage __instance, ref (UIPage, PageTransistion) __result)
    {
        if (!IsGamepad)
        {
            return true;
        }

        PanelFocus? focus = __instance.GetComponent<PanelFocus>();
        if (focus == null || !focus.ConsumeBack())
        {
            return true;
        }

        __result = (__instance, NoopTransistion.Shared);
        return false;
    }

    /// <summary>同页空过渡：GetParentPage 被吃掉时返回给调用方的占位物。</summary>
    internal sealed class NoopTransistion : PageTransistion
    {
        internal static readonly PageTransistion Shared = new NoopTransistion();

        public override void Transistion(PageBase oldSubPage, PageBase newSubPage)
        {
        }
    }

    // ── 两级焦点锁 ──────────────────────────────────────────────────

    /// <summary>
    /// 挂在 ModSettings 页面上：层级状态机（Sidebar 左栏 / Options 右栏）+ 每帧兜底。
    /// 锁某层时 EventSystem 选中必须落在该层区域内：失效或越界就拉回该层记忆/默认项。
    /// 选中合法时记住它（_lastSidebar / _lastOptions），进右栏、B 回左栏都靠这两个记忆点。
    /// </summary>
    internal sealed class PanelFocus : MonoBehaviour
    {
        internal ModSettingsMenu? Menu;
        internal PeakChildPage? Page;

        /// <summary>true = 锁右栏（选项区）；false = 锁左栏。</summary>
        internal bool InOptions { get; private set; }

        /// <summary>上一次按过 A 的模组行：同一行再按 A 视为「往下钻」。</summary>
        internal ModdedTABSButton? LastSubmittedModRow;

        private UIPageHandler? _handler;
        private GameObject? _lastSidebar;
        private GameObject? _lastOptions;

        private void Awake()
        {
            _handler = GetComponentInParent<UIPageHandler>(true);
        }

        private void OnDisable()
        {
            InOptions = false;
            _lastSidebar = null;
            _lastOptions = null;
            LastSubmittedModRow = null;
        }

        private void Update()
        {
            if (!IsGamepad || Menu == null || Page == null)
            {
                return;
            }

            if (_handler == null || _handler.currentPage != Page || !Page.gameObject.activeInHierarchy)
            {
                return;
            }

            if (Overlaid() || CaptureActive() || AnyDropdownOpen(Page.transform))
            {
                return;
            }

            EventSystem? system = EventSystem.current;
            if (system == null || system.alreadySelecting)
            {
                return;
            }

            GameObject? selected = system.currentSelectedGameObject;
            bool valid = IsValid(selected);
            bool inOptions = IsInOptions(selected);

            if (InOptions)
            {
                if (valid && inOptions)
                {
                    _lastOptions = selected;
                }
                else
                {
                    SelectOptions(system);
                }
            }
            else
            {
                if (valid && !inOptions)
                {
                    _lastSidebar = selected;
                }
                else
                {
                    SelectSidebar(system);
                }
            }
        }

        /// <summary>A 键下钻：锁右栏并把焦点放到 _lastOptions（仍有效）或第一个选项控件。右栏空则不动。</summary>
        internal void EnterOptions()
        {
            if (!IsGamepad || Menu == null)
            {
                return;
            }

            GameObject? target = StillValid(_lastOptions, true) ? _lastOptions : FirstCellSelectable();
            if (target == null)
            {
                return; // 右栏没东西可进，留在左栏
            }

            InOptions = true;
            EventSystem.current?.SetSelectedGameObject(target);
        }

        /// <summary>B 键消费判定：true = 已吃掉（这次返回不该翻页）。</summary>
        internal bool ConsumeBack()
        {
            if (!IsGamepad || Overlaid())
            {
                return false;
            }

            // 捕获中：B 只取消捕获不退页（上游的 pauseAction 不认手柄 B，取消权在我们手里）
            InputBindingCaptureService? capture = CaptureService();
            if (capture != null && capture.IsCapturing)
            {
                Object? captureOwner = capture.owner;
                if (captureOwner != null)
                {
                    capture.Cancel(captureOwner);
                }

                return true;
            }

            // 下拉展开中：这次返回只关下拉（项上的 DropdownItem.OnCancel 会 Hide），不弹层
            if (Page != null && AnyDropdownOpen(Page.transform))
            {
                return true;
            }

            GameObject? selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

            // 输入框编辑中 → 只退出编辑（与官方 JoinRoom 对 roomInput 的处理同款）
            TMP_InputField? input = selected != null ? selected.GetComponent<TMP_InputField>() : null;
            if (input != null && input.isFocused)
            {
                input.DeactivateInputField();
                return true;
            }

            // 滑条调值中 → 只退出调值
            SliderGate? gate = selected != null ? selected.GetComponent<SliderGate>() : null;
            if (gate != null && gate.Engaged)
            {
                gate.Disengage();
                return true;
            }

            if (!InOptions)
            {
                return false; // 锁左栏：原返回（翻回上一级页面）
            }

            InOptions = false;
            EventSystem? system = EventSystem.current;
            if (system != null)
            {
                SelectSidebar(system);
            }

            return true;
        }

        // ── 内部 ────────────────────────────────────────────────────

        private bool IsValid(GameObject? selected)
        {
            if (selected == null || !selected.activeInHierarchy || Page == null)
            {
                return false;
            }

            if (!selected.transform.IsGrandChildOf(Page.transform))
            {
                return false;
            }

            Selectable? selectable = selected.GetComponent<Selectable>();
            return selectable != null && selectable.IsInteractable();
        }

        /// <summary>只校验存活 + 层级归属，给记忆点（_lastXxx）复用。</summary>
        private bool StillValid(GameObject? selected, bool wantOptions)
        {
            if (selected == null || !selected.activeInHierarchy || IsInOptions(selected) != wantOptions)
            {
                return false;
            }

            Selectable? selectable = selected.GetComponent<Selectable>();
            return selectable != null && selectable.IsInteractable();
        }

        private bool IsInOptions(GameObject? go)
        {
            return go != null && Menu != null && Menu.Content != null
                && go.transform.IsGrandChildOf(Menu.Content);
        }

        /// <summary>右栏默认/回退项：上一个仍有效的右栏选中 → 第一个选项单元格的可交互件 → 空则退回左栏。</summary>
        private void SelectOptions(EventSystem system)
        {
            GameObject? target = StillValid(_lastOptions, true) ? _lastOptions : FirstCellSelectable();
            if (target == null)
            {
                InOptions = false;
                SelectSidebar(system);
                return;
            }

            system.SetSelectedGameObject(target);
        }

        /// <summary>左栏默认/回退项：上一个仍有效的左栏选中 → 当前选中模组行 → 第一个可见模组行 → 返回键。</summary>
        private void SelectSidebar(EventSystem system)
        {
            GameObject? target = StillValid(_lastSidebar, false) ? _lastSidebar : null;
            if (target == null)
            {
                ModdedTABSButton? selected = Menu?.ModTabs != null ? Menu.ModTabs.selectedButton : null;
                if (selected != null && selected.gameObject.activeInHierarchy)
                {
                    target = selected.gameObject;
                }
            }

            target ??= FirstVisibleModRow();
            if (target == null && Page != null && Page.BackButton != null)
            {
                target = Page.BackButton.gameObject;
            }

            if (target != null)
            {
                system.SetSelectedGameObject(target);
            }
        }

        private GameObject? FirstVisibleModRow()
        {
            ScrollRect? scroll = Menu?.ModTabController != null ? Menu.ModTabController.GetComponent<ScrollRect>() : null;
            if (scroll == null || scroll.content == null)
            {
                return null;
            }

            foreach (Transform child in scroll.content)
            {
                if (child.gameObject.activeInHierarchy && child.GetComponent<ModdedTABSButton>() != null)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private GameObject? FirstCellSelectable()
        {
            if (Menu == null)
            {
                return null;
            }

            foreach (SettingsUICell cell in Menu.m_spawnedCells)
            {
                if (cell == null || !cell.gameObject.activeInHierarchy)
                {
                    continue;
                }

                GameObject? selectable = cell.GetSelectable();
                if (selectable == null || !selectable.activeInHierarchy)
                {
                    continue;
                }

                Selectable? component = selectable.GetComponent<Selectable>();
                if (component != null && component.IsInteractable())
                {
                    return selectable;
                }
            }

            return null;
        }
    }

    // ── 显式导航链 ──────────────────────────────────────────────────

    /// <summary>
    /// 把一个 ScrollRect content 的直属子项（每行）钉成显式导航链：
    /// 行内可选件按世界 x 排成水平组（行按钮 → 复选框），行间上下按行序。
    /// uGUI Automatic 会让「行上按右」越过行内复选框直奔滚动条或右栏控件，实测选不到 —— 必须显式。
    /// 每帧重算（行可见性 / 分区重建 / 单元格重建都会让链过期，Explicit 目标失活会被 uGUI 直接跳过）。
    /// Sidebar 模式额外处理：分区容器（TreeMarker）逐行展开进链；顶端行的 up 桥到搜索框。
    /// </summary>
    internal sealed class NavChain : MonoBehaviour
    {
        internal PeakChildPage? Page;
        internal RectTransform? Content;
        internal bool Sidebar;

        /// <summary>同列容差：两个可选项中心 x 差 ≤ 该值视为同一纵向列（堆叠的「默认/清空」正好同 x）。</summary>
        private const float ColumnTolerance = 14f;

        private UIPageHandler? _handler;

        /// <summary>一行（模组行 / 分区行 / 表头 / 选项单元格）；行内可选项按 x 分成若干「格」。</summary>
        private sealed class Line
        {
            internal readonly List<Selectable> Items = new List<Selectable>();
            internal readonly List<Group> Groups = new List<Group>();
        }

        /// <summary>行内一格 = 同 x 堆叠的纵向小组（默认在上、清空在下）；行内格按 meanX 排序。</summary>
        private sealed class Group
        {
            internal readonly List<Selectable> Items = new List<Selectable>();   // y 倒序（上→下）
            internal float MeanX;
            internal int Col;                                                   // 全局列号（跨行对齐用）
        }

        private void Awake()
        {
            _handler = GetComponentInParent<UIPageHandler>(true);
        }

        private void Update()
        {
            if (!IsGamepad || Page == null || Content == null)
            {
                return;
            }

            if (_handler == null || _handler.currentPage != Page)
            {
                return;
            }

            Relink();
        }

        private void Relink()
        {
            // 1. 收行（可见子节点；分区容器逐行展开进链）
            List<Line> lines = new List<Line>();
            foreach (Transform child in Content!)
            {
                if (!child.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (Sidebar && child.GetComponent<ModConfigTreeLayoutPatch.TreeMarker>() != null)
                {
                    // 分区容器嵌在模组列表里：逐行展开进链（父级不可点，它的 content 里的行才可点）
                    ScrollRect? inner = child.GetComponent<ScrollRect>();
                    RectTransform? innerContent = inner != null ? inner.content : null;
                    if (innerContent == null)
                    {
                        continue;
                    }

                    foreach (Transform sectionRow in innerContent)
                    {
                        if (!sectionRow.gameObject.activeInHierarchy)
                        {
                            continue;
                        }

                        Selectable? selectable = sectionRow.GetComponent<Selectable>();
                        if (selectable != null && selectable.IsInteractable())
                        {
                            Line line = new Line();
                            line.Items.Add(selectable);
                            lines.Add(line);
                        }
                    }

                    continue;
                }

                List<Selectable>? items = CollectLineItems(child);
                if (items != null)
                {
                    Line line = new Line();
                    line.Items.AddRange(items);
                    lines.Add(line);
                }
            }

            if (lines.Count == 0)
            {
                return;
            }

            // 2. 行内分格：按中心 x 排序，连续 Δx ≤ 容差归同格；格内按 y 倒序（上→下）
            foreach (Line line in lines)
            {
                line.Items.Sort(CompareX);
                Group? group = null;
                foreach (Selectable s in line.Items)
                {
                    float x = s.transform.position.x;
                    if (group == null || Mathf.Abs(x - group.MeanX) > ColumnTolerance)
                    {
                        group = new Group();
                        line.Groups.Add(group);
                    }

                    group.Items.Add(s);
                    group.MeanX = (group.MeanX * (group.Items.Count - 1) + x) / group.Items.Count;
                }

                foreach (Group g in line.Groups)
                {
                    g.Items.Sort(CompareYDesc);
                }
            }

            // 3. 跨行对齐：逐行把格并进最近的全局列（首见均值作列锚点）
            List<float> columnAnchors = new List<float>();
            foreach (Line line in lines)
            {
                foreach (Group g in line.Groups)
                {
                    int col = -1;
                    float best = float.MaxValue;
                    for (int c = 0; c < columnAnchors.Count; c++)
                    {
                        float d = Mathf.Abs(columnAnchors[c] - g.MeanX);
                        if (d <= ColumnTolerance && d < best)
                        {
                            best = d;
                            col = c;
                        }
                    }

                    if (col < 0)
                    {
                        col = columnAnchors.Count;
                        columnAnchors.Add(g.MeanX);
                    }

                    g.Col = col;
                }
            }

            // 4. 赋链：格内上下 = 同列堆叠项（默认↔清空）；出格到相邻行的同列/最近列；左右 = 同行相邻格
            Selectable? searchField = Sidebar ? FindSearchField() : null;
            for (int i = 0; i < lines.Count; i++)
            {
                Line line = lines[i];
                for (int gi = 0; gi < line.Groups.Count; gi++)
                {
                    Group g = line.Groups[gi];
                    for (int si = 0; si < g.Items.Count; si++)
                    {
                        Selectable s = g.Items[si];
                        float y = s.transform.position.y;
                        Navigation nav = s.navigation;
                        nav.mode = Navigation.Mode.Explicit;
                        nav.selectOnUp = si > 0 ? g.Items[si - 1]
                            : i > 0 ? NearestInLine(lines[i - 1], g.Col, y) : searchField;
                        nav.selectOnDown = si < g.Items.Count - 1 ? g.Items[si + 1]
                            : i < lines.Count - 1 ? NearestInLine(lines[i + 1], g.Col, y) : null;
                        nav.selectOnLeft = gi > 0 ? NearestInGroup(line.Groups[gi - 1], y) : null;
                        nav.selectOnRight = gi < line.Groups.Count - 1 ? NearestInGroup(line.Groups[gi + 1], y) : null;
                        s.navigation = nav;
                    }
                }
            }
        }

        /// <summary>下一可见行里的目标：同列优先、最近列兜底，列内取 y 最近项。</summary>
        private static Selectable? NearestInLine(Line line, int col, float y)
        {
            Group? best = null;
            int bestDist = int.MaxValue;
            foreach (Group g in line.Groups)
            {
                int d = Mathf.Abs(g.Col - col);
                if (d < bestDist || (d == bestDist && g.Col < (best != null ? best.Col : int.MaxValue)))
                {
                    best = g;
                    bestDist = d;
                }
            }

            return best != null ? NearestInGroup(best, y) : null;
        }

        /// <summary>格内取与 y 最近的项（左右落到列里高度最接近的那个）。</summary>
        private static Selectable NearestInGroup(Group group, float y)
        {
            Selectable best = group.Items[0];
            float bestDy = Mathf.Abs(best.transform.position.y - y);
            for (int i = 1; i < group.Items.Count; i++)
            {
                float dy = Mathf.Abs(group.Items[i].transform.position.y - y);
                if (dy < bestDy)
                {
                    best = group.Items[i];
                    bestDy = dy;
                }
            }

            return best;
        }

        private static readonly Comparison<Selectable> CompareX =
            (a, b) => a.transform.position.x.CompareTo(b.transform.position.x);

        private static readonly Comparison<Selectable> CompareYDesc =
            (a, b) => b.transform.position.y.CompareTo(a.transform.position.y);

        /// <summary>一行的全部可选项（根对象自己的 Selectable + 子级可选件；下拉展开项 / 滚动条除外）。</summary>
        private static List<Selectable>? CollectLineItems(Transform row)
        {
            List<Selectable> items = new List<Selectable>();
            Selectable? root = row.GetComponent<Selectable>();
            if (root != null && root.IsInteractable())
            {
                items.Add(root);
            }

            foreach (Selectable selectable in row.GetComponentsInChildren<Selectable>(true))
            {
                if (selectable == root || selectable is Scrollbar)
                {
                    continue;
                }

                // 下拉展开项是 TMP_Dropdown 的子节点，会被当成本行可选件误收进来；
                // 它们的导航归 TMP / DropdownNavFix 管，收进链会把左右改成「跳到旁边的默认键」
                if (selectable is not TMP_Dropdown && selectable.GetComponentInParent<TMP_Dropdown>() != null)
                {
                    continue;
                }

                if (!selectable.gameObject.activeInHierarchy || !selectable.IsInteractable())
                {
                    continue;
                }

                items.Add(selectable);
            }

            return items.Count > 0 ? items : null;
        }

        private Selectable? FindSearchField()
        {
            Transform? search = Page != null ? Page.transform.Find("SearchInput") : null;
            return search != null ? search.GetComponentInChildren<Selectable>(true) : null;
        }
    }

    // ── 选中滚入视野 ────────────────────────────────────────────────

    /// <summary>
    /// 挂在会被选中的对象上：OnSelect 起滚，Update 里每帧按当前世界矩形重算目标
    /// （content 正在滚动 → 行的世界位置在变 → 目标连续，跟手不抖），MoveTowards 到位即停。
    /// 不依赖 verticalScrollbar，嵌套（分区行在模组列表里）天然正确。
    /// </summary>
    internal sealed class FocusScrollIntoView : MonoBehaviour, ISelectHandler
    {
        internal ScrollRect? Scroll;
        internal RectTransform? Target;

        private bool _scrolling;
        private static readonly Vector3[] Corners = new Vector3[4];

        public void OnSelect(BaseEventData eventData)
        {
            if (IsGamepad)
            {
                _scrolling = true;
            }
        }

        private void OnDisable()
        {
            _scrolling = false;
        }

        private void Update()
        {
            if (!_scrolling)
            {
                return;
            }

            if (!IsGamepad || Scroll == null || Scroll.content == null)
            {
                _scrolling = false;
                return;
            }

            RectTransform viewport = Scroll.viewport != null ? Scroll.viewport : (RectTransform)Scroll.transform;
            RectTransform target = Target != null ? Target : (RectTransform)transform;

            float contentTop = TopY(Scroll.content);
            float contentBottom = BottomY(Scroll.content);
            float viewTop = TopY(viewport);
            float viewBottom = BottomY(viewport);
            float scrollable = contentTop - contentBottom - (viewTop - viewBottom);
            if (scrollable <= 0.5f)
            {
                _scrolling = false; // 装得下，不用滚
                return;
            }

            // verticalNormalizedPosition 1 = 顶（官方 ScrollRectAutoScrollerElement 同口径）
            float position = Scroll.verticalNormalizedPosition;
            float targetTop = TopY(target);
            float targetBottom = BottomY(target);
            if (targetTop > viewTop + 1f)
            {
                position += (targetTop - viewTop) / scrollable;
            }
            else if (targetBottom < viewBottom - 1f)
            {
                position -= (viewBottom - targetBottom) / scrollable;
            }
            else
            {
                _scrolling = false; // 已在视野内
                return;
            }

            position = Mathf.Clamp01(position);
            float next = Mathf.MoveTowards(Scroll.verticalNormalizedPosition, position, Time.unscaledDeltaTime * 10f);
            Scroll.verticalNormalizedPosition = next;
            if (Mathf.Approximately(next, position))
            {
                _scrolling = false;
            }
        }

        private static float TopY(RectTransform rect)
        {
            rect.GetWorldCorners(Corners);
            return Corners[1].y;
        }

        private static float BottomY(RectTransform rect)
        {
            rect.GetWorldCorners(Corners);
            return Corners[0].y;
        }
    }

    // ── 焦点框 ────────────────────────────────────────────────────

    /// <summary>
    /// 焦点框：藏在宿主最底下（SetAsFirstSibling）的一张外扩 2px 的圆角米白底图，
    /// 行自己的底图盖住中心、只露一圈描边；选中才激活。米白在 TimeTheme 表里，夜里自动变紫。
    /// <see cref="SetEngaged"/> 给滑条调值态用：焦点框换亮橙，提示「正在调值」。
    /// </summary>
    internal sealed class FocusHighlight : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private const float Outset = 2f;
        private static readonly Color EngagedColor = new Color(1f, 0.72f, 0.28f);

        private GameObject? _frame;
        private bool _engaged;

        public void OnSelect(BaseEventData eventData)
        {
            SetVisible(true);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            SetVisible(false);
            _engaged = false;
        }

        private void OnDisable()
        {
            SetVisible(false);
            _engaged = false;
        }

        internal void SetEngaged(bool engaged)
        {
            _engaged = engaged;
            if (_frame != null && _frame.TryGetComponent(out Image image))
            {
                image.color = engaged ? EngagedColor : ModConfigSidebarWidgets.Cream;
            }
        }

        private void SetVisible(bool visible)
        {
            if (visible && _frame == null)
            {
                _frame = CreateFrame((RectTransform)transform);
            }

            if (_frame != null && _frame.activeSelf != visible)
            {
                _frame.SetActive(visible);
            }
        }

        private static GameObject CreateFrame(RectTransform host)
        {
            // 焦点框贴在可视图形上：targetGraphic 的 rect 比 Selectable 自身 rect 更贴近可见形状
            // （「默认」丝带这类按钮底图比命中矩形小/有偏移，贴命中框就是「虚线位置不对」的根源）。
            Selectable? selectable = host.GetComponent<Selectable>();
            Transform parent = selectable != null && selectable.targetGraphic != null
                ? selectable.targetGraphic.transform
                : host;

            GameObject go = new GameObject("MCE_FocusFrame", typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-Outset, -Outset);
            rect.offsetMax = new Vector2(Outset, Outset);
            rect.SetAsLastSibling(); // 画在最上层：空心环不遮内容，也不挑底图在父在子

            Image image = go.GetComponent<Image>();
            image.sprite = RingSprite();
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1.2f; // 屏上描边 ≈ 2.5px、圆角 ≈ 10px
            image.color = ModConfigSidebarWidgets.Cream;
            image.raycastTarget = false;

            go.SetActive(false);
            return go;
        }

        private const int RingTexSize = 64;
        private const float RingRadiusPx = 12f;
        private const float RingStrokePx = 3f;
        private const int RingSliceBorder = 16;
        private static Sprite? _ringSprite;

        /// <summary>空心圆角环（程序化生成）：只有外缘一圈不透明，中间全透——盖在任何底图上都是描边。</summary>
        private static Sprite RingSprite()
        {
            if (_ringSprite != null)
            {
                return _ringSprite;
            }

            Texture2D texture = new Texture2D(RingTexSize, RingTexSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "MCE_FocusRing",
            };
            Color32[] pixels = new Color32[RingTexSize * RingTexSize];
            for (int y = 0; y < RingTexSize; y++)
            {
                for (int x = 0; x < RingTexSize; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float cx = Mathf.Clamp(px, RingRadiusPx, RingTexSize - RingRadiusPx);
                    float cy = Mathf.Clamp(py, RingRadiusPx, RingTexSize - RingRadiusPx);
                    float d = RingRadiusPx - Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    float alpha = Mathf.Clamp01(Mathf.Min(d, RingStrokePx - d) + 0.5f);
                    pixels[y * RingTexSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Object.DontDestroyOnLoad(texture);
            _ringSprite = Sprite.Create(texture, new Rect(0f, 0f, RingTexSize, RingTexSize), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(RingSliceBorder, RingSliceBorder, RingSliceBorder, RingSliceBorder));
            _ringSprite.name = "MCE_FocusRing";
            Object.DontDestroyOnLoad(_ringSprite);
            return _ringSprite;
        }
    }

    // ── 下拉展开项导航 ─────────────────────────────────────────────

    /// <summary>
    /// 挂在 TMP_Dropdown 上：展开瞬间把项导航重链成纯上下。
    /// TMP 自己设的链是「上/左 = 前一项、下/右 = 后一项」——左右也能在项间跳，手感反直觉；
    /// 重链后左右什么都不做，退出只能靠选完或 B。
    /// </summary>
    internal sealed class DropdownNavFix : MonoBehaviour
    {
        private TMP_Dropdown? _dropdown;
        private bool _wasExpanded;

        private void Awake()
        {
            _dropdown = GetComponent<TMP_Dropdown>();
        }

        private void Update()
        {
            if (_dropdown == null)
            {
                return;
            }

            bool expanded = _dropdown.IsExpanded;
            if (expanded && !_wasExpanded && IsGamepad)
            {
                RelinkItems();
            }

            _wasExpanded = expanded;
        }

        private void RelinkItems()
        {
            List<Toggle> items = new List<Toggle>(_dropdown!.GetComponentsInChildren<Toggle>(false));
            for (int i = 0; i < items.Count; i++)
            {
                Navigation nav = items[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = i > 0 ? items[i - 1] : null;
                nav.selectOnDown = i < items.Count - 1 ? items[i + 1] : null;
                nav.selectOnLeft = null;
                nav.selectOnRight = null;
                items[i].navigation = nav;
            }
        }
    }

    // ── 输入框编辑态指示 ─────────────────────────────────────────────

    /// <summary>
    /// 挂在 TMP_InputField 上：编辑中时把焦点框换成亮橙（同滑条调值态）。
    /// 「选中不激活」靠 <see cref="TMP_InputField.shouldActivateOnSelect"/> = false
    /// （MarkSelectable 里关）；A 键走 TMP 自己的 OnSubmit → m_ShouldActivateNextUpdate 激活，
    /// B 键走 PanelFocus.ConsumeBack → DeactivateInputField。
    /// </summary>
    internal sealed class InputFieldGate : MonoBehaviour
    {
        private TMP_InputField? _input;
        private FocusHighlight? _highlight;
        private bool _engaged;

        private void Awake()
        {
            _input = GetComponent<TMP_InputField>();
        }

        private void Update()
        {
            bool focused = _input != null && _input.isFocused;
            if (focused == _engaged)
            {
                return;
            }

            _engaged = focused;
            if (_highlight == null)
            {
                _highlight = GetComponent<FocusHighlight>();
            }

            _highlight?.SetEngaged(focused);
        }
    }

    // ── 按键捕获武装态指示 ──────────────────────────────────────────

    /// <summary>按键绑定框：ModConfig 捕获服务正盯着本格时焦点框亮橙（武装态提示）。</summary>
    internal sealed class CaptureGlow : MonoBehaviour
    {
        private InputBindingSettingUI? _cell;
        private FocusHighlight? _highlight;
        private bool _on;

        private void Awake()
        {
            _cell = GetComponent<InputBindingSettingUI>();
        }

        private void Update()
        {
            InputBindingCaptureService? service = CaptureService();
            bool on = service != null && ReferenceEquals(service.owner, _cell);
            if (on == _on)
            {
                return;
            }

            _on = on;
            if (_highlight == null)
            {
                _highlight = GetComponent<FocusHighlight>();
            }

            _highlight?.SetEngaged(on);
        }
    }

    // ── 滑条门控（A 键调值）─────────────────────────────────────────

    /// <summary>
    /// 官方 SelectableSlider 同款语义：焦点落在滑条上只算「选中」，按 A（Submit）进入调值态
    /// 后左右才改值；再按 A 或 B 退出。未激活时 Slider.OnMove 的 prefix 把方向输入全部改成导航。
    /// 调值由本 Update 的计时器驱动：按下沿立即一步，按住 1s 后 10 步/秒、3s 后 40 步/秒。
    /// </summary>
    internal sealed class SliderGate : MonoBehaviour, ISubmitHandler, IDeselectHandler
    {
        private const float HoldDelay = 1f;
        private const float RepeatPerSecond = 10f;
        private const float FastRepeatAfter = 3f;
        private const float FastRepeatPerSecond = 40f;

        internal bool Engaged;

        private Slider? _slider;
        private FocusHighlight? _highlight;
        private int _dir;
        private float _held;
        private float _accum;

        private void Awake()
        {
            _slider = GetComponent<Slider>();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (!IsGamepad)
            {
                return;
            }

            Engaged = !Engaged;
            _dir = 0;
            _held = 0f;
            _accum = 0f;
            Highlight()?.SetEngaged(Engaged);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            Disengage();
        }

        private void OnDisable()
        {
            Disengage();
        }

        internal void Disengage()
        {
            Engaged = false;
            _dir = 0;
            Highlight()?.SetEngaged(false);
        }

        private FocusHighlight? Highlight()
        {
            return _highlight != null ? _highlight : _highlight = GetComponent<FocusHighlight>();
        }

        private void Update()
        {
            if (!Engaged || _slider == null || !IsGamepad || NavAction == null)
            {
                _dir = 0;
                _held = 0f;
                _accum = 0f;
                return;
            }

            float x = NavAction.ReadValue<Vector2>().x;
            int dir = x > 0.5f ? 1 : x < -0.5f ? -1 : 0;
            if (dir == 0)
            {
                _dir = 0;
                _held = 0f;
                _accum = 0f;
                return;
            }

            if (dir != _dir)
            {
                _dir = dir;
                _held = 0f;
                _accum = 0f;
                Step(dir); // 按下沿立即一步
                return;
            }

            _held += Time.unscaledDeltaTime;
            if (_held < HoldDelay)
            {
                return;
            }

            _accum += Time.unscaledDeltaTime * (_held >= FastRepeatAfter ? FastRepeatPerSecond : RepeatPerSecond);
            while (_accum >= 1f)
            {
                _accum -= 1f;
                Step(_dir);
            }
        }

        private void Step(int dir)
        {
            float range = _slider!.maxValue - _slider.minValue;
            if (range <= 0f)
            {
                return;
            }

            bool reversed = _slider.direction is Slider.Direction.RightToLeft or Slider.Direction.TopToBottom;
            float step = _slider.wholeNumbers
                ? Mathf.Max(1f, Mathf.Round(range * 0.005f))
                : Mathf.Min(range * 0.1f, range * 0.005f);
            _slider.value = Mathf.Clamp(_slider.value + step * dir * (reversed ? -1 : 1), _slider.minValue, _slider.maxValue);
        }
    }

    // ── 肩键切模组 ──────────────────────────────────────────────────

    /// <summary>
    /// 挂在模组列表上：LB/RB（UITabLeft/UITabRight）按显示顺序切模组——直属 content 且激活的
    /// 模组行才算（跳过表头、被筛选藏掉的行、嵌在里面的分区行；不用 TABS.SelectRelative，
    /// 它的 buttons 会把挪进列表的分区行也算进去、且不认置顶排序）。
    /// 切完把焦点落到新选中行：行上的 FocusScrollIntoView 自动把它滚回视野。
    /// 只在锁左栏时响应（锁右栏时切模组会换掉正在看的选项，太诡异）。
    /// </summary>
    internal sealed class SidebarTabHotkeys : MonoBehaviour
    {
        internal ModSettingsMenu? Menu;
        internal PeakChildPage? Page;

        private UIPageHandler? _handler;
        private InputAction? _left;
        private InputAction? _right;
        private bool _actionsResolved;

        private void Awake()
        {
            _handler = GetComponentInParent<UIPageHandler>(true);
        }

        private void Update()
        {
            if (!IsGamepad || Menu == null || Page == null || Menu.ModTabs == null || Menu.ModTabController == null)
            {
                return;
            }

            if (_handler == null || _handler.currentPage != Page)
            {
                return;
            }

            PanelFocus? focus = Page.GetComponent<PanelFocus>();
            if (focus != null && focus.InOptions)
            {
                return;
            }

            if (!ResolveActions())
            {
                return;
            }

            bool left = _left!.WasPressedThisFrame();
            bool right = !left && _right!.WasPressedThisFrame();
            if (!left && !right)
            {
                return;
            }

            if (Overlaid() || CaptureActive() || TypingGuard.Active || AnyDropdownOpen(Page.transform))
            {
                return;
            }

            ModdedTABSButton? next = SelectModRelative(left ? -1 : 1);
            if (next != null)
            {
                EventSystem.current?.SetSelectedGameObject(next.gameObject);
            }
        }

        private bool ResolveActions()
        {
            if (_actionsResolved)
            {
                return _left != null && _right != null;
            }

            _actionsResolved = true;
            try
            {
                _left = InputSystem.actions.FindAction("UITabLeft");
                _right = InputSystem.actions.FindAction("UITabRight");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[手柄] UI 页签动作解析失败: {ex.Message}");
            }

            if (_left == null || _right == null)
            {
                Plugin.Log.LogWarning("[手柄] 找不到 UITabLeft/UITabRight 动作，肩键切模组不可用。");
            }

            return _left != null && _right != null;
        }

        private ModdedTABSButton? SelectModRelative(int delta)
        {
            ScrollRect? scroll = Menu!.ModTabController!.GetComponent<ScrollRect>();
            if (scroll == null || scroll.content == null)
            {
                return null;
            }

            List<ModdedTABSButton> rows = new List<ModdedTABSButton>();
            foreach (Transform child in scroll.content)
            {
                if (!child.gameObject.activeInHierarchy)
                {
                    continue;
                }

                ModdedTABSButton? button = child.GetComponent<ModdedTABSButton>();
                if (button != null)
                {
                    rows.Add(button);
                }
            }

            if (rows.Count == 0)
            {
                return null;
            }

            int index = rows.IndexOf(Menu.ModTabs.selectedButton);
            index = index < 0 ? 0 : (index + delta + rows.Count) % rows.Count;
            ModdedTABSButton target = rows[index];
            Menu.ModTabs.Select(target);
            return target;
        }
    }
}
