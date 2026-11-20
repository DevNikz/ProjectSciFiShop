using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// The computer desktop: one icon per app (OrderScreen, FurnitureScreen...). An icon opens that app as a
/// window, its close button returns here, and Esc closes the window first, then the desktop.
/// BuiltIn makes the canvas, icon grid, taskbar and window frame in code. Custom uses the scene objects you assign.
/// A UI Style asset (BuiltIn) replaces the window settings here. Needs an EventSystem in the scene.
/// </summary>
public class DesktopScreen : MonoBehaviour, IScreen
{
    #region Inspector visibility rules

    // Shorthands for VisibleWhen. Stack them on a field: all must hold.
    sealed class BuiltInOnlyAttribute : VisibleWhenAttribute
    {
        public BuiltInOnlyAttribute()
            : base(
                nameof(DesktopScreen.layoutMode),
                VisibleCondition.EnumEquals,
                (int)UILayoutMode.BuiltIn
            ) { }
    }

    sealed class CustomOnlyAttribute : VisibleWhenAttribute
    {
        public CustomOnlyAttribute()
            : base(
                nameof(DesktopScreen.layoutMode),
                VisibleCondition.EnumEquals,
                (int)UILayoutMode.Custom
            ) { }
    }

    sealed class WithoutStyleAttribute : VisibleWhenAttribute
    {
        public WithoutStyleAttribute()
            : base(nameof(DesktopScreen.uiStyle), VisibleCondition.Null) { }
    }

    #endregion

    #region Types and constants

    enum LabelPlacement
    {
        Below,
        Right,
        Above,
    }

    [Serializable]
    public struct Entry
    {
        [Tooltip("Shown under the icon. Empty = the screen's object name.")]
        public string label;

        public Sprite icon;

        [Tooltip(
            "A component that implements IDesktopApp (OrderScreen, FurnitureScreen). Drag the component, not just the object."
        )]
        public GameObject screen;
    }

    static readonly Color MissingIconColor = new(0.25f, 0.3f, 0.4f, 1f);
    const string GlyphName = "Glyph";
    const int CanvasSortOrder = 100;
    const int MinutesPerDay = 1440;
    const float GridSpacing = 24f;
    const int GridPadding = 48;
    const float ClockInset = 16f;

    #endregion

    #region Serialized settings

    // UI Style: the shared asset, and the window settings it replaces.
    [Tooltip(
        "Optional. A UI Style asset: its window look replaces the window settings below (they hide while it is assigned), and the order and furniture screens can use the same asset."
    )]
    [SerializeField]
    [FormerlySerializedAs("style")]
    [BuiltInOnly(Header = "UI Style")]
    UIStyle uiStyle;

    [Tooltip("Gap between the frame and the screen edge, in pixels.")]
    [SerializeField]
    [BuiltInOnly]
    [WithoutStyle(
        Note = "A UI Style is assigned. It replaces the window settings, so they are hidden."
    )]
    float windowMargin = 80f;

    [SerializeField]
    [FormerlySerializedAs("titleBarHeight")]
    [BuiltInOnly]
    [WithoutStyle]
    float windowTitleBarHeight = 44f;

    [SerializeField]
    [FormerlySerializedAs("titleBarColor")]
    [BuiltInOnly]
    [WithoutStyle]
    Color windowTitleBarColor = new(0.18f, 0.22f, 0.32f, 1f);

    [SerializeField]
    [FormerlySerializedAs("titleStyle")]
    [BuiltInOnly]
    [WithoutStyle]
    UITextStyle windowTitleStyle = new()
    {
        size = 22f,
        alignment = TextAlignmentOptions.MidlineLeft,
    };

    [SerializeField]
    [BuiltInOnly]
    [WithoutStyle]
    Color windowFillColor = new(0.12f, 0.14f, 0.2f, 1f);

