using System;
using TMPro;
using UnityEngine;

public enum UILayoutMode
{
    BuiltIn, // the window, list and buttons are made in code
    Custom, // the scene objects assigned on the screen are used
}

/// <summary>Text look used by built-in UI (labels, titles, buttons). Field names are saved by DesktopScreen: never rename.</summary>
[Serializable]
public class UITextStyle
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

/// <summary>The popup window frame. Defaults match the desktop's window.</summary>
[Serializable]
public class WindowStyle
{
    [Tooltip("Gap between the frame and the screen edge, in pixels.")]
    [Min(0f)]
    public float margin = 80f;

    [Min(0f)]
    public float titleBarHeight = 44f;
    public Color titleBarColor = new(0.18f, 0.22f, 0.32f, 1f);
    public UITextStyle titleStyle = new()
    {
        size = 22f,
        alignment = TextAlignmentOptions.MidlineLeft,
    };
    public Color fillColor = new(0.12f, 0.14f, 0.2f, 1f);
    public Color borderColor = new(0.35f, 0.4f, 0.5f, 1f);

    [Min(0f)]
    public float borderThickness = 3f;

    [Tooltip("Close (X) button background while hovered.")]
    public Color closeHoverColor = new(1f, 1f, 1f, 0.15f);

    [Tooltip("Close (X) button background while pressed.")]
    public Color closePressedColor = new(1f, 1f, 1f, 0.3f);
}

/// <summary>Filled buttons (+, -, Unlock, Buy, Close, Upgrades).</summary>
[Serializable]
public class ButtonStyle
{
    public Color normalColor = new(0.25f, 0.35f, 0.55f, 1f);
    public Color hoverColor = new(0.33f, 0.45f, 0.7f, 1f);
    public Color pressedColor = new(0.2f, 0.28f, 0.45f, 1f);
    public Color disabledColor = new(0.2f, 0.22f, 0.28f, 0.7f);
    public UITextStyle textStyle = new() { size = 22f };

    [Tooltip("Width of a footer button.")]
    [Min(1f)]
    public float width = 140f;

    [Min(1f)]
    public float height = 44f;

    [Tooltip("Size of the + and - buttons.")]
    [Min(1f)]
    public float stepSize = 44f;
}

/// <summary>The header, scroll list, rows and footer of an order or furniture screen.</summary>
[Serializable]
public class ListStyle
{
    [Header("Colors")]
    public Color backgroundColor = new(0.09f, 0.11f, 0.16f, 1f);
    public Color headerColor = new(0.16f, 0.19f, 0.28f, 1f);
    public Color rowColor = new(0.16f, 0.19f, 0.27f, 1f);
    public Color footerColor = new(0.1f, 0.12f, 0.17f, 1f);
    public Color scrollbarTrackColor = new(0.06f, 0.07f, 0.1f, 1f);
    public Color scrollbarHandleColor = new(0.35f, 0.4f, 0.5f, 1f);

    [Header("Sizes (pixels)")]
    [Tooltip("Space around the header, list and footer.")]
    [Min(0f)]
    public float padding = 12f;

    [Min(1f)]
    public float headerHeight = 36f;

    [Min(1f)]
    public float rowHeight = 72f;

    [Min(1f)]
    public float footerHeight = 64f;

    [Tooltip("Space between rows.")]
    [Min(0f)]
    public float rowSpacing = 6f;

    [Tooltip("Left and right space inside a row, header and footer.")]
    [Min(0f)]
    public float rowPadding = 10f;

    [Tooltip("Space between the columns of a row.")]
    [Min(0f)]
    public float cellSpacing = 10f;

    [Min(1f)]
    public float iconSize = 56f;

    [Min(1f)]
    public float iconColumnWidth = 72f;

    [Min(1f)]
    public float nameMinWidth = 140f;

    [Min(1f)]
    public float detailMinWidth = 200f;

    [Tooltip("Width of the quantity column.")]
    [Min(1f)]
    public float qtyWidth = 64f;

    [Min(1f)]
    public float unlockWidth = 150f;

    [Min(1f)]
    public float scrollbarWidth = 14f;

    [Min(1f)]
    public float scrollSensitivity = 30f;

    [Header("Text")]
    public UITextStyle headerText = new()
    {
        size = 18f,
        color = new Color(0.7f, 0.75f, 0.85f, 1f),
        fontStyle = FontStyles.Bold,
        alignment = TextAlignmentOptions.MidlineLeft,
    };
    public UITextStyle nameText = new()
    {
        size = 22f,
        alignment = TextAlignmentOptions.MidlineLeft,
    };
    public UITextStyle detailText = new()
    {
        size = 18f,
        color = new Color(0.7f, 0.75f, 0.85f, 1f),
        alignment = TextAlignmentOptions.MidlineLeft,
    };
    public UITextStyle qtyText = new() { size = 22f };
    public UITextStyle footerText = new()
    {
        size = 22f,
        alignment = TextAlignmentOptions.MidlineLeft,
    };
}

/// <summary>
/// Authored look of the built-in order and furniture screens (and the desktop window frame, when assigned
/// there). Defaults match the desktop's current look, so an empty asset looks the same as the desktop.
/// </summary>
[CreateAssetMenu(fileName = "UIStyle", menuName = "Shop/UI Style")]
public class UIStyle : ScriptableObject
{
    static UIStyle _fallback;

    [SerializeField]
    WindowStyle window = new();

    [SerializeField]
    ButtonStyle button = new();

    [SerializeField]
    ListStyle list = new();

    public WindowStyle Window => window;
    public ButtonStyle Button => button;
    public ListStyle List => list;

    /// <summary>The default look, used when a screen has no UI Style assigned.</summary>
    public static UIStyle Fallback
    {
        get
        {
            if (_fallback == null)
            {
                _fallback = CreateInstance<UIStyle>();
                _fallback.name = "UIStyle (default)";
                _fallback.hideFlags = HideFlags.HideAndDontSave;
            }
            return _fallback;
        }
    }

    // Reset statics for "Enter Play Mode without Domain Reload".
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        if (_fallback != null)
            Destroy(_fallback);
        _fallback = null;
    }
}
