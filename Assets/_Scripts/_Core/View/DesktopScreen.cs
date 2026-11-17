using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The computer desktop: one icon per app (OrderScreen, FurnitureScreen...). An icon opens that app as a
/// window, its close button returns here, and Esc closes the window first, then the desktop.
/// BuiltIn makes the canvas, icon grid, taskbar and window frame in code. Custom uses the scene objects you assign.
/// Needs an EventSystem in the scene.
/// </summary>
public class DesktopScreen : MonoBehaviour, IScreen
{
    enum LayoutMode
    {
        BuiltIn,
        Custom,
    }

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

    /// <summary>Text look used by the built-in icon labels, window title and clock.</summary>
    [Serializable]
    class TextStyle
    {
        [Tooltip("Empty = the TMP default font.")]
        public TMP_FontAsset font;

        [Min(1f)]
        public float size = 24f;
        public Color color = Color.white;

        [Tooltip("Bold, Italic, Underline, UpperCase... can be combined.")]
        public FontStyles fontStyle = FontStyles.Normal;
        public TextAlignmentOptions alignment = TextAlignmentOptions.Center;

        [Tooltip("0 = no outline.")]
        [Min(0f)]
        public float outlineWidth;
        public Color outlineColor = Color.black;

        public void Apply(TMP_Text text)
        {
            if (font != null)
                text.font = font;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            if (outlineWidth > 0f)
            {
                text.outlineWidth = outlineWidth;
                text.outlineColor = outlineColor;
            }
        }
    }

    static readonly Color MissingIconColor = new(0.25f, 0.3f, 0.4f, 1f);
    const string GlyphName = "Glyph";

    [Header("Layout")]
    [Tooltip(
        "BuiltIn: the canvas, icon grid and window layer are made in code. Custom: use the objects below."
    )]
    [SerializeField]
    LayoutMode mode = LayoutMode.BuiltIn;

    [Header("Custom layout (Custom mode only)")]
    [Tooltip("Shown while open. Must be a child: this object has to stay active.")]
    [SerializeField]
    GameObject panel;

    [Tooltip("Icons are created here (a grid or layout group works well).")]
    [SerializeField]
    Transform iconParent;

    [Tooltip("A Button. Its target graphic gets the icon sprite, a TMP_Text child gets the label.")]
    [SerializeField]
    Button iconPrefab;

    [Tooltip("App windows are placed here. Keep it above the icons so windows draw on top.")]
    [SerializeField]
    Transform windowLayer;

    [Header("Built-in look (BuiltIn mode only)")]
    [SerializeField]
    Color backgroundColor = new(0.08f, 0.1f, 0.14f, 1f);

    [SerializeField]
    Vector2 cellSize = new(160f, 180f);

    [SerializeField]
    int columns = 4;

    [Header("Built-in icon labels (BuiltIn mode only)")]
    [SerializeField]
    TextStyle labelStyle = new();

    [Tooltip("Where the label sits next to the icon. Use a left alignment in the style for Right.")]
    [SerializeField]
    LabelPlacement labelPlacement = LabelPlacement.Below;

    [Tooltip("Icon image size in pixels.")]
    [SerializeField]
    float iconSize = 96f;

    [Tooltip("Space between the icon and its label, in pixels.")]
    [SerializeField]
    float labelGap = 8f;

    [Tooltip("Extra label shift in pixels, added after placement.")]
    [SerializeField]
    Vector2 labelOffset = Vector2.zero;

    [Tooltip("Label height for Below and Above.")]
    [SerializeField]
    float labelHeight = 40f;

    [Tooltip("Icon background while hovered.")]
    [SerializeField]
    Color iconHoverColor = new(1f, 1f, 1f, 0.15f);

    [Tooltip("Icon background while pressed.")]
    [SerializeField]
    Color iconPressedColor = new(1f, 1f, 1f, 0.3f);