    [SerializeField]
    [BuiltInOnly]
    [WithoutStyle]
    Color windowBorderColor = new(0.35f, 0.4f, 0.5f, 1f);

    [SerializeField]
    [BuiltInOnly]
    [WithoutStyle]
    float windowBorderThickness = 3f;

    [Header("Layout")]
    [Tooltip(
        "BuiltIn: the canvas, icon grid and window layer are made in code. Custom: use the objects below."
    )]
    [SerializeField]
    [FormerlySerializedAs("mode")]
    UILayoutMode layoutMode = UILayoutMode.BuiltIn;

    // Custom layout
    [Tooltip("Shown while open. Must be a child: this object has to stay active.")]
    [SerializeField]
    [CustomOnly(Header = "Custom layout")]
    GameObject panel;

    [Tooltip("Icons are created here (a grid or layout group works well).")]
    [SerializeField]
    [CustomOnly]
    Transform iconParent;

    [Tooltip("A Button. Its target graphic gets the icon sprite, a TMP_Text child gets the label.")]
    [SerializeField]
    [CustomOnly]
    Button iconPrefab;

    [Tooltip("App windows are placed here. Keep it above the icons so windows draw on top.")]
    [SerializeField]
    [CustomOnly]
    Transform windowLayer;

    // Built-in look
    [SerializeField]
    [BuiltInOnly(Header = "Built-in look")]
    Color backgroundColor = new(0.08f, 0.1f, 0.14f, 1f);

    [SerializeField]
    [BuiltInOnly]
    Vector2 cellSize = new(160f, 180f);

    [SerializeField]
    [BuiltInOnly]
    int columns = 4;

    // Built-in icon labels
    [SerializeField]
    [BuiltInOnly(Header = "Built-in icon labels")]
    UITextStyle labelStyle = new();

    [Tooltip("Where the label sits next to the icon. Use a left alignment in the style for Right.")]
    [SerializeField]
    [BuiltInOnly]
    LabelPlacement labelPlacement = LabelPlacement.Below;

    [Tooltip("Icon image size in pixels.")]
    [SerializeField]
    [BuiltInOnly]
    float iconSize = 96f;

    [Tooltip("Space between the icon and its label, in pixels.")]
    [SerializeField]
    [BuiltInOnly]
    float labelGap = 8f;

    [Tooltip("Extra label shift in pixels, added after placement.")]
    [SerializeField]
    [BuiltInOnly]
    Vector2 labelOffset = Vector2.zero;

    [Tooltip("Label height for Below and Above.")]
    [SerializeField]
    [BuiltInOnly]
    float labelHeight = 40f;

    [Tooltip("Icon background while hovered.")]
    [SerializeField]
    [BuiltInOnly]
    Color iconHoverColor = new(1f, 1f, 1f, 0.15f);

    [Tooltip("Icon background while pressed.")]
    [SerializeField]
    [BuiltInOnly]
    Color iconPressedColor = new(1f, 1f, 1f, 0.3f);

    // Built-in desktop chrome
    [SerializeField]
    [BuiltInOnly(Header = "Built-in desktop chrome")]
    Color panelBorderColor = new(0.35f, 0.4f, 0.5f, 1f);

    [SerializeField]
    [BuiltInOnly]
    float panelBorderThickness = 4f;

    [Tooltip("Optional. Drawn over the background color.")]
    [SerializeField]
    [BuiltInOnly]
    Sprite wallpaper;

    [SerializeField]
    [BuiltInOnly]
    Color wallpaperTint = Color.white;

    // Built-in taskbar
    [SerializeField]
    [BuiltInOnly(Header = "Built-in taskbar")]
    bool showTaskbar = true;

    [SerializeField]
    [BuiltInOnly]
    float taskbarHeight = 56f;

    [SerializeField]
    [BuiltInOnly]
    Color taskbarColor = new(0.05f, 0.06f, 0.09f, 1f);

