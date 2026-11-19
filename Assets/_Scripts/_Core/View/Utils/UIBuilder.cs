using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Code-built UI pieces shared by the desktop and the built-in order and furniture screens, so every
/// window looks the same. Nothing here keeps state.
/// </summary>
public static class UIBuilder
{
    public static RectTransform CreateStretched(string objectName, Transform parent)
    {
        var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Stretch(rect);
        return rect;
    }

    /// <summary>Fills the parent: anchors 0..1, no offsets.</summary>
    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // A border-colored rect with a fill-colored rect inset by the thickness. Returns the fill (put content there).
    public static RectTransform CreateFramedRect(
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

    public static TextMeshProUGUI CreateText(Transform parent, string objectName, UITextStyle style)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.raycastTarget = false; // clicks reach the button
        style.Apply(text);
        return text;
    }

    // The button graphic must be white so the tint shows as-is. Selected = normal: it must not stay lit after a click.
    public static void ApplyButtonColors(
        Button button,
        Color normal,
        Color highlighted,
        Color pressed,
        Color disabled
    )
    {
        var colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = highlighted;
        colors.pressedColor = pressed;
        colors.selectedColor = normal;
        colors.disabledColor = disabled;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    /// <summary>A full-screen overlay canvas (1920x1080 reference) with a raycaster.</summary>
    public static Canvas CreateOverlayCanvas(string objectName, Transform parent, int sortingOrder)
    {
        var canvasObject = new GameObject(
            objectName,
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        canvasObject.transform.SetParent(parent, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    /// <summary>
    /// One window frame: border > title bar (title + X) + content. Returns the content rect (put the app there).
    /// onClose (optional) is called by the X button.
    /// </summary>
    public static RectTransform BuildWindowFrame(
        Transform parent,
        WindowStyle style,
        Action onClose,
        out GameObject frame,
        out TMP_Text title
    )
    {
        var content = CreateFramedRect(
            parent,
            "WindowFrame",
            style.fillColor,
            style.borderColor,
            style.borderThickness,
            out RectTransform frameRect
        );
        frameRect.offsetMin = new Vector2(style.margin, style.margin);
        frameRect.offsetMax = new Vector2(-style.margin, -style.margin);

        var titleBar = CreateStretched("TitleBar", content);
        titleBar.anchorMin = new Vector2(0f, 1f);
        titleBar.anchorMax = Vector2.one;
        titleBar.pivot = new Vector2(0.5f, 1f);
        titleBar.anchoredPosition = Vector2.zero;
        titleBar.sizeDelta = new Vector2(0f, style.titleBarHeight);
        titleBar.gameObject.AddComponent<Image>().color = style.titleBarColor;

        title = CreateText(titleBar, "Title", style.titleStyle);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.offsetMin = new Vector2(12f, 0f);
        titleRect.offsetMax = new Vector2(-(style.titleBarHeight + 8f), 0f);

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
        closeRect.sizeDelta = new Vector2(style.titleBarHeight, 0f);
        var closeButton = closeObject.GetComponent<Button>();
        closeButton.targetGraphic = closeObject.GetComponent<Image>();
        ApplyButtonColors(
            closeButton,
            Color.clear,
            style.closeHoverColor,
            style.closePressedColor,
            Color.clear
        );
        if (onClose != null)
            closeButton.onClick.AddListener(() => onClose());
        var closeLabel = CreateText(closeRect, "X", style.titleStyle);
        closeLabel.text = "X";
        closeLabel.alignment = TextAlignmentOptions.Center;
        Stretch(closeLabel.rectTransform);

        var body = CreateStretched("Content", content);
        body.offsetMax = new Vector2(0f, -style.titleBarHeight);
        frame = frameRect.gameObject;
        return body;
    }

    /// <summary>A filled button with a centered, auto-shrinking label.</summary>
    public static Button CreateButton(
        Transform parent,
        string objectName,
        string label,
        ButtonStyle style
    )
    {
        var buttonObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );
        buttonObject.transform.SetParent(parent, false);
        var button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        ApplyButtonColors(
            button,
            style.normalColor,
            style.hoverColor,
            style.pressedColor,
            style.disabledColor
        );

        var text = CreateText(buttonObject.transform, "Label", style.textStyle);
        text.text = label;
        Stretch(text.rectTransform);
        text.enableAutoSizing = true; // "Unlock: 1500" must fit the button
        text.fontSizeMax = style.textStyle.size;
        text.fontSizeMin = Mathf.Min(12f, style.textStyle.size);
        return button;
    }
}