    [Header("Built-in desktop chrome (BuiltIn mode only)")]
    [SerializeField]
    Color panelBorderColor = new(0.35f, 0.4f, 0.5f, 1f);

    [SerializeField]
    float panelBorderThickness = 4f;

    [Tooltip("Optional. Drawn over the background color.")]
    [SerializeField]
    Sprite wallpaper;

    [SerializeField]
    Color wallpaperTint = Color.white;

    [Header("Built-in taskbar (BuiltIn mode only)")]
    [SerializeField]
    bool showTaskbar = true;

    [SerializeField]
    float taskbarHeight = 56f;

    [SerializeField]
    Color taskbarColor = new(0.05f, 0.06f, 0.09f, 1f);

    [SerializeField]
    Color taskbarBorderColor = new(0.3f, 0.35f, 0.45f, 1f);

    [SerializeField]
    float taskbarBorderThickness = 2f;

    [SerializeField]
    TextStyle clockStyle = new() { size = 22f, alignment = TextAlignmentOptions.MidlineRight };

    [Tooltip("{0} = day, {1} = hour, {2} = minute. Example: Day {0}  {1:00}:{2:00}")]
    [SerializeField]
    string clockFormat = "Day {0}  {1:00}:{2:00}";

    [Header("Built-in window frame (BuiltIn mode only)")]
    [Tooltip("Gap between the frame and the screen edge, in pixels.")]
    [SerializeField]
    float windowMargin = 80f;

    [SerializeField]
    float titleBarHeight = 44f;

    [SerializeField]
    Color titleBarColor = new(0.18f, 0.22f, 0.32f, 1f);

    [SerializeField]
    TextStyle titleStyle = new() { size = 22f, alignment = TextAlignmentOptions.MidlineLeft };

    [SerializeField]
    Color windowFillColor = new(0.12f, 0.14f, 0.2f, 1f);

    [SerializeField]
    Color windowBorderColor = new(0.35f, 0.4f, 0.5f, 1f);

    [SerializeField]
    float windowBorderThickness = 3f;

    [Header("Apps")]
    [Tooltip("One icon per entry, in this order.")]
    [SerializeField]
    Entry[] entries = Array.Empty<Entry>();

    [Tooltip("Optional. Opens the desktop when interacted with (the terminal).")]
    [SerializeField]
    Interactable opener;

    readonly List<IDesktopApp> _apps = new();
    GameObject _panel;
    Transform _iconParent,
        _windowLayer;
    GameObject _frame; // BuiltIn: the one shared window frame
    Transform _frameContent;
    TMP_Text _frameTitle,
        _clockText;
    IDesktopApp _current;
    int _lastClockKey = -1;
    bool _open,
        _built,
        _clockWarned;

    public ScreenId Id => ScreenId.Desktop;
    public bool IsOpen => _open;

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

    void HandleOpener(Interactor _) => Open();

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

    // One icon per entry, built the first time the desktop opens.
    void BuildIcons()
    {
        if (_built)
            return;
        foreach (var entry in entries)
        {
            var app = entry.screen.GetComponent<IDesktopApp>();
            if (app == null)
            {
                Debug.LogWarning($"{name}: entry '{entry.label}' has no screen, not listed.", this);
                continue;
            }
            if (mode == LayoutMode.BuiltIn && app.WindowRoot != null)
                app.WindowRoot.SetParent(_frameContent, false);

            string title = string.IsNullOrEmpty(entry.label) ? entry.screen.name : entry.label;
            var button = CreateIconButton();
            BindIcon(button, entry);
            button.onClick.AddListener(() => HandleIconClick(app, title));
            app.WindowClosed += HandleWindowClosed;
            _apps.Add(app);
        }
        _built = true;
    }

    void BindIcon(Button button, Entry entry)
    {
        // BuiltIn: the sprite goes on the Glyph child (the button root is the hover background).
        Image image =
            mode == LayoutMode.BuiltIn
                ? button.transform.Find(GlyphName).GetComponent<Image>()
                : button.targetGraphic as Image;
        if (image != null)
        {
            image.sprite = entry.icon;
            image.color = entry.icon != null ? Color.white : MissingIconColor;
        }
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = string.IsNullOrEmpty(entry.label) ? entry.screen.name : entry.label;
    }

    bool TryResolveLayout()
    {
        if (mode == LayoutMode.Custom)
        {
            if (panel == null || iconParent == null || iconPrefab == null || windowLayer == null)
            {
                Debug.LogError($"{name}: custom layout/icon prefab missing.", this);
                return false;
            }
            _panel = panel;
            _iconParent = iconParent;
            _windowLayer = windowLayer;
            return true;
        }
        if (_panel == null)
            BuildDesktopUI();
        return true;
    }

    // BuiltIn mode: Canvas > Panel (border + background + wallpaper) > Icons (grid) + Taskbar + Windows (on top).
    void BuildDesktopUI()
    {
        var canvasObject = new GameObject(
            "[DesktopCanvas]",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        canvasObject.transform.SetParent(transform, false);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.GetComponent<Canvas>().sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var content = CreateFramedRect(
            canvasObject.transform,
            "Panel",
            backgroundColor,
            panelBorderColor,
            panelBorderThickness,
            out RectTransform panelRect
        );

        if (wallpaper != null)
        {
            var wallpaperImage = CreateStretched("Wallpaper", content)
                .gameObject.AddComponent<Image>();
            wallpaperImage.sprite = wallpaper;
            wallpaperImage.color = wallpaperTint;
            wallpaperImage.raycastTarget = false;
        }

        float barHeight = showTaskbar ? taskbarHeight : 0f;

        var icons = CreateStretched("Icons", content);
        icons.offsetMin = new Vector2(0f, barHeight); // keep icons above the taskbar
        var grid = icons.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = cellSize;
        grid.spacing = new Vector2(24f, 24f);
        grid.padding = new RectOffset(48, 48, 48, 48);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, columns);

        if (showTaskbar)
            BuildTaskbar(content);

        _windowLayer = CreateStretched("Windows", content); // last child = drawn on top
        ((RectTransform)_windowLayer).offsetMin = new Vector2(0f, barHeight);
        BuildWindowFrame(_windowLayer);

        _iconParent = icons;
        _panel = panelRect.gameObject;
    }

    void BuildTaskbar(Transform parent)
    {
        var content = CreateFramedRect(
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

        _clockText = CreateText(content, "Clock", clockStyle);
        var clockRect = _clockText.rectTransform;
        clockRect.anchorMin = Vector2.zero;
        clockRect.anchorMax = Vector2.one;
        clockRect.offsetMin = new Vector2(16f, 0f);
        clockRect.offsetMax = new Vector2(-16f, 0f);
    }

    // One frame shared by every app window: border > title bar (title + close) + content.
    void BuildWindowFrame(Transform parent)
    {
        var content = CreateFramedRect(
            parent,
            "WindowFrame",
            windowFillColor,
            windowBorderColor,
            windowBorderThickness,
            out RectTransform frame
        );
        frame.offsetMin = new Vector2(windowMargin, windowMargin);
        frame.offsetMax = new Vector2(-windowMargin, -windowMargin);

        var titleBar = CreateStretched("TitleBar", content);
        titleBar.anchorMin = new Vector2(0f, 1f);
        titleBar.anchorMax = Vector2.one;
        titleBar.pivot = new Vector2(0.5f, 1f);
        titleBar.anchoredPosition = Vector2.zero;
        titleBar.sizeDelta = new Vector2(0f, titleBarHeight);
        titleBar.gameObject.AddComponent<Image>().color = titleBarColor;

        _frameTitle = CreateText(titleBar, "Title", titleStyle);
        var titleRect = _frameTitle.rectTransform;
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.offsetMin = new Vector2(12f, 0f);
        titleRect.offsetMax = new Vector2(-(titleBarHeight + 8f), 0f);

        var closeObject = new GameObject(
            "Close",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );
        var closeRect = (RectTransform)closeObject.transform;
        closeRect.SetParent(titleBar, false);
        closeRect.anchorMin = new Vector2(1f, 0f);
        closeRect.anchorMax = Vector2.one;
        closeRect.pivot = new Vector2(1f, 0.5f);
        closeRect.anchoredPosition = Vector2.zero;
        closeRect.sizeDelta = new Vector2(titleBarHeight, 0f);
        var closeButton = closeObject.GetComponent<Button>();
        closeButton.targetGraphic = closeObject.GetComponent<Image>();
        ApplyHoverColors(closeButton);
        closeButton.onClick.AddListener(CloseCurrentWindow);
        var closeLabel = CreateText(closeRect, "X", titleStyle);
        closeLabel.text = "X";
        closeLabel.alignment = TextAlignmentOptions.Center;
        var closeLabelRect = closeLabel.rectTransform;
        closeLabelRect.anchorMin = Vector2.zero;
        closeLabelRect.anchorMax = Vector2.one;
        closeLabelRect.offsetMin = Vector2.zero;
        closeLabelRect.offsetMax = Vector2.zero;

        var body = CreateStretched("Content", content);
        body.offsetMax = new Vector2(0f, -titleBarHeight);
        _frameContent = body;

        _frame = frame.gameObject;
        _frame.SetActive(false);
    }

    static RectTransform CreateStretched(string objectName, Transform parent)
    {
        var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    // A border-colored rect with a fill-colored rect inset by the thickness. Returns the fill (put content there).
    static RectTransform CreateFramedRect(
        Transform parent,
        string objectName,
        Color fill,
        Color border,
        float thickness,
        out RectTransform outer
    )
    {
        outer = CreateStretched(objectName, parent);
        outer.gameObject.AddComponent<Image>().color = border;
        var inner = CreateStretched("Fill", outer);
        inner.offsetMin = new Vector2(thickness, thickness);
        inner.offsetMax = new Vector2(-thickness, -thickness);
        inner.gameObject.AddComponent<Image>().color = fill;
        return inner;
    }

    static TextMeshProUGUI CreateText(Transform parent, string objectName, TextStyle style)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.raycastTarget = false; // clicks reach the button
        style.Apply(text);
        return text;
    }

    // Transparent until hovered or pressed. The button graphic must be white so the tint shows as-is.
    void ApplyHoverColors(Button button)
    {
        var colors = button.colors;
        colors.normalColor = Color.clear;
        colors.highlightedColor = iconHoverColor;
        colors.pressedColor = iconPressedColor;
        colors.selectedColor = Color.clear; // do not stay lit after a click
        colors.disabledColor = Color.clear;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    // Custom: the designer's prefab. BuiltIn: a hover background with a Glyph image and a Label placed per the settings.
    Button CreateIconButton()
    {
        if (mode == LayoutMode.Custom)
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

        var label = CreateText(iconObject.transform, "Label", labelStyle);
        PlaceIconParts(glyph.rectTransform, label.rectTransform);
        return button;
    }

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

    // Rebuilds the text only when the in-game minute changes.
    void RefreshClock()
    {
        if (_clockText == null || !TryReadClock(out int day, out int minuteOfDay))
            return;
        int key = day * 1440 + minuteOfDay;
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

    void OnValidate()
    {
        columns = Mathf.Max(1, columns);
        iconSize = Mathf.Max(1f, iconSize);
        labelHeight = Mathf.Max(1f, labelHeight);
        labelGap = Mathf.Max(0f, labelGap);
        panelBorderThickness = Mathf.Max(0f, panelBorderThickness);
        taskbarHeight = Mathf.Max(0f, taskbarHeight);
        taskbarBorderThickness = Mathf.Max(0f, taskbarBorderThickness);
        titleBarHeight = Mathf.Max(0f, titleBarHeight);
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
}