    [SerializeField]
    [BuiltInOnly]
    Color taskbarBorderColor = new(0.3f, 0.35f, 0.45f, 1f);

    [SerializeField]
    [BuiltInOnly]
    float taskbarBorderThickness = 2f;

    [SerializeField]
    [BuiltInOnly]
    UITextStyle clockStyle = new() { size = 22f, alignment = TextAlignmentOptions.MidlineRight };

    [Tooltip("{0} = day, {1} = hour, {2} = minute. Example: Day {0}  {1:00}:{2:00}")]
    [SerializeField]
    [BuiltInOnly]
    string clockFormat = "Day {0}  {1:00}:{2:00}";

    // Apps
    [Header("Apps")]
    [Tooltip("One icon per entry, in this order.")]
    [SerializeField]
    Entry[] entries = Array.Empty<Entry>();

    [Tooltip("Optional. Opens the desktop when interacted with (the terminal).")]
    [SerializeField]
    Interactable opener;

    #endregion

    #region Runtime state

    readonly List<IDesktopApp> _apps = new();
    GameObject _panel;
    Transform _iconParent;
    GameObject _frame; // BuiltIn: the one shared window frame
    Transform _frameContent;
    TMP_Text _frameTitle;
    TMP_Text _clockText;
    IDesktopApp _current;
    int _lastClockKey = -1;
    bool _open;
    bool _built;
    bool _clockWarned;

    public ScreenId Id => ScreenId.Desktop;
    public bool IsOpen => _open;

    #endregion

    #region Lifecycle

    void OnEnable()
    {
        if (!TryResolveLayout())
        {
            enabled = false;
            return;
        }
        if (opener != null)
            opener.OnInteractEvent += HandleOpener;
        var router = UIScreenRouter.Instance; // null only while quitting
        if (router != null)
            router.Register(this);
        _panel.SetActive(false);
    }

    void OnDisable()
    {
        Close(); // never leave the cursor free or input blocked
        if (opener != null)
            opener.OnInteractEvent -= HandleOpener;
        var router = UIScreenRouter.Existing; // never create one while shutting down
        if (router != null)
            router.Unregister(this);
    }

    void OnDestroy()
    {
        foreach (var app in _apps)
            app.WindowClosed -= HandleWindowClosed;
        _apps.Clear();
    }

    void Update()
    {
        if (!_open)
            return;
        RefreshClock();
        var input = InputManager.Instance;
        if (input == null || !input.UICancelPressed)
            return;
        if (_current != null)
            CloseCurrentWindow(); // first Esc: back to the desktop
        else
            Close(); // second Esc: leave the desktop
    }

    #endregion

    #region Open and close

    /// <summary>Open the desktop. Does nothing if already open.</summary>
    public void Open()
    {
        if (_open || !isActiveAndEnabled)
            return;
        var input = InputManager.Instance;
        if (input == null)
            return;

        BuildIcons();
        input.EnterUIMode(this);
        _open = true;
        _lastClockKey = -1; // redraw the clock on the first frame
        _panel.SetActive(true);
    }

    public void Close()
    {
        if (!_open)
            return;
        _open = false;

        CloseCurrentWindow();
        if (_panel != null) // gone on scene unload
            _panel.SetActive(false);
        var input = InputManager.Instance; // null only while quitting
        if (input != null)
            input.ExitUIMode(this);
        var router = UIScreenRouter.Existing;
        if (router != null)
            router.NotifyClosed(this);
    }

    void HandleOpener(Interactor _) => Open();

    #endregion

    #region App windows

    void HandleIconClick(IDesktopApp app, string title)
    {
        if (!_open || _current == app)
            return;
        CloseCurrentWindow(); // one window at a time
        ShowWindowFrame(title); // before ShowWindow so the window is visible inside it
        app.ShowWindow();
        _current = app.IsWindowOpen ? app : null;
        if (_current == null)
            HideWindowFrame();
    }

