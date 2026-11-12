using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UITK = UnityEngine.UIElements;

namespace MapHack
{
    /// <summary>
    /// Prevents the game's chat from opening on top of the MapHack settings panel while it is open.
    /// </summary>
    [HarmonyPatch(typeof(UIChat), "StartInput")]
    internal static class ChatClosedWhileMapHackOpen
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !MapHackUI.PanelOpen;
        }
    }

    public sealed class MapHackUI : MonoBehaviour
    {
        public static MapHackUI Instance { get; private set; }
        internal static bool PanelOpen => Instance != null && Instance._isVisible;

        private enum UiTab { Layout, Fade, Icons, Radar }

        // OWPUI / PonceArenaTweaks palette verbatim
        private static readonly Color PanelBg       = new Color32(20, 20, 24, 245);
        private static readonly Color PanelBorder   = new Color32(48, 48, 56, 255);
        private static readonly Color SectionBg     = new Color32(26, 26, 32, 255);
        private static readonly Color SectionBorder = new Color32(38, 38, 46, 255);
        private static readonly Color RowBg         = new Color32(34, 34, 40, 255);
        private static readonly Color RowBgHover    = new Color32(44, 44, 52, 255);
        private static readonly Color TextFieldBg   = new Color32(28, 28, 34, 255);
        private static readonly Color ButtonBg      = new Color32(48, 48, 56, 255);
        private static readonly Color TabActiveBg   = new Color32(34, 34, 40, 255);
        private static readonly Color TabInactiveBg = new Color32(24, 24, 30, 255);
        private static readonly Color Backdrop      = new Color32(0, 0, 0, 140);
        private static readonly Color Accent        = new Color32(74, 158, 255, 255);
        private static readonly Color AccentDim     = new Color32(40, 100, 170, 255);
        private static readonly Color Danger        = new Color32(220, 80, 80, 255);
        private static readonly Color TextPrimary   = new Color32(240, 240, 245, 255);
        private static readonly Color TextMuted     = new Color32(155, 155, 165, 255);
        private static readonly Color TextDim       = new Color32(110, 110, 120, 255);

        private const float ROW_RADIUS = 6f;
        private const float BTN_RADIUS = 5f;
        private const float SECTION_RADIUS = 8f;

        private const string RowClass = "mh-row";
        private const string SectionClass = "mh-section";

        private static readonly List<string> ModeOptions = new List<string> { "vanilla", "center", "top", "bottom" };
        private static readonly List<string> ColorOptions = new List<string> { "Blue", "Cyan", "Green", "Yellow", "Red", "White" };

        private UITK.UIDocument _doc;
        private UITK.VisualElement _backdrop, _panel;
        private UITK.TextField _searchField;
        private string _searchQuery = "";
        private UITK.Label _searchHint;
        private UITK.Label _subtitleLabel;

        private UITK.Button _tabLayout, _tabFade, _tabIcons, _tabRadar;
        private UITK.ScrollView _layoutBody, _fadeBody, _iconsBody, _radarBody;
        private UiTab _activeTab = UiTab.Layout;

        private static Action _cancelSliderDrag;
        private static Action _cancelSearchEdit;
        internal static bool TextEntryActive;

        private bool _isVisible;
        private bool _savedCursorState;
        private CursorLockMode _prevLockState = CursorLockMode.None;
        private bool _prevCursorVisible;
        private bool _savedMouseRequired;
        private bool _prevIsMouseRequired;

        private static Font _uiFont;

        public static void Initialize()
        {
            if (Instance != null) return;
            var go = new GameObject("__MapHackUI__");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<MapHackUI>();
            Plugin.Log($"UI Initialized. Press {ModConfig.ResolvedToggleKey} to open MapHack settings.");
        }

        public static void Shutdown()
        {
            if (Instance != null)
            {
                Destroy(Instance.gameObject);
                Instance = null;
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_isVisible) Hide();
            if (_backdrop != null) { _backdrop.RemoveFromHierarchy(); _backdrop = null; }
            if (_panel != null) { _panel.RemoveFromHierarchy(); _panel = null; }
            _doc = null;
        }

        private static Font GetUIFont()
        {
            if (_uiFont != null) return _uiFont;
            try
            {
                var ui = MonoBehaviourSingleton<UIManager>.Instance;
                var ts = ui != null && ui.PanelSettings != null ? ui.PanelSettings.textSettings : null;
                if (ts != null && ts.defaultFontAsset != null)
                    _uiFont = ts.defaultFontAsset.sourceFontFile;
            }
            catch { }
            return _uiFont;
        }

        private static void ForceUIFont(UITK.VisualElement ve)
        {
            var f = GetUIFont();
            if (f != null) ve.style.unityFont = f;
        }

        private UITK.VisualElement Root()
        {
            if (_doc == null)
            {
                var ui = UnityEngine.Object.FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
                _doc = ui != null ? ui.UIDocument
                                  : UnityEngine.Object.FindFirstObjectByType<UITK.UIDocument>(FindObjectsInactive.Include);
            }
            return _doc != null ? _doc.rootVisualElement : null;
        }

        private void Update()
        {
            HandleHotkey();
        }

        private void HandleHotkey()
        {
            var kb = Keyboard.current;
            if (kb == null || IsChatOpen() || TextEntryActive) return;

            if (kb[ModConfig.ResolvedToggleKey].wasPressedThisFrame)
            {
                Toggle();
            }
            else if (_isVisible && kb[Key.Escape].wasPressedThisFrame)
            {
                Hide();
            }
        }

        private static bool IsChatOpen()
        {
            try
            {
                var chat = MonoBehaviourSingleton<UIManager>.Instance?.Chat;
                if (chat != null && chat.IsFocused) return true;
            }
            catch { }
            return false;
        }

        public void Toggle()
        {
            if (_isVisible) Hide();
            else Show();
        }

        public void Show()
        {
            if (_panel == null || _panel.panel == null)
            {
                if (_backdrop != null) { _backdrop.RemoveFromHierarchy(); _backdrop = null; }
                if (_panel != null) { _panel.RemoveFromHierarchy(); _panel = null; }
                _doc = null;
                Create();
            }
            if (_panel == null) return;

            _prevLockState     = UnityEngine.Cursor.lockState;
            _prevCursorVisible = UnityEngine.Cursor.visible;
            _savedCursorState  = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible   = true;

            // Hide scoreboard if open so we capture a clean resting IsMouseRequired state
            try
            {
                var sb = MonoBehaviourSingleton<UIManager>.Instance?.Scoreboard;
                if (sb != null && sb.IsVisible) sb.Hide();
            }
            catch { }

            SuppressPlayerInput();

            if (_subtitleLabel != null)
                _subtitleLabel.text = $"Minimap & radar customization  ·  {ModConfig.ResolvedToggleKey} to toggle  ·  Esc to close";

            Rebuild();

            _backdrop.style.display = UITK.DisplayStyle.Flex;
            _panel.style.display    = UITK.DisplayStyle.Flex;
            _backdrop.BringToFront();
            _panel.BringToFront();

            _isVisible = true;
        }

        public void Hide()
        {
            if (_panel == null) return;

            EndTransientEdits(true);

            _backdrop.style.display = UITK.DisplayStyle.None;
            _panel.style.display    = UITK.DisplayStyle.None;
            _isVisible = false;

            RestorePlayerInput();
            RestoreCursor();
        }

        private void SuppressPlayerInput()
        {
            try
            {
                _prevIsMouseRequired = GlobalStateManager.UIState.IsMouseRequired;
                _savedMouseRequired = true;
                if (!_prevIsMouseRequired)
                    GlobalStateManager.SetUIState(new Dictionary<string, object> { { "isMouseRequired", true } });
            }
            catch (Exception ex) { Plugin.LogError("SuppressPlayerInput failed: " + ex.Message); }
        }

        private void RestorePlayerInput()
        {
            if (!_savedMouseRequired) return;
            try
            {
                GlobalStateManager.SetUIState(new Dictionary<string, object> { { "isMouseRequired", _prevIsMouseRequired } });
            }
            catch (Exception ex) { Plugin.LogError("RestorePlayerInput failed: " + ex.Message); }
            _savedMouseRequired = false;
        }

        private void RestoreCursor()
        {
            if (!_savedCursorState) return;
            UnityEngine.Cursor.lockState = _prevLockState;
            UnityEngine.Cursor.visible   = _prevCursorVisible;
            _savedCursorState = false;
        }

        private static void EndTransientEdits(bool includeSearch)
        {
            var cancelDrag = _cancelSliderDrag; _cancelSliderDrag = null; cancelDrag?.Invoke();
            if (!includeSearch) return;
            var endSearch = _cancelSearchEdit; _cancelSearchEdit = null; endSearch?.Invoke();
        }

        // ── shell ─────────────────────────────────────────────────────

        private void Create()
        {
            var root = Root();
            if (root == null) { Plugin.Log("No UIDocument yet; panel not built."); return; }

            _backdrop = new UITK.VisualElement { name = "MapHack_Backdrop" };
            var bs = _backdrop.style;
            bs.position = UITK.Position.Absolute;
            bs.left = 0; bs.top = 0; bs.right = 0; bs.bottom = 0;
            bs.backgroundColor = Backdrop;
            bs.display = UITK.DisplayStyle.None;
            _backdrop.pickingMode = UITK.PickingMode.Position;
            _backdrop.RegisterCallback<UITK.PointerUpEvent>(_ => Hide());
            root.Add(_backdrop);

            _panel = new UITK.VisualElement { name = "MapHack_Panel" };
            var ps = _panel.style;
            ps.position = UITK.Position.Absolute;
            ps.left = new UITK.Length(50, UITK.LengthUnit.Percent);
            ps.top  = new UITK.Length(50, UITK.LengthUnit.Percent);
            ps.translate = new UITK.Translate(
                new UITK.Length(-50, UITK.LengthUnit.Percent),
                new UITK.Length(-50, UITK.LengthUnit.Percent), 0f);
            ps.width = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.52f), 680, 920);
            ps.height    = new UITK.Length(78, UITK.LengthUnit.Percent);
            ps.minHeight = new UITK.Length(56, UITK.LengthUnit.Percent);
            ps.maxHeight = new UITK.Length(82, UITK.LengthUnit.Percent);
            ps.overflow = UITK.Overflow.Hidden;
            ps.flexDirection = UITK.FlexDirection.Column;
            ps.backgroundColor = PanelBg;
            ps.paddingLeft = 18; ps.paddingRight = 18; ps.paddingTop = 16; ps.paddingBottom = 14;
            ps.borderTopLeftRadius = 12; ps.borderTopRightRadius = 12;
            ps.borderBottomLeftRadius = 12; ps.borderBottomRightRadius = 12;
            SetBorderColor(_panel, PanelBorder); SetBorderWidth(_panel, 1f);
            ps.display = UITK.DisplayStyle.None;
            _panel.RegisterCallback<UITK.PointerUpEvent>(e => e.StopPropagation());
            root.Add(_panel);

            // Header Row
            var headerRow = new UITK.VisualElement();
            headerRow.style.flexDirection = UITK.FlexDirection.Row;
            headerRow.style.alignItems = UITK.Align.FlexEnd;
            headerRow.style.flexShrink = 0;
            _panel.Add(headerRow);

            var titleCol = new UITK.VisualElement();
            titleCol.style.flexDirection = UITK.FlexDirection.Column;
            titleCol.style.flexGrow = 1;
            titleCol.style.flexShrink = 1;
            titleCol.style.minWidth = 0;
            headerRow.Add(titleCol);

            var titleRow = new UITK.VisualElement();
            titleRow.style.flexDirection = UITK.FlexDirection.Row;
            titleRow.style.alignItems = UITK.Align.Center;
            titleRow.style.marginBottom = 4;
            titleCol.Add(titleRow);
            titleRow.Add(Title("MAP", TextPrimary));
            titleRow.Add(Title("HACK", Accent));

            var searchAside = new UITK.VisualElement();
            searchAside.style.flexDirection = UITK.FlexDirection.Row;
            searchAside.style.alignItems = UITK.Align.Center;
            searchAside.style.flexShrink = 0;
            searchAside.style.marginBottom = 12;

            var searchLabel = new UITK.Label("SEARCH");
            searchLabel.style.fontSize = 11;
            searchLabel.style.color = TextMuted;
            searchLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            searchLabel.style.letterSpacing = 3;
            searchLabel.style.marginRight = 8;
            ForceUIFont(searchLabel);
            searchAside.Add(searchLabel);

            _searchField = new UITK.TextField { value = _searchQuery };
            _searchField.style.width = 180;
            _searchField.style.height = 28;
            StyleSearchField(_searchField);
            _searchField.RegisterValueChangedCallback(e =>
            {
                _searchQuery = e.newValue ?? "";
                ApplySearchFilter();
            });
            _searchField.RegisterCallback<UITK.FocusInEvent>(_ =>
            {
                TextEntryActive = true;
                _cancelSearchEdit = () =>
                {
                    TextEntryActive = false;
                    _cancelSearchEdit = null;
                    _searchField?.Blur();
                };
            });
            _searchField.RegisterCallback<UITK.FocusOutEvent>(_ =>
            {
                TextEntryActive = false;
                _cancelSearchEdit = null;
            });
            _searchField.RegisterCallback<UITK.KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                if (!string.IsNullOrEmpty(_searchQuery))
                {
                    _searchField.value = "";
                    e.StopPropagation();
                }
                else _searchField.Blur();
            });
            searchAside.Add(_searchField);

            _searchHint = new UITK.Label("");
            _searchHint.style.fontSize = 11;
            _searchHint.style.color = TextDim;
            _searchHint.style.marginLeft = 8;
            ForceUIFont(_searchHint);
            searchAside.Add(_searchHint);

            headerRow.Add(searchAside);

            _subtitleLabel = new UITK.Label($"Minimap & radar customization  ·  {ModConfig.ResolvedToggleKey} to toggle  ·  Esc to close");
            _subtitleLabel.style.fontSize = 12;
            _subtitleLabel.style.color = TextMuted;
            _subtitleLabel.style.marginBottom = 12;
            ForceUIFont(_subtitleLabel);
            titleCol.Add(_subtitleLabel);

            // Tabs
            var tabs = new UITK.VisualElement();
            tabs.style.flexDirection = UITK.FlexDirection.Row;
            tabs.style.flexWrap = UITK.Wrap.Wrap;
            _panel.Add(tabs);

            _tabLayout = MakeTab("Layout & Ghost", () => ShowTab(UiTab.Layout), () => _activeTab == UiTab.Layout);
            _tabFade   = MakeTab("Proximity Fade", () => ShowTab(UiTab.Fade),   () => _activeTab == UiTab.Fade);
            _tabIcons  = MakeTab("Icons & Ring",   () => ShowTab(UiTab.Icons),  () => _activeTab == UiTab.Icons);
            _tabRadar  = MakeTab("3D Radar",       () => ShowTab(UiTab.Radar),  () => _activeTab == UiTab.Radar);

            tabs.Add(_tabLayout);
            tabs.Add(_tabFade);
            tabs.Add(_tabIcons);
            tabs.Add(_tabRadar);

            _layoutBody = MakeBody();
            _fadeBody   = MakeBody();
            _iconsBody  = MakeBody();
            _radarBody  = MakeBody();

            _panel.Add(_layoutBody);
            _panel.Add(_fadeBody);
            _panel.Add(_iconsBody);
            _panel.Add(_radarBody);

            // Footer
            var footer = new UITK.VisualElement();
            footer.style.flexDirection = UITK.FlexDirection.Row;
            footer.style.justifyContent = UITK.Justify.SpaceBetween;
            footer.style.alignItems = UITK.Align.Center;
            footer.style.marginTop = 12;
            _panel.Add(footer);

            var leftInfo = new UITK.Label("Changes apply live to the minimap.");
            leftInfo.style.fontSize = 11;
            leftInfo.style.color = TextDim;
            ForceUIFont(leftInfo);
            footer.Add(leftInfo);

            var actions = new UITK.VisualElement();
            actions.style.flexDirection = UITK.FlexDirection.Row;
            footer.Add(actions);

            actions.Add(MakeFooterButton("Reset all", () =>
            {
                ModConfig.ResetToDefaults();
                MinimapPatcher.ForceRefreshLayout();
                Rebuild();
            }));
            actions.Add(MakeFooterButton("Close", Hide));

            ShowTab(_activeTab);
        }

        private void ShowTab(UiTab t)
        {
            EndTransientEdits(false);
            _activeTab = t;
            _layoutBody.style.display = t == UiTab.Layout ? UITK.DisplayStyle.Flex : UITK.DisplayStyle.None;
            _fadeBody.style.display   = t == UiTab.Fade   ? UITK.DisplayStyle.Flex : UITK.DisplayStyle.None;
            _iconsBody.style.display  = t == UiTab.Icons  ? UITK.DisplayStyle.Flex : UITK.DisplayStyle.None;
            _radarBody.style.display  = t == UiTab.Radar  ? UITK.DisplayStyle.Flex : UITK.DisplayStyle.None;

            SetTabVisual(_tabLayout, t == UiTab.Layout);
            SetTabVisual(_tabFade,   t == UiTab.Fade);
            SetTabVisual(_tabIcons,  t == UiTab.Icons);
            SetTabVisual(_tabRadar,  t == UiTab.Radar);

            ApplySearchFilter();
        }

        private void Rebuild()
        {
            if (_panel == null) return;
            EndTransientEdits(false);

            _layoutBody.Clear();
            _fadeBody.Clear();
            _iconsBody.Clear();
            _radarBody.Clear();

            BuildLayoutTab();
            BuildFadeTab();
            BuildIconsTab();
            BuildRadarTab();

            ApplySearchFilter();
        }

        // ── tab builders ──────────────────────────────────────────────

        private void BuildLayoutTab()
        {
            var cfg = ModConfig.Config;

            var presets = AddSection(_layoutBody, "Quick Presets",
                "One-click setups for widescreen play. You can fine-tune every setting below afterwards.");

            presets.Add(BuildActionRow("Horizontal Top Bar",
                "Rotates the map horizontally at the top-center below the scoreboard, enables Ghost Mode and Proximity Fade.",
                "APPLY", () =>
                {
                    cfg.MapMode = "top";
                    cfg.MapOffsetX = 0f;
                    cfg.MapOffsetY = 0f;
                    cfg.GhostMode = true;
                    cfg.ProximityFadeEnabled = true;
                    ModConfig.SaveConfig();
                    MinimapPatcher.ForceRefreshLayout();
                    Rebuild();
                }));

            presets.Add(BuildActionRow("Horizontal Bottom Bar",
                "Rotates the map horizontally at the bottom-center over your stick shaft, with Ghost Mode and Proximity Fade.",
                "APPLY", () =>
                {
                    cfg.MapMode = "bottom";
                    cfg.MapOffsetX = 0f;
                    cfg.MapOffsetY = 0f;
                    cfg.GhostMode = true;
                    cfg.ProximityFadeEnabled = true;
                    ModConfig.SaveConfig();
                    MinimapPatcher.ForceRefreshLayout();
                    Rebuild();
                }));

            presets.Add(BuildActionRow("ARPG Center Ghost",
                "Places the map dead-center with a transparent background that fades out whenever the puck approaches your stick.",
                "APPLY", () =>
                {
                    cfg.MapMode = "center";
                    cfg.MapOffsetX = 0f;
                    cfg.MapOffsetY = 0f;
                    cfg.GhostMode = true;
                    cfg.ProximityFadeEnabled = true;
                    ModConfig.SaveConfig();
                    MinimapPatcher.ForceRefreshLayout();
                    Rebuild();
                }));

            var pos = AddSection(_layoutBody, "Layout & Position",
                "Where the minimap sits and how it is oriented. Horizontal modes automatically align directional cones and keep player numbers upright.");

            pos.Add(BuildChoiceRow("Map mode",
                "Vanilla keeps the game's default placement. Top and Bottom rotate the rink -90 degrees into a horizontal bar.",
                ModeOptions,
                () => cfg.MapMode,
                v =>
                {
                    cfg.MapMode = v;
                    ModConfig.SaveConfig();
                    MinimapPatcher.ForceRefreshLayout();
                    Rebuild();
                }));

            if (!string.Equals(cfg.MapMode, "vanilla", StringComparison.OrdinalIgnoreCase))
            {
                pos.Add(BuildSliderRow("Map scale",
                    "Scales the entire minimap container. 1.00 is the base size.",
                    0.4f, 2.5f, "0.00",
                    () => cfg.CustomMapScale,
                    v => { cfg.CustomMapScale = v; MinimapPatcher.ForceRefreshLayout(); },
                    v => { cfg.CustomMapScale = v; ModConfig.SaveConfig(); MinimapPatcher.ForceRefreshLayout(); }));

                pos.Add(BuildSliderRow("Horizontal offset (X%)",
                    "Nudges the map left (negative) or right (positive) across the screen.",
                    -200f, 200f, "0.0",
                    () => cfg.MapOffsetX,
                    v => { cfg.MapOffsetX = v; MinimapPatcher.ForceRefreshLayout(); },
                    v => { cfg.MapOffsetX = v; ModConfig.SaveConfig(); MinimapPatcher.ForceRefreshLayout(); }));

                pos.Add(BuildSliderRow("Vertical offset (Y%)",
                    "Nudges the map up (negative) or down (positive) on the screen.",
                    -100f, 100f, "0.0",
                    () => cfg.MapOffsetY,
                    v => { cfg.MapOffsetY = v; MinimapPatcher.ForceRefreshLayout(); },
                    v => { cfg.MapOffsetY = v; ModConfig.SaveConfig(); MinimapPatcher.ForceRefreshLayout(); }));

                pos.Add(BuildActionRow("Center offsets",
                    "Resets Horizontal and Vertical offsets back to 0.",
                    "CENTER", () =>
                    {
                        cfg.MapOffsetX = 0f;
                        cfg.MapOffsetY = 0f;
                        ModConfig.SaveConfig();
                        MinimapPatcher.ForceRefreshLayout();
                        Rebuild();
                    }));
            }

            var ghost = AddSection(_layoutBody, "Ghost Background",
                "Strips out the bright white ice fill of the minimap while keeping the rink lines, creases, and player icons at full contrast.");

            ghost.Add(BuildToggleRow("Ghost mode",
                "Removes the solid white background so you can see the 3D ice and puck right through the map.",
                () => cfg.GhostMode,
                v =>
                {
                    cfg.GhostMode = v;
                    ModConfig.SaveConfig();
                    MinimapPatcher.ForceRefreshLayout();
                    Rebuild();
                }));

            if (cfg.GhostMode)
            {
                ghost.Add(BuildSliderRow("Background alpha",
                    "How much of the white rink surface remains visible. 0.00 is completely clear; 0.08 leaves a subtle glass tint.",
                    0f, 0.6f, "0.00",
                    () => cfg.GhostBackgroundAlpha,
                    v => { cfg.GhostBackgroundAlpha = v; MinimapPatcher.ForceRefreshLayout(); },
                    v => { cfg.GhostBackgroundAlpha = v; ModConfig.SaveConfig(); MinimapPatcher.ForceRefreshLayout(); }));
            }
        }

        private void BuildFadeTab()
        {
            var cfg = ModConfig.Config;

            var fade = AddSection(_fadeBody, "Proximity Auto-Fade",
                "Automatically fades the minimap out when the puck gets close to your stick so your central vision is unobstructed for stick-handling and shooting.");

            fade.Add(BuildToggleRow("Enable proximity auto-fade",
                "Tracks the 3D distance between your player and the closest puck every frame.",
                () => cfg.ProximityFadeEnabled,
                v => { cfg.ProximityFadeEnabled = v; ModConfig.SaveConfig(); Rebuild(); }));

            if (!cfg.ProximityFadeEnabled) return;

            fade.Add(BuildSliderRow("Close opacity (puck on stick)",
                "Map opacity when the puck is inside the close distance threshold.",
                0.02f, 1.0f, "0.00",
                () => cfg.CloseOpacity,
                v => cfg.CloseOpacity = v,
                v => { cfg.CloseOpacity = v; ModConfig.SaveConfig(); }));

            fade.Add(BuildSliderRow("Far opacity (macro play)",
                "Map opacity when the puck is far away and you are reading team positioning.",
                0.1f, 1.0f, "0.00",
                () => cfg.FarOpacity,
                v => cfg.FarOpacity = v,
                v => { cfg.FarOpacity = v; ModConfig.SaveConfig(); }));

            fade.Add(BuildSliderRow("Close distance (meters)",
                "Distance at or below which the map reaches its minimum Close Opacity.",
                1f, 25f, "0.0",
                () => cfg.FadeMinDist,
                v => cfg.FadeMinDist = Mathf.Min(v, cfg.FadeMaxDist - 0.5f),
                v => { cfg.FadeMinDist = Mathf.Min(v, cfg.FadeMaxDist - 0.5f); ModConfig.SaveConfig(); Rebuild(); }));

            fade.Add(BuildSliderRow("Far distance (meters)",
                "Distance at or above which the map reaches its full Far Opacity.",
                2f, 50f, "0.0",
                () => cfg.FadeMaxDist,
                v => cfg.FadeMaxDist = Mathf.Max(v, cfg.FadeMinDist + 0.5f),
                v => { cfg.FadeMaxDist = Mathf.Max(v, cfg.FadeMinDist + 0.5f); ModConfig.SaveConfig(); Rebuild(); }));
        }

        private void BuildIconsTab()
        {
            var cfg = ModConfig.Config;

            var icons = AddSection(_iconsBody, "Minimap Icons",
                "Scales player and puck icons independently of the minimap container.");

            icons.Add(BuildSliderRow("Icon scale",
                "Multiplier for player arrows, jersey numbers, and puck icons. 1.75 is the MapHack default.",
                0.5f, 3.5f, "0.00",
                () => cfg.IconScale,
                v => cfg.IconScale = v,
                v => { cfg.IconScale = v; ModConfig.SaveConfig(); }));

            var ring = AddSection(_iconsBody, "Airborne Puck Ring",
                "Draws a colored border ring around the puck icon when the puck is lifted off the ice.");

            ring.Add(BuildToggleRow("Show airborne ring",
                "Toggles the elevation indicator ring on both the minimap and the 3D radar.",
                () => cfg.RingEnabled,
                v => { cfg.RingEnabled = v; ModConfig.SaveConfig(); Rebuild(); }));

            if (!cfg.RingEnabled) return;

            ring.Add(BuildSliderRow("Activation height",
                "Minimum puck elevation before the airborne ring appears.",
                0f, 3f, "0.00",
                () => cfg.RingActivationHeight,
                v => cfg.RingActivationHeight = v,
                v => { cfg.RingActivationHeight = v; ModConfig.SaveConfig(); }));

            ring.Add(BuildChoiceRow("Minimum height color",
                "Color of the ring when the puck first leaves the ice.",
                ColorOptions,
                () => cfg.MinHeightColor,
                v => { cfg.MinHeightColor = v; ModConfig.SaveConfig(); }));

            ring.Add(BuildChoiceRow("Maximum height color",
                "Color the ring blends toward as the puck reaches peak elevation.",
                ColorOptions,
                () => cfg.MaxHeightColor,
                v => { cfg.MaxHeightColor = v; ModConfig.SaveConfig(); }));
        }

        private void BuildRadarTab()
        {
            var cfg = ModConfig.Config;

            var radar = AddSection(_radarBody, "3D-to-2D Crosshair Radar Ring",
                "Projects a 360-degree halo around your crosshair showing the exact direction and proximity of teammates, opponents, and the puck without looking away from the ice.");

            radar.Add(BuildToggleRow("Enable crosshair radar ring",
                "Draws player and puck dots around the center of your screen.",
                () => cfg.RadarRingEnabled,
                v => { cfg.RadarRingEnabled = v; ModConfig.SaveConfig(); Rebuild(); }));

            if (!cfg.RadarRingEnabled) return;

            radar.Add(BuildSliderRow("Ring radius (px)",
                "Radius of the radar halo around the center of your screen.",
                50f, 400f, "0",
                () => cfg.RadarRadius,
                v => cfg.RadarRadius = v,
                v => { cfg.RadarRadius = v; ModConfig.SaveConfig(); }));

            radar.Add(BuildSliderRow("Radar opacity",
                "Overall transparency of the radar ring and its dots.",
                0.1f, 1.0f, "0.00",
                () => cfg.RadarOpacity,
                v => cfg.RadarOpacity = v,
                v => { cfg.RadarOpacity = v; ModConfig.SaveConfig(); }));

            radar.Add(BuildSliderRow("Proximity scale range (m)",
                "World distance over which radar dots scale from large (close to you) to small (far away).",
                10f, 90f, "0",
                () => cfg.RadarMaxDistance,
                v => cfg.RadarMaxDistance = v,
                v => { cfg.RadarMaxDistance = v; ModConfig.SaveConfig(); }));
        }

        // ── search filter ─────────────────────────────────────────────

        private List<KeyValuePair<UiTab, UITK.ScrollView>> Bodies()
        {
            return new List<KeyValuePair<UiTab, UITK.ScrollView>>
            {
                new KeyValuePair<UiTab, UITK.ScrollView>(UiTab.Layout, _layoutBody),
                new KeyValuePair<UiTab, UITK.ScrollView>(UiTab.Fade,   _fadeBody),
                new KeyValuePair<UiTab, UITK.ScrollView>(UiTab.Icons,  _iconsBody),
                new KeyValuePair<UiTab, UITK.ScrollView>(UiTab.Radar,  _radarBody),
            };
        }

        private static string CardTextOf(UITK.VisualElement row)
        {
            for (var p = row?.parent; p != null; p = p.parent)
                if (p.ClassListContains(SectionClass)) return p.userData as string ?? "";
            return "";
        }

        private static void MarkSearchable(UITK.VisualElement row, string label, string description)
        {
            if (row == null) return;
            string hay = (label ?? "") + " " + (description ?? "");
            row.userData = hay.ToLowerInvariant();
            row.AddToClassList(RowClass);
        }

        private void ApplySearchFilter()
        {
            if (_panel == null) return;

            string q = (_searchQuery ?? "").Trim().ToLowerInvariant();
            bool searching = q.Length > 0;
            int here = 0, elsewhere = 0;

            foreach (var pair in Bodies())
            {
                var body = pair.Value;
                if (body == null) continue;
                int hits = 0;

                foreach (var row in body.Query(className: RowClass).ToList())
                {
                    string hay = row.userData as string ?? "";
                    bool match = !searching || hay.Contains(q) || CardTextOf(row).Contains(q);
                    row.style.display = match ? UITK.DisplayStyle.Flex : UITK.DisplayStyle.None;
                    if (searching && match) hits++;
                }

                foreach (var card in body.Query(className: SectionClass).ToList())
                {
                    bool any = searching && (card.userData as string ?? "").Contains(q);
                    foreach (var row in card.Query(className: RowClass).ToList())
                        if (row.style.display.value == UITK.DisplayStyle.Flex) { any = true; break; }
                    card.style.display = (!searching || any) ? UITK.DisplayStyle.Flex : UITK.DisplayStyle.None;
                }

                if (!searching) continue;
                if (pair.Key == _activeTab) here = hits; else elsewhere += hits;
            }

            if (_searchHint == null) return;
            _searchHint.text = !searching ? ""
                             : elsewhere > 0 ? $"{here} here \u00b7 {elsewhere} on other tabs"
                             : here > 0 ? $"{here} here"
                             : "no matches";
            _searchHint.style.color = searching && here == 0 && elsewhere == 0 ? Danger : TextDim;
        }

        // ── ported OWPUI / PonceArenaTweaks widgets ───────────────────

        private static UITK.Label Title(string text, Color c)
        {
            var l = new UITK.Label(text);
            l.style.fontSize = 38;
            l.style.color = c;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.letterSpacing = 2;
            ForceUIFont(l);
            return l;
        }

        private static UITK.ScrollView MakeBody()
        {
            var sv = new UITK.ScrollView(UITK.ScrollViewMode.Vertical);
            sv.style.flexGrow = 1;
            sv.style.display = UITK.DisplayStyle.None;
            return sv;
        }

        private static UITK.VisualElement AddSection(UITK.VisualElement parent, string label, string subtitle = null)
        {
            var card = new UITK.VisualElement();
            var s = card.style;
            s.flexDirection = UITK.FlexDirection.Column;
            s.marginBottom = 14;
            s.paddingLeft = 12; s.paddingRight = 12;
            s.paddingTop = 12; s.paddingBottom = 4;
            s.backgroundColor = SectionBg;
            s.borderTopLeftRadius = SECTION_RADIUS; s.borderTopRightRadius = SECTION_RADIUS;
            s.borderBottomLeftRadius = SECTION_RADIUS; s.borderBottomRightRadius = SECTION_RADIUS;
            SetBorderWidth(card, 1f); SetBorderColor(card, SectionBorder);
            card.AddToClassList(SectionClass);
            card.userData = ((label ?? "") + " " + (subtitle ?? "")).ToLowerInvariant();
            parent.Add(card);

            var wrap = new UITK.VisualElement();
            wrap.style.flexDirection = UITK.FlexDirection.Column;
            wrap.style.marginBottom = 12;

            var row = new UITK.VisualElement();
            row.style.flexDirection = UITK.FlexDirection.Row;
            row.style.alignItems = UITK.Align.Center;
            row.style.marginBottom = 4;

            var bar = new UITK.VisualElement();
            bar.style.width = 4; bar.style.height = 18;
            bar.style.backgroundColor = Accent;
            bar.style.marginRight = 10;
            bar.style.borderTopLeftRadius = 2; bar.style.borderBottomLeftRadius = 2;
            bar.style.borderTopRightRadius = 2; bar.style.borderBottomRightRadius = 2;
            row.Add(bar);

            var head = new UITK.Label(label.ToUpperInvariant());
            head.style.color = TextPrimary;
            head.style.fontSize = 16;
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.style.letterSpacing = 3;
            ForceUIFont(head);
            row.Add(head);
            wrap.Add(row);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = new UITK.Label(subtitle);
                sub.style.color = TextMuted; sub.style.fontSize = 12; sub.style.marginLeft = 14;
                sub.style.whiteSpace = UITK.WhiteSpace.Normal;
                ForceUIFont(sub);
                wrap.Add(sub);
            }

            card.Add(wrap);
            return card;
        }

        private static UITK.VisualElement BuildToggleRow(string label, string description,
            Func<bool> getter, Action<bool> setter)
        {
            var row = NewRow();

            var textCol = new UITK.VisualElement();
            textCol.style.flexDirection = UITK.FlexDirection.Column;
            textCol.style.flexGrow = 1; textCol.style.flexShrink = 1; textCol.style.minWidth = 0;
            textCol.style.marginRight = 12;

            var title = new UITK.Label(label);
            title.style.fontSize = 14; title.style.color = TextPrimary;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1;
            ForceUIFont(title);
            textCol.Add(title);

            if (!string.IsNullOrEmpty(description))
            {
                var desc = new UITK.Label(description);
                desc.style.fontSize = 11; desc.style.color = TextMuted;
                desc.style.marginTop = 2;
                desc.style.whiteSpace = UITK.WhiteSpace.Normal;
                ForceUIFont(desc);
                textCol.Add(desc);
            }
            row.Add(textCol);

            var pill = new UITK.Button();
            pill.style.height = 32; pill.style.minWidth = 72; pill.style.maxWidth = 72;
            pill.style.paddingLeft = 0; pill.style.paddingRight = 0;
            pill.style.paddingTop = 0; pill.style.paddingBottom = 0;
            pill.style.marginLeft = 8;
            pill.style.borderTopLeftRadius = 16; pill.style.borderTopRightRadius = 16;
            pill.style.borderBottomLeftRadius = 16; pill.style.borderBottomRightRadius = 16;
            SetBorderWidth(pill, 1f);
            pill.style.unityFontStyleAndWeight = FontStyle.Bold;
            pill.style.fontSize = 12;
            pill.style.letterSpacing = 2;
            pill.style.unityTextAlign = new UITK.StyleEnum<TextAnchor>(TextAnchor.MiddleCenter);
            pill.style.whiteSpace = UITK.WhiteSpace.NoWrap;
            pill.focusable = false;
            ForceUIFont(pill);
            row.Add(pill);
            MarkSearchable(row, label, description);

            bool hovered = false;
            void Refresh()
            {
                bool v = getter();
                pill.text = v ? "ON" : "OFF";
                Color bg = v ? AccentDim : ButtonBg;
                pill.style.backgroundColor = hovered ? Color.Lerp(bg, Accent, 0.35f) : bg;
                pill.style.color = v || hovered ? TextPrimary : TextMuted;
                SetBorderColor(pill, v || hovered ? Accent : PanelBorder);
            }
            Refresh();

            pill.RegisterCallback<UITK.PointerEnterEvent>(_ => { hovered = true; Refresh(); });
            pill.RegisterCallback<UITK.PointerLeaveEvent>(_ => { hovered = false; Refresh(); });
            pill.clicked += () => { setter(!getter()); Refresh(); };

            return row;
        }

        private static UITK.VisualElement BuildSliderRow(string label, string description,
            float min, float max, string valueFormat,
            Func<float> getter, Action<float> liveSetter, Action<float> commitSetter)
        {
            var row = NewRow();

            var textCol = new UITK.VisualElement();
            textCol.style.flexDirection = UITK.FlexDirection.Column;
            textCol.style.flexGrow = 1; textCol.style.flexShrink = 1; textCol.style.minWidth = 0;
            textCol.style.marginRight = 12;

            var title = new UITK.Label(label);
            title.style.fontSize = 14; title.style.color = TextPrimary;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1;
            ForceUIFont(title);
            textCol.Add(title);

            if (!string.IsNullOrEmpty(description))
            {
                var desc = new UITK.Label(description);
                desc.style.fontSize = 11; desc.style.color = TextMuted;
                desc.style.marginTop = 2;
                desc.style.whiteSpace = UITK.WhiteSpace.Normal;
                ForceUIFont(desc);
                textCol.Add(desc);
            }
            row.Add(textCol);

            var sliderCol = new UITK.VisualElement();
            sliderCol.style.flexDirection = UITK.FlexDirection.Column;
            sliderCol.style.alignItems = UITK.Align.FlexEnd;
            sliderCol.style.flexShrink = 0;

            var track = new UITK.VisualElement();
            track.style.width = 160; track.style.height = 6;
            track.style.backgroundColor = TextFieldBg;
            track.style.borderTopLeftRadius = 3; track.style.borderTopRightRadius = 3;
            track.style.borderBottomLeftRadius = 3; track.style.borderBottomRightRadius = 3;
            track.style.flexShrink = 0;

            var fill = new UITK.VisualElement();
            fill.style.position = UITK.Position.Absolute;
            fill.style.left = 0; fill.style.top = 0; fill.style.bottom = 0;
            fill.style.backgroundColor = AccentDim;
            fill.style.borderTopLeftRadius = 3; fill.style.borderTopRightRadius = 3;
            fill.style.borderBottomLeftRadius = 3; fill.style.borderBottomRightRadius = 3;
            track.Add(fill);

            var thumb = new UITK.VisualElement();
            thumb.style.position = UITK.Position.Absolute;
            thumb.style.width = 14; thumb.style.height = 14;
            thumb.style.top = -4;
            thumb.style.backgroundColor = Accent;
            thumb.style.borderTopLeftRadius = 7; thumb.style.borderTopRightRadius = 7;
            thumb.style.borderBottomLeftRadius = 7; thumb.style.borderBottomRightRadius = 7;
            track.Add(thumb);

            sliderCol.Add(track);

            var valueLabel = new UITK.Label();
            valueLabel.style.fontSize = 11; valueLabel.style.color = Accent;
            valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            valueLabel.style.letterSpacing = 1;
            valueLabel.style.marginTop = 4;
            ForceUIFont(valueLabel);
            sliderCol.Add(valueLabel);

            row.Add(sliderCol);

            const float TRACK_W = 160f, THUMB_W = 14f;
            float range = Mathf.Max(0.0001f, max - min);

            void Render(float v)
            {
                v = Mathf.Clamp(v, min, max);
                float t = (v - min) / range;
                float fillPx = t * TRACK_W;
                fill.style.width = fillPx;
                thumb.style.left = Mathf.Clamp(fillPx - THUMB_W * 0.5f, 0f, TRACK_W - THUMB_W);
                valueLabel.text = v.ToString(valueFormat, CultureInfo.InvariantCulture);
            }
            Render(getter());

            float ValueFromLocalX(float x) => min + Mathf.Clamp01(x / TRACK_W) * range;

            bool dragging = false;
            track.RegisterCallback<UITK.PointerDownEvent>(e =>
            {
                dragging = true;
                track.CapturePointer(e.pointerId);
                int pid = e.pointerId;
                _cancelSliderDrag = () =>
                {
                    if (!dragging) return;
                    dragging = false;
                    commitSetter(getter());
                    if (track.HasPointerCapture(pid)) track.ReleasePointer(pid);
                };

                float v = ValueFromLocalX(e.localPosition.x);
                liveSetter(v); Render(v);
                e.StopPropagation();
            });
            track.RegisterCallback<UITK.PointerMoveEvent>(e =>
            {
                if (!dragging) return;
                float v = ValueFromLocalX(e.localPosition.x);
                liveSetter(v); Render(v);
            });
            track.RegisterCallback<UITK.PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                track.ReleasePointer(e.pointerId);
                float v = ValueFromLocalX(e.localPosition.x);
                commitSetter(v); Render(v);
                _cancelSliderDrag = null;
            });
            track.RegisterCallback<UITK.PointerCaptureOutEvent>(_ =>
            {
                if (!dragging) return;
                dragging = false;
                commitSetter(getter());
                _cancelSliderDrag = null;
            });

            MarkSearchable(row, label, description);
            return row;
        }

        private static UITK.VisualElement BuildChoiceRow(string label, string description,
            List<string> options, Func<string> getter, Action<string> setter)
        {
            var row = NewRow();

            var textCol = new UITK.VisualElement();
            textCol.style.flexDirection = UITK.FlexDirection.Column;
            textCol.style.flexGrow = 1; textCol.style.flexShrink = 1; textCol.style.minWidth = 0;
            textCol.style.marginRight = 12;

            var title = new UITK.Label(label);
            title.style.fontSize = 14; title.style.color = TextPrimary;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1;
            ForceUIFont(title);
            textCol.Add(title);

            if (!string.IsNullOrEmpty(description))
            {
                var desc = new UITK.Label(description);
                desc.style.fontSize = 11; desc.style.color = TextMuted;
                desc.style.marginTop = 2; desc.style.whiteSpace = UITK.WhiteSpace.Normal;
                ForceUIFont(desc);
                textCol.Add(desc);
            }
            row.Add(textCol);

            var picker = new UITK.VisualElement();
            picker.style.flexDirection = UITK.FlexDirection.Row;
            picker.style.alignItems = UITK.Align.Center;
            picker.style.flexShrink = 0;

            var value = new UITK.Label();
            value.style.minWidth = 100;
            value.style.fontSize = 12; value.style.color = Accent;
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            value.style.letterSpacing = 1;
            value.style.unityTextAlign = new UITK.StyleEnum<TextAnchor>(TextAnchor.MiddleCenter);
            ForceUIFont(value);

            void Refresh() => value.text = (getter() ?? "-").ToUpperInvariant();

            void Step(int dir)
            {
                if (options == null || options.Count == 0) return;
                string current = getter();
                int i = options.FindIndex(o => string.Equals(o, current, StringComparison.OrdinalIgnoreCase));
                if (i < 0) { setter(options[0]); Refresh(); return; }
                i = (i + dir + options.Count) % options.Count;
                setter(options[i]);
                Refresh();
            }

            picker.Add(StepButton("<", () => Step(-1)));
            picker.Add(value);
            picker.Add(StepButton(">", () => Step(+1)));
            Refresh();

            row.Add(picker);
            MarkSearchable(row, label, description);
            return row;
        }

        private static UITK.VisualElement BuildActionRow(string label, string description,
            string buttonText, Action onClick)
        {
            var row = NewRow();

            var textCol = new UITK.VisualElement();
            textCol.style.flexDirection = UITK.FlexDirection.Column;
            textCol.style.flexGrow = 1; textCol.style.flexShrink = 1; textCol.style.minWidth = 0;
            textCol.style.marginRight = 12;

            var title = new UITK.Label(label);
            title.style.fontSize = 14; title.style.color = TextPrimary;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1;
            ForceUIFont(title);
            textCol.Add(title);

            if (!string.IsNullOrEmpty(description))
            {
                var desc = new UITK.Label(description);
                desc.style.fontSize = 11; desc.style.color = TextMuted;
                desc.style.marginTop = 2; desc.style.whiteSpace = UITK.WhiteSpace.Normal;
                ForceUIFont(desc);
                textCol.Add(desc);
            }
            row.Add(textCol);

            var b = new UITK.Button(onClick) { text = buttonText.ToUpperInvariant() };
            b.style.height = 32; b.style.minWidth = 96;
            b.style.marginLeft = 8;
            b.style.fontSize = 12;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.letterSpacing = 2;
            b.style.backgroundColor = ButtonBg;
            b.style.color = TextPrimary;
            b.style.unityTextAlign = new UITK.StyleEnum<TextAnchor>(TextAnchor.MiddleCenter);
            b.style.borderTopLeftRadius = 16; b.style.borderTopRightRadius = 16;
            b.style.borderBottomLeftRadius = 16; b.style.borderBottomRightRadius = 16;
            SetBorderWidth(b, 1f); SetBorderColor(b, PanelBorder);
            b.focusable = false;
            ForceUIFont(b);
            AddButtonFlash(b, ButtonBg, TextPrimary);
            row.Add(b);

            MarkSearchable(row, label, description);
            return row;
        }

        private static UITK.Button StepButton(string glyph, Action onClick)
        {
            var b = new UITK.Button(onClick) { text = glyph };
            b.style.width = 32; b.style.height = 32;
            b.style.paddingLeft = 0; b.style.paddingRight = 0;
            b.style.marginLeft = 4; b.style.marginRight = 4;
            b.style.fontSize = 14;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.backgroundColor = ButtonBg;
            b.style.color = TextPrimary;
            b.style.unityTextAlign = new UITK.StyleEnum<TextAnchor>(TextAnchor.MiddleCenter);
            b.style.borderTopLeftRadius = BTN_RADIUS; b.style.borderTopRightRadius = BTN_RADIUS;
            b.style.borderBottomLeftRadius = BTN_RADIUS; b.style.borderBottomRightRadius = BTN_RADIUS;
            SetBorderWidth(b, 1f); SetBorderColor(b, PanelBorder);
            b.focusable = false;
            ForceUIFont(b);
            AddButtonFlash(b, ButtonBg, TextPrimary);
            return b;
        }

        private static UITK.Button MakeTab(string text, Action onClick, Func<bool> isActive)
        {
            var b = new UITK.Button(onClick) { text = text.ToUpperInvariant() };
            b.style.unityTextAlign = new UITK.StyleEnum<TextAnchor>(TextAnchor.MiddleCenter);
            b.style.height = 44;
            b.style.flexGrow = 1;
            b.style.flexShrink = 1;
            b.style.minWidth = 0;
            b.style.paddingLeft = 8; b.style.paddingRight = 8;
            b.style.marginRight = 4; b.style.marginBottom = 14;
            b.style.fontSize = 14;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.letterSpacing = 1;
            b.style.whiteSpace = UITK.WhiteSpace.NoWrap;
            b.style.borderTopLeftRadius = 8; b.style.borderTopRightRadius = 8;
            b.style.borderBottomLeftRadius = 0; b.style.borderBottomRightRadius = 0;
            b.style.borderTopWidth = 0; b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
            b.style.borderBottomWidth = 0;
            b.style.borderBottomColor = Accent;
            b.style.backgroundColor = TabInactiveBg;
            b.style.color = TextMuted;
            b.focusable = false;
            ForceUIFont(b);

            Action apply = () => SetTabVisual(b, isActive());
            apply();
            b.RegisterCallback<UITK.PointerEnterEvent>(_ =>
            {
                if (isActive()) return;
                b.style.backgroundColor = RowBgHover;
                b.style.color = TextPrimary;
            });
            b.RegisterCallback<UITK.PointerLeaveEvent>(_ => apply());
            return b;
        }

        private static void SetTabVisual(UITK.Button b, bool active)
        {
            if (b == null) return;
            b.style.backgroundColor = active ? TabActiveBg : TabInactiveBg;
            b.style.color = active ? TextPrimary : TextMuted;
            b.style.borderBottomWidth = active ? 3 : 0;
            b.style.borderBottomColor = Accent;
        }

        private static UITK.Button MakeFooterButton(string text, Action onClick)
        {
            var b = new UITK.Button(onClick) { text = text.ToUpperInvariant() };
            b.style.height = 38;
            b.style.minWidth = 124;
            b.style.marginLeft = 8;
            b.style.fontSize = 13;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.letterSpacing = 1;
            b.style.backgroundColor = ButtonBg;
            b.style.color = TextPrimary;
            b.style.unityTextAlign = new UITK.StyleEnum<TextAnchor>(TextAnchor.MiddleCenter);
            b.style.borderTopLeftRadius = BTN_RADIUS; b.style.borderTopRightRadius = BTN_RADIUS;
            b.style.borderBottomLeftRadius = BTN_RADIUS; b.style.borderBottomRightRadius = BTN_RADIUS;
            SetBorderWidth(b, 1f); SetBorderColor(b, PanelBorder);
            b.focusable = false;
            ForceUIFont(b);
            AddButtonFlash(b, ButtonBg, TextPrimary);
            return b;
        }

        private static void AddButtonFlash(UITK.Button b, Color baseBg, Color baseText, int flashMs = 120)
        {
            Color hoverBg = Color.Lerp(baseBg, Accent, 0.35f);
            Color flashBg = Color.Lerp(baseBg, Accent, 0.60f);
            Action setBase = () => { b.style.backgroundColor = baseBg; b.style.color = baseText; };
            setBase();

            bool hover = false, flashing = false;
            b.RegisterCallback<UITK.PointerEnterEvent>(_ =>
            {
                hover = true;
                b.style.backgroundColor = hoverBg;
                b.style.color = TextPrimary;
            });
            b.RegisterCallback<UITK.PointerLeaveEvent>(_ =>
            {
                hover = false;
                if (!flashing) setBase();
            });
            b.RegisterCallback<UITK.PointerUpEvent>(_ =>
            {
                flashing = true;
                b.style.backgroundColor = flashBg;
                b.style.color = TextPrimary;
                b.schedule.Execute(() =>
                {
                    flashing = false;
                    if (!hover) setBase();
                }).StartingIn(flashMs);
            });
        }

        private static void StyleSearchField(UITK.TextField tf)
        {
            if (tf == null) return;
            tf.style.backgroundColor = TextFieldBg;
            tf.style.color = TextPrimary;
            tf.style.justifyContent = UITK.Justify.Center;
            tf.style.borderTopLeftRadius = BTN_RADIUS; tf.style.borderTopRightRadius = BTN_RADIUS;
            tf.style.borderBottomLeftRadius = BTN_RADIUS; tf.style.borderBottomRightRadius = BTN_RADIUS;
            SetBorderWidth(tf, 1f); SetBorderColor(tf, PanelBorder);
            ForceUIFont(tf);

            var input = tf.Q(className: "unity-base-text-field__input");
            if (input != null)
            {
                var ins = input.style;
                ins.backgroundColor = TextFieldBg;
                ins.paddingLeft = 8; ins.paddingRight = 8;
                ins.paddingTop = 0; ins.paddingBottom = 0;
                ins.justifyContent = UITK.Justify.Center;
                ins.overflow = UITK.Overflow.Hidden;
                SetBorderWidth(input, 0f);
                ForceUIFont(input);
            }

            var inner = tf.Q<UITK.TextElement>();
            if (inner != null)
            {
                var innerStyle = inner.style;
                innerStyle.color = TextPrimary;
                innerStyle.backgroundColor = Color.clear;
                innerStyle.fontSize = 13;
                innerStyle.unityTextAlign = new UITK.StyleEnum<TextAnchor>(TextAnchor.MiddleLeft);
                innerStyle.whiteSpace = UITK.WhiteSpace.NoWrap;
                innerStyle.flexGrow = 1;
                ForceUIFont(inner);
            }
        }

        private static UITK.VisualElement NewRow()
        {
            var row = new UITK.VisualElement();
            row.style.flexDirection = UITK.FlexDirection.Row;
            row.style.alignItems = UITK.Align.Center;
            row.style.marginBottom = 8;
            row.style.backgroundColor = RowBg;
            row.style.paddingLeft = 14; row.style.paddingRight = 10;
            row.style.paddingTop = 10; row.style.paddingBottom = 10;
            row.style.borderTopLeftRadius = ROW_RADIUS; row.style.borderTopRightRadius = ROW_RADIUS;
            row.style.borderBottomLeftRadius = ROW_RADIUS; row.style.borderBottomRightRadius = ROW_RADIUS;
            return row;
        }

        private static void SetBorderColor(UITK.VisualElement e, Color c)
        {
            e.style.borderTopColor = c; e.style.borderBottomColor = c;
            e.style.borderLeftColor = c; e.style.borderRightColor = c;
        }

        private static void SetBorderWidth(UITK.VisualElement e, float w)
        {
            e.style.borderTopWidth = w; e.style.borderBottomWidth = w;
            e.style.borderLeftWidth = w; e.style.borderRightWidth = w;
        }
    }
}