using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The options screen: volume, mouse sensitivity, screen mode, resolution, vsync, quality. Changes apply at
/// once and are written to the global settings file when the screen closes. Works as a desktop app, on its
/// own, or in the main menu. BuiltIn mode makes the controls in code; Custom mode uses the scene objects below.
/// </summary>
public class SettingsScreen : WindowScreen
{
    static readonly FullScreenMode[] ScreenModes =
    {
        FullScreenMode.FullScreenWindow,
        FullScreenMode.ExclusiveFullScreen,
        FullScreenMode.Windowed,
    };

    [Header("Custom layout (Custom mode only)")]
    [Tooltip("Required. 0..1.")]
    [SerializeField]
    Slider volumeSlider;

    [Tooltip("Required. 0.1..3.")]
    [SerializeField]
    Slider sensitivitySlider;

    [Tooltip("Optional. Shows the volume as a percentage.")]
    [SerializeField]
    TMP_Text volumeValue;

    [Tooltip("Optional. Shows the sensitivity multiplier.")]
    [SerializeField]
    TMP_Text sensitivityValue;

    [Tooltip("Optional. Each click picks the next resolution.")]
    [SerializeField]
    Button resolutionButton;

    [Tooltip("Optional. Each click picks the next screen mode.")]
    [SerializeField]
    Button screenModeButton;

    [Tooltip("Optional. Turns vsync on or off.")]
    [SerializeField]
    Button vsyncButton;

    [Tooltip("Optional. Each click picks the next quality level.")]
    [SerializeField]
    Button qualityButton;

    [Tooltip("Optional. Puts every option back to its default.")]
    [SerializeField]
    Button resetButton;

    GameSettingsData _working;
    readonly List<Vector2Int> _resolutions = new();

    public override ScreenId Id => ScreenId.Settings;
    protected override string DefaultTitle => "Settings";