    void HandleWindowClosed()
    {
        _current = null;
        HideWindowFrame();
    }

    void CloseCurrentWindow()
    {
        var app = _current;
        _current = null;
        if (app != null)
            app.HideWindow();
        HideWindowFrame();
    }

    void ShowWindowFrame(string title)
    {
        if (_frame == null) // Custom mode
            return;
        _frameTitle.text = title;
        _frame.SetActive(true);
    }

    void HideWindowFrame()
    {
        if (_frame != null)
            _frame.SetActive(false);
    }

    #endregion

    #region Icons

    // One icon per entry, built the first time the desktop opens.
    void BuildIcons()
    {
        if (_built)
            return;
        foreach (var entry in entries)
        {
            var app = entry.screen != null ? entry.screen.GetComponent<IDesktopApp>() : null;
            if (app == null)
            {
                Debug.LogWarning(
                    $"{name}: entry '{entry.label}' has no screen with an IDesktopApp, not listed.",
                    this
                );
                continue;
            }
            if (layoutMode == UILayoutMode.BuiltIn && app.WindowRoot != null)
                app.WindowRoot.SetParent(_frameContent, false);

            string title = string.IsNullOrEmpty(entry.label) ? entry.screen.name : entry.label;
            var button = CreateIconButton();
            BindIcon(button, entry.icon, title);
            button.onClick.AddListener(() => HandleIconClick(app, title));
            app.WindowClosed += HandleWindowClosed;
            _apps.Add(app);
        }
        _built = true;
    }

    void BindIcon(Button button, Sprite icon, string title)
    {
        // BuiltIn: the sprite goes on the Glyph child (the button root is the hover background).
        Image image =
            layoutMode == UILayoutMode.BuiltIn
                ? button.transform.Find(GlyphName).GetComponent<Image>()
                : button.targetGraphic as Image;
        if (image != null)
        {
            image.sprite = icon;
            image.color = icon != null ? Color.white : MissingIconColor;
        }
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = title;
    }

    // Custom: the designer's prefab. BuiltIn: a hover background with a Glyph image and a Label placed per the settings.
    Button CreateIconButton()
    {
        if (layoutMode == UILayoutMode.Custom)
            return Instantiate(iconPrefab, _iconParent);

        var iconObject = new GameObject(
            "Icon",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );
        iconObject.transform.SetParent(_iconParent, false);
        var button = iconObject.GetComponent<Button>();
        button.targetGraphic = iconObject.GetComponent<Image>();
        ApplyHoverColors(button);

        var glyphObject = new GameObject(GlyphName, typeof(RectTransform), typeof(Image));
        glyphObject.transform.SetParent(iconObject.transform, false);
        var glyph = glyphObject.GetComponent<Image>();
        glyph.preserveAspect = true;
        glyph.raycastTarget = false;

        var label = UIBuilder.CreateText(iconObject.transform, "Label", labelStyle);
        PlaceIconParts(glyph.rectTransform, label.rectTransform);
        return button;
    }

    // Transparent until hovered or pressed. The button graphic must be white so the tint shows as-is.
    void ApplyHoverColors(Button button) =>
        UIBuilder.ApplyButtonColors(
            button,
            Color.clear,
            iconHoverColor,
            iconPressedColor,
            Color.clear
        );

    // Positions the glyph and label inside the cell from Label Placement, Icon Size, Gap, Height and Offset.
    void PlaceIconParts(RectTransform glyph, RectTransform label)
    {
        glyph.sizeDelta = new Vector2(iconSize, iconSize);

        void GlyphTopCenter(float y)
        {
            glyph.anchorMin = glyph.anchorMax = glyph.pivot = new Vector2(0.5f, 1f);
            glyph.anchoredPosition = new Vector2(0f, y);
        }

        if (labelPlacement == LabelPlacement.Right)
        {
            glyph.anchorMin = glyph.anchorMax = glyph.pivot = new Vector2(0f, 0.5f);
            glyph.anchoredPosition = Vector2.zero;
            label.anchorMin = Vector2.zero;
            label.anchorMax = Vector2.one;
            label.pivot = new Vector2(0.5f, 0.5f);
            label.offsetMin = new Vector2(iconSize + labelGap, 0f) + labelOffset;
            label.offsetMax = labelOffset;
            return;
        }

        label.anchorMin = new Vector2(0f, 1f); // full-width strip hanging from the top
        label.anchorMax = Vector2.one;
        label.pivot = new Vector2(0.5f, 1f);
        label.sizeDelta = new Vector2(0f, labelHeight);

        if (labelPlacement == LabelPlacement.Above)
        {
            label.anchoredPosition = labelOffset;
            GlyphTopCenter(-(labelHeight + labelGap));
        }
        else
        {
            GlyphTopCenter(0f);
            label.anchoredPosition = new Vector2(0f, -(iconSize + labelGap)) + labelOffset;
        }
    }

    #endregion

    #region Layout and built-in UI

    bool TryResolveLayout()
    {
        if (layoutMode == UILayoutMode.Custom)
        {
            if (panel == null || iconParent == null || iconPrefab == null || windowLayer == null)
            {
                Debug.LogError($"{name}: custom layout/icon prefab missing.", this);
                return false;
            }
            _panel = panel;
            _iconParent = iconParent;
            return true;
        }
        if (_panel == null)
            BuildDesktopUI();
        return true;
    }

    // BuiltIn mode: Canvas > Panel (border + background + wallpaper) > Icons (grid) + Taskbar + Windows (on top).
    void BuildDesktopUI()
    {
        var canvasObject = UIBuilder
            .CreateOverlayCanvas("[DesktopCanvas]", transform, CanvasSortOrder)
            .gameObject;

        var content = UIBuilder.CreateFramedRect(
            canvasObject.transform,
            "Panel",
            backgroundColor,
            panelBorderColor,
            panelBorderThickness,
            out RectTransform panelRect
        );

        if (wallpaper != null)
        {
            var wallpaperImage = UIBuilder
                .CreateStretched("Wallpaper", content)
                .gameObject.AddComponent<Image>();
            wallpaperImage.sprite = wallpaper;
            wallpaperImage.color = wallpaperTint;
            wallpaperImage.raycastTarget = false;
        }

        float barHeight = showTaskbar ? taskbarHeight : 0f;

        var icons = UIBuilder.CreateStretched("Icons", content);
        icons.offsetMin = new Vector2(0f, barHeight); // keep icons above the taskbar
        var grid = icons.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = cellSize;
        grid.spacing = new Vector2(GridSpacing, GridSpacing);
        grid.padding = new RectOffset(GridPadding, GridPadding, GridPadding, GridPadding);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, columns);

        if (showTaskbar)
            BuildTaskbar(content);

        var windows = UIBuilder.CreateStretched("Windows", content); // last child = drawn on top
        windows.offsetMin = new Vector2(0f, barHeight);
        BuildWindowFrame(windows);