    protected override bool HasCustomReferences() =>
        volumeSlider != null && sensitivitySlider != null;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!enabled)
            return;
        volumeSlider.onValueChanged.AddListener(HandleVolume);
        sensitivitySlider.onValueChanged.AddListener(HandleSensitivity);
        AddClick(resolutionButton, CycleResolution);
        AddClick(screenModeButton, CycleScreenMode);
        AddClick(vsyncButton, ToggleVsync);
        AddClick(qualityButton, CycleQuality);
        AddClick(resetButton, ResetAll);
    }

    protected override void OnDisable()
    {
        if (volumeSlider != null)
            volumeSlider.onValueChanged.RemoveListener(HandleVolume);
        if (sensitivitySlider != null)
            sensitivitySlider.onValueChanged.RemoveListener(HandleSensitivity);
        RemoveClick(resolutionButton, CycleResolution);
        RemoveClick(screenModeButton, CycleScreenMode);
        RemoveClick(vsyncButton, ToggleVsync);
        RemoveClick(qualityButton, CycleQuality);
        RemoveClick(resetButton, ResetAll);
        base.OnDisable();
    }

    static void AddClick(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.AddListener(action);
    }

    static void RemoveClick(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.RemoveListener(action);
    }

    protected override void OnSessionBegin()
    {
        BuildResolutionList();
        var settings = SettingsManager.Instance;
        _working = settings != null ? settings.Current.Clone() : new GameSettingsData();
        Refresh();
    }

    // Written when the window closes, so dragging a slider does not write a file every frame.
    protected override void OnSessionEnd()
    {
        var settings = SettingsManager.Instance;
        if (settings != null)
            settings.Save();
    }

    void BuildResolutionList()
    {
        _resolutions.Clear();
        foreach (var r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (!_resolutions.Contains(size))
                _resolutions.Add(size);
        }
    }

    void Push()
    {
        var settings = SettingsManager.Instance;
        if (settings != null)
            settings.Apply(_working);
        Refresh();
    }

    void HandleVolume(float value)
    {
        _working.masterVolume = value;
        Push();
    }

    void HandleSensitivity(float value)
    {
        _working.mouseSensitivity = value;
        Push();
    }

    void CycleResolution()
    {
        if (_resolutions.Count == 0)
            return;
        int index = _resolutions.FindIndex(r =>
            r.x == _working.screenWidth && r.y == _working.screenHeight
        );
        var next = _resolutions[(index + 1) % _resolutions.Count];
        _working.screenWidth = next.x;
        _working.screenHeight = next.y;
        Push();
    }

    void CycleScreenMode()
    {
        int index = System.Array.IndexOf(ScreenModes, (FullScreenMode)_working.fullScreenMode);
        _working.fullScreenMode = (int)ScreenModes[(index + 1) % ScreenModes.Length];
        Push();
    }

    void ToggleVsync()
    {
        _working.vsync = !_working.vsync;
        Push();
    }

    void CycleQuality()
    {
        int count = QualitySettings.names.Length;
        if (count == 0)
            return;
        int current =
            _working.qualityLevel >= 0 ? _working.qualityLevel : QualitySettings.GetQualityLevel();
        _working.qualityLevel = (current + 1) % count;
        Push();
    }

    void ResetAll()
    {
        var settings = SettingsManager.Instance;
        if (settings == null)
            return;
        settings.ResetToDefaults();
        _working = settings.Current.Clone();
        Refresh();
    }

    // Shows _working in the controls without firing their events.
    void Refresh()
    {
        if (_working == null)
            return;
        volumeSlider.SetValueWithoutNotify(_working.masterVolume);
        sensitivitySlider.SetValueWithoutNotify(_working.mouseSensitivity);
        SetText(volumeValue, $"{Mathf.RoundToInt(_working.masterVolume * 100f)}%");
        SetText(sensitivityValue, $"{_working.mouseSensitivity:0.00}x");

        string resolution =
            _working.screenWidth > 0
                ? $"Resolution: {_working.screenWidth}x{_working.screenHeight}"
                : $"Resolution: {Screen.width}x{Screen.height}";
        SetButtonText(resolutionButton, resolution);
        SetButtonText(
            screenModeButton,
            $"Screen: {ModeName((FullScreenMode)_working.fullScreenMode)}"
        );
        SetButtonText(vsyncButton, $"VSync: {(_working.vsync ? "On" : "Off")}");
        int quality =
            _working.qualityLevel >= 0 ? _working.qualityLevel : QualitySettings.GetQualityLevel();
        string[] names = QualitySettings.names;
        SetButtonText(
            qualityButton,
            $"Quality: {(quality >= 0 && quality < names.Length ? names[quality] : "-")}"
        );
    }

    static string ModeName(FullScreenMode mode) =>
        mode switch
        {
            FullScreenMode.ExclusiveFullScreen => "Fullscreen",
            FullScreenMode.FullScreenWindow => "Borderless",
            FullScreenMode.Windowed => "Windowed",
            _ => mode.ToString(),
        };

    static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value;
    }

    static void SetButtonText(Button button, string value)
    {
        if (button == null)
            return;
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = value;
    }

    // BuiltIn: one row per option, stacked from the top.
    protected override bool TryBuildContent(RectTransform panelRect, UIStyle look)
    {
        var list = look.List;
        var rows = panelRect.gameObject.AddComponent<VerticalLayoutGroup>();
        rows.padding = new RectOffset(
            (int)list.padding,
            (int)list.padding,
            (int)list.padding,
            (int)list.padding
        );
        rows.spacing = list.rowSpacing;
        rows.childAlignment = TextAnchor.UpperCenter;
        rows.childControlWidth = true;
        rows.childControlHeight = true;
        rows.childForceExpandWidth = true;
        rows.childForceExpandHeight = false;

        volumeSlider = AddSliderRow(panelRect, look, "Master volume", 0f, 1f, out volumeValue);
        sensitivitySlider = AddSliderRow(
            panelRect,
            look,
            "Mouse sensitivity",
            0.1f,
            3f,
            out sensitivityValue
        );
        resolutionButton = AddButtonRow(panelRect, look, "Resolution");
        screenModeButton = AddButtonRow(panelRect, look, "Screen");
        vsyncButton = AddButtonRow(panelRect, look, "VSync");
        qualityButton = AddButtonRow(panelRect, look, "Quality");
        resetButton = AddButtonRow(panelRect, look, "Reset to defaults");
        return true;
    }

    static RectTransform AddRow(Transform parent, ListStyle list)
    {
        var row = new GameObject(
            "Row",
            typeof(RectTransform),
            typeof(Image),
            typeof(HorizontalLayoutGroup)
        );
        row.transform.SetParent(parent, false);
        row.GetComponent<Image>().color = list.rowColor;
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset((int)list.rowPadding, (int)list.rowPadding, 4, 4);
        layout.spacing = list.cellSpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        row.AddComponent<LayoutElement>().preferredHeight = list.rowHeight * 0.75f;
        return (RectTransform)row.transform;
    }

    static Slider AddSliderRow(
        Transform parent,
        UIStyle look,
        string label,
        float min,
        float max,
        out TMP_Text valueText
    )
    {
        var list = look.List;
        var row = AddRow(parent, list);
        var title = UIBuilder.CreateText(row, "Label", list.nameText);
        title.text = label;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var slider = CreateSlider(row, list, min, max);
        var sliderElement = slider.gameObject.AddComponent<LayoutElement>();
        sliderElement.preferredWidth = 320f;
        sliderElement.preferredHeight = 30f;

        valueText = UIBuilder.CreateText(row, "Value", list.qtyText);
        valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;
        return slider;
    }

    static Button AddButtonRow(Transform parent, UIStyle look, string label)
    {
        var list = look.List;
        var row = AddRow(parent, list);
        var title = UIBuilder.CreateText(row, "Label", list.nameText);
        title.text = label;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var button = UIBuilder.CreateButton(row, "Button", label, look.Button);
        var element = button.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = 420f;
        element.preferredHeight = look.Button.height;
        return button;
    }

    static Slider CreateSlider(Transform parent, ListStyle list, float min, float max)
    {
        var root = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        root.transform.SetParent(parent, false);

        var background = NewImage("Background", root.transform, list.scrollbarTrackColor);
        background.anchorMin = new Vector2(0f, 0.35f);
        background.anchorMax = new Vector2(1f, 0.65f);
        background.offsetMin = background.offsetMax = Vector2.zero;

        var fillArea = new GameObject(
            "Fill Area",
            typeof(RectTransform)
        ).GetComponent<RectTransform>();
        fillArea.SetParent(root.transform, false);
        fillArea.anchorMin = new Vector2(0f, 0.35f);
        fillArea.anchorMax = new Vector2(1f, 0.65f);
        fillArea.offsetMin = new Vector2(5f, 0f);
        fillArea.offsetMax = new Vector2(-15f, 0f);
        var fill = NewImage("Fill", fillArea, list.scrollbarHandleColor);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = fill.offsetMax = Vector2.zero;

        var handleArea = new GameObject(
            "Handle Slide Area",
            typeof(RectTransform)
        ).GetComponent<RectTransform>();
        handleArea.SetParent(root.transform, false);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(10f, 0f);
        handleArea.offsetMax = new Vector2(-10f, 0f);
        var handle = NewImage("Handle", handleArea, Color.white);
        handle.anchorMin = new Vector2(0f, 0f);
        handle.anchorMax = new Vector2(0f, 1f);
        handle.sizeDelta = new Vector2(20f, 0f);

        var slider = root.GetComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        return slider;
    }

    static RectTransform NewImage(string objectName, Transform parent, Color color)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return (RectTransform)go.transform;
    }
}