        _iconParent = icons;
        _panel = panelRect.gameObject;
    }

    void BuildTaskbar(Transform parent)
    {
        var content = UIBuilder.CreateFramedRect(
            parent,
            "Taskbar",
            taskbarColor,
            taskbarBorderColor,
            taskbarBorderThickness,
            out RectTransform bar
        );
        bar.anchorMin = Vector2.zero; // bottom strip
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = Vector2.zero;
        bar.sizeDelta = new Vector2(0f, taskbarHeight);

        _clockText = UIBuilder.CreateText(content, "Clock", clockStyle);
        var clockRect = _clockText.rectTransform;
        clockRect.anchorMin = Vector2.zero;
        clockRect.anchorMax = Vector2.one;
        clockRect.offsetMin = new Vector2(ClockInset, 0f);
        clockRect.offsetMax = new Vector2(-ClockInset, 0f);
    }

    // One frame shared by every app window: border > title bar (title + close) + content.
    void BuildWindowFrame(Transform parent)
    {
        _frameContent = UIBuilder.BuildWindowFrame(
            parent,
            ResolveWindowStyle(),
            CloseCurrentWindow,
            out _frame,
            out _frameTitle
        );
        _frame.SetActive(false);
    }

    // The shared UI Style wins when assigned; otherwise the window fields on this component.
    WindowStyle ResolveWindowStyle()
    {
        if (uiStyle != null)
            return uiStyle.Window;
        return new WindowStyle
        {
            margin = windowMargin,
            titleBarHeight = windowTitleBarHeight,
            titleBarColor = windowTitleBarColor,
            titleStyle = windowTitleStyle,
            fillColor = windowFillColor,
            borderColor = windowBorderColor,
            borderThickness = windowBorderThickness,
            closeHoverColor = iconHoverColor,
            closePressedColor = iconPressedColor,
        };
    }

    #endregion

    #region Taskbar clock

    // Rebuilds the text only when the in-game minute changes.
    void RefreshClock()
    {
        if (_clockText == null || !TryReadClock(out int day, out int minuteOfDay))
            return;
        int key = day * MinutesPerDay + minuteOfDay;
        if (key == _lastClockKey)
            return;
        _lastClockKey = key;
        try
        {
            _clockText.text = string.Format(clockFormat, day, minuteOfDay / 60, minuteOfDay % 60);
        }
        catch (FormatException e)
        {
            Debug.LogWarning($"{name}: clock format is invalid ({e.Message}).", this);
            _clockText.text = "";
        }
    }

    // The only place that knows the clock sources. TimeOfDayDriver owns the hour mapping (open..close hours,
    // closing hold, night run) and writes it into TimeOfDayCycle, so reading the cycle matches the sun and
    // the driver exactly. The day number comes from DayManager.
    bool TryReadClock(out int day, out int minuteOfDay)
    {
        day = 0;
        minuteOfDay = 0;
        var dayManager = DayManager.Instance;
        var cycle = TimeOfDayCycle.Instance;
        if (dayManager == null || cycle == null)
        {
            WarnClockOnce(
                "needs a DayManager and a TimeOfDayCycle, so the taskbar clock stays empty."
            );
            return false;
        }
        day = Mathf.Max(1, dayManager.Day);
        minuteOfDay = cycle.MinuteOfDay;
        return true;
    }

    void WarnClockOnce(string message)
    {
        if (_clockWarned)
            return;
        _clockWarned = true;
        Debug.LogWarning($"{name}: {message}", this);
    }

    #endregion

    #region Validation

    // Clamps live here, not in [Min]: Unity runs one property drawer per field and VisibleWhen needs it.
    void OnValidate()
    {
        columns = Mathf.Max(1, columns);
        iconSize = Mathf.Max(1f, iconSize);
        labelHeight = Mathf.Max(1f, labelHeight);
        labelGap = Mathf.Max(0f, labelGap);
        panelBorderThickness = Mathf.Max(0f, panelBorderThickness);
        taskbarHeight = Mathf.Max(0f, taskbarHeight);
        taskbarBorderThickness = Mathf.Max(0f, taskbarBorderThickness);
        windowTitleBarHeight = Mathf.Max(0f, windowTitleBarHeight);
        windowBorderThickness = Mathf.Max(0f, windowBorderThickness);
        windowMargin = Mathf.Max(0f, windowMargin);
        if (entries == null)
            return;
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].screen != null && entries[i].screen.GetComponent<IDesktopApp>() == null)
                Debug.LogWarning(
                    $"{name}: entry {i} ('{entries[i].screen.name}') does not implement IDesktopApp.",
                    this
                );
    }

    #endregion
}
