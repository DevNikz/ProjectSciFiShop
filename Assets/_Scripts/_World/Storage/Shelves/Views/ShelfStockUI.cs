using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Formats one shelf's stock into a TMP label that fades in only while the player aims at the shelf.
/// Also publishes this shelf's prompt lines (place, pick up, "Shelf full"...) to PromptUI as a
/// prompt context provider, so the shelf draws no prompt of its own.
/// </summary>
public class ShelfStockUI : MonoBehaviour, IPromptSource, IPromptContextProvider
{
    enum ShelfPromptState
    {
        None,
        SelectProduct,
        PlaceSelected,
        Place,
        Full,
        CannotStock,
        PickUp,
        TakeLast,
        Empty,
    }

    const float NoticeSeconds = 1.5f;
    const string NoticeColor = "#FFB84D";

    static readonly PromptEntry[][] StateLines =
    {
        System.Array.Empty<PromptEntry>(), // None
        One(PromptAction.None, "Select a box product", PromptContext.Holding),
        One(PromptAction.Interact, "Place selected product", PromptContext.Holding),
        One(PromptAction.Interact, "Place product", PromptContext.Holding),
        One(PromptAction.None, "Shelf full", PromptContext.Holding),
        One(PromptAction.None, "This item cannot be stocked", PromptContext.Holding),
        One(PromptAction.Interact, "Pick up product", PromptContext.EmptyHands),
        One(PromptAction.Interact, "Take last product", PromptContext.EmptyHands),
        One(PromptAction.None, "Shelf empty", PromptContext.EmptyHands),
    };

    [SerializeField]
    ShelfInventory shelf;

    [SerializeField]
    TMP_Text stockLabel;

    [Tooltip(
        "Fades the whole stock panel. Empty = found on the label's parents. None = always visible."
    )]
    [SerializeField]
    CanvasGroup stockGroup;

    [Tooltip("Seconds to fade the stock panel in and out.")]
    [SerializeField]
    float fadeDuration = 0.15f;

    [SerializeField]
    Interactor interactor;

    [Header("Content Fit")]
    [Tooltip(
        "The panel that is resized to its content. Its direct child is the label. Empty = the stock group's object, else the label's parent."
    )]
    [SerializeField]
    RectTransform panel;

    [Tooltip("Narrowest label width, in canvas units.")]
    [SerializeField]
    float fitMinWidth = 160f;

    [Tooltip("Widest label width, in canvas units. Longer lines wrap. 0 = no limit.")]
    [SerializeField]
    float fitMaxWidth = 320f;

    [Tooltip(
        "Space between the panel edge and the label. Applied by the 'Setup content fit' context menu."
    )]
    [SerializeField]
    RectOffset fitPadding;

    [Header("Slot Outline")]
    [Tooltip("Width and depth of the flat placement marker, in metres.")]
    [SerializeField]
    Vector2 slotOutlineSize = new(0.28f, 0.28f);

    [Tooltip(
        "Distance below the slot pivot to the shelf surface. Use a negative value when slots sit at product-centre height."
    )]
    [SerializeField]
    float slotSurfaceOffset = -0.12f;

    [SerializeField]
    Color outlineColor = new(0.25f, 0.9f, 1f, 1f);

    [Header("Prompt")]
    [Tooltip("Ranks this shelf against other prompt providers. The highest one that applies wins.")]
    [SerializeField]
    int promptPriority;

    readonly Dictionary<string, int> stock = new();
    readonly Dictionary<string, string> names = new();
    readonly List<string> ids = new();
    LineRenderer outline;
    Material outlineMaterial;
    LayoutElement labelLayout;
    Tween fadeTween;
    bool isStockVisible;
    bool isFocused;
    ShelfPromptState promptState;
    PromptEntry[] noticeLines;
    float noticeUntil;

    // Resolved once per held object, not per frame (same idea as PromptPublisher).
    Carriable heldObj;
    Interactable heldSource;
    IPromptContextFlag[] heldFlags = System.Array.Empty<IPromptContextFlag>();

    bool IsNoticeActive => noticeLines != null && Time.unscaledTime < noticeUntil;

    public int Priority => promptPriority;

    /// <summary>The notice while it is active, else the line for what the player can do here.</summary>
    public IReadOnlyList<PromptEntry> Prompts =>
        IsNoticeActive ? noticeLines : StateLines[(int)promptState];

    void Awake()
    {
        if (stockLabel == null)
            stockLabel = GetComponent<TMP_Text>();
        if (stockGroup == null && stockLabel != null)
            stockGroup = stockLabel.GetComponentInParent<CanvasGroup>();
        if (stockGroup == null)
            Debug.LogWarning(
                $"{name}: no CanvasGroup, so the stock panel stays always visible.",
                this
            );
        if (interactor == null)
            interactor = FindFirstObjectByType<Interactor>();

        panel = ResolvePanel();
        if (stockLabel != null)
            labelLayout = stockLabel.GetComponent<LayoutElement>();
        if (panel == null || labelLayout == null)
            Debug.LogWarning(
                $"{name}: no panel or label Layout Element, so the stock panel does not fit its content. Use 'Setup content fit'.",
                this
            );
    }

    RectTransform ResolvePanel()
    {
        if (panel != null)
            return panel;
        if (stockGroup != null)
            return stockGroup.transform as RectTransform;
        return stockLabel != null ? stockLabel.rectTransform.parent as RectTransform : null;
    }

    void OnEnable()
    {
        if (shelf != null)
        {
            shelf.Changed += Refresh;
            shelf.PlacementRejected += ShowNotice;
        }
        Refresh();
        isStockVisible = false;
        isFocused = false;
        promptState = ShelfPromptState.None;
        if (stockGroup != null)
        {
            stockGroup.alpha = 0f;
            stockGroup.blocksRaycasts = false;
        }
        //PromptBus.Register(this);
    }

    void OnDisable()
    {
        if (shelf != null)
        {
            shelf.Changed -= Refresh;
            shelf.PlacementRejected -= ShowNotice;
        }
        //PromptBus.Unregister(this);
        fadeTween?.Kill();
        fadeTween = null;
        if (outline != null)
            outline.enabled = false;
    }

    void Refresh()
    {
        if (shelf == null || stockLabel == null)
            return;
        shelf.GetStock(stock, names);
        ids.Clear();
        ids.AddRange(stock.Keys);
        ids.Sort(System.StringComparer.Ordinal);
        var text = new StringBuilder();
        text.Append("<b>SHELF STOCK</b>\n")
            .Append("<size=78%><color=#AAB7C4>")
            .Append(shelf.ShelfName)
            .Append("  ·  ")
            .Append(shelf.Count)
            .Append(" / ")
            .Append(shelf.Capacity)
            .Append(" SLOTS</color></size>\n")
            .Append("<size=72%><color=#7F91A5>PRODUCTS</color></size>");
        if (ids.Count == 0)
            text.Append("\n<color=#AAB7C4>No products stocked</color>");
        foreach (var id in ids)
            text.Append("\n")
                .Append(names[id])
                .Append("  <color=#72E6FF><b>× ")
                .Append(stock[id])
                .Append("</b></color>");
        stockLabel.text = text.ToString();
        ApplyLabelWidth();
        if (panel != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel); // size is right before the fade-in
    }

    // The label is as wide as its longest line, kept between the min and max. Longer lines wrap.
    void ApplyLabelWidth()
    {
        if (labelLayout == null)
            return;
        float width = stockLabel.GetPreferredValues(stockLabel.text).x;
        if (fitMaxWidth > 0f)
            width = Mathf.Min(width, fitMaxWidth);
        labelLayout.preferredWidth = Mathf.Max(width, fitMinWidth);
    }

    /// <summary>Adds the layout components that make the panel fit the label. Safe to run again.</summary>
    [ContextMenu("Setup content fit")]
    void EnsureContentFit()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Debug.LogWarning($"{name}: set up content fit in Edit Mode so it is saved.", this);
            return;
        }
        var target = ResolvePanel();
        if (target == null || stockLabel == null)
        {
            Debug.LogWarning($"{name}: assign the label (and a panel) first.", this);
            return;
        }
        if (stockLabel.rectTransform.parent != target)
        {
            Debug.LogWarning($"{name}: the label must be a direct child of the panel.", this);
            return;
        }
        if (target.TryGetComponent(out LayoutGroup other) && other is not VerticalLayoutGroup)
        {
            Debug.LogWarning(
                $"{name}: the panel already has a {other.GetType().Name}. Remove it first.",
                this
            );
            return;
        }
        if (target.anchorMin != target.anchorMax)
            Debug.LogWarning(
                $"{name}: the panel has stretched anchors, which fight the size fitter.",
                this
            );

        Undo.RecordObject(this, "Setup content fit");
        panel = target;

        var group = target.GetComponent<VerticalLayoutGroup>();
        if (group == null)
            group = Undo.AddComponent<VerticalLayoutGroup>(target.gameObject);
        group.padding = new RectOffset(
            fitPadding.left,
            fitPadding.right,
            fitPadding.top,
            fitPadding.bottom
        );
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;

        var fitter = target.GetComponent<ContentSizeFitter>();
        if (fitter == null)
            fitter = Undo.AddComponent<ContentSizeFitter>(target.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var element = stockLabel.GetComponent<LayoutElement>();
        if (element == null)
            element = Undo.AddComponent<LayoutElement>(stockLabel.gameObject);
        element.preferredWidth = fitMinWidth;

        Undo.RecordObject(stockLabel, "Setup content fit");
        stockLabel.textWrappingMode = TextWrappingModes.Normal;
        EditorUtility.SetDirty(this);
        Debug.Log($"{name}: content fit set up on '{target.name}'.", this);
#endif
    }

    void ShowNotice(string message)
    {
        noticeLines = One(
            PromptAction.None,
            $"<color={NoticeColor}>{message}</color>",
            PromptContext.Any
        );
        noticeUntil = Time.unscaledTime + NoticeSeconds;
    }

    /// <summary>Lets the prompt show while aimed at this shelf, or while its notice is up.</summary>
    public bool TryGetState(out PromptState state)
    {
        state = default;
        if (interactor == null || (!isFocused && !IsNoticeActive))
            return false;

        if (!interactor.IsHolding)
        {
            state = new PromptState { source = this, context = PromptContext.EmptyHands };
            return true;
        }

        ResolveHeld(interactor.Carrier.Held);
        var context = PromptContext.Holding;
        for (int i = 0; i < heldFlags.Length; i++)
            context |= heldFlags[i].Flag; // read each frame: look-at bits change while aiming

        // The held object's own lines first, then this shelf's.
        state = new PromptState
        {
            source = heldSource != null ? heldSource : this,
            extra = heldSource != null ? this : null,
            context = context,
        };
        return true;
    }

    void ResolveHeld(Carriable held)
    {
        if (held == heldObj)
            return;
        heldObj = held;
        heldSource = held != null && held.TryGetComponent(out Interactable i) ? i : null;
        heldFlags =
            held != null
                ? held.GetComponents<IPromptContextFlag>()
                : System.Array.Empty<IPromptContextFlag>();
    }

    void LateUpdate()
    {
        if (shelf == null || interactor == null || interactor.Origin == null)
        {
            isFocused = false;
            promptState = ShelfPromptState.None;
            SetStockVisible(false);
            DrawOutline(null);
            return;
        }

        bool focused = false;
        ShelfProduct aimedProduct = null;
        if (
            interactor.Cast(
                interactor.Origin.position,
                interactor.Origin.forward,
                interactor.InteractRange,
                interactor.Carrier.Held != null ? interactor.Carrier.Held.transform : null,
                out var hit
            )
        )
        {
            focused = hit.collider.GetComponentInParent<ShelfInventory>() == shelf;
            aimedProduct = hit.collider.GetComponentInParent<ShelfProduct>();
        }

        // A freed cursor (open UI, Esc) means the player is not looking at the shelf.
        var input = InputManager.Instance;
        if (input != null && !input.CursorLocked)
            focused = false;

        Transform slot = null;
        var state = ShelfPromptState.None;
        if (focused)
        {
            if (interactor.IsHolding)
            {
                var held = interactor.Carrier.Held;
                if (held.TryGetComponent<CardboardBox>(out var box))
                {
                    slot = shelf.GetPlacementSlot(box.SelectedItem);
                    state =
                        box.SelectedItem == null ? ShelfPromptState.SelectProduct
                        : slot == null ? ShelfPromptState.Full
                        : ShelfPromptState.PlaceSelected;
                }
                else
                {
                    slot = shelf.GetPlacementSlot(held);
                    state =
                        held.GetComponent<ShelfProduct>() == null ? ShelfPromptState.CannotStock
                        : slot == null ? ShelfPromptState.Full
                        : ShelfPromptState.Place;
                }
            }
            else if (aimedProduct != null && aimedProduct.Shelf == shelf)
            {
                slot = shelf.GetStoredSlot(aimedProduct);
                state = ShelfPromptState.PickUp;
            }
            else
                state = shelf.Count > 0 ? ShelfPromptState.TakeLast : ShelfPromptState.Empty;
        }

        isFocused = focused;
        promptState = state;
        SetStockVisible(focused);
        DrawOutline(slot);
    }

    void SetStockVisible(bool visible)
    {
        if (stockGroup == null || visible == isStockVisible)
            return;
        isStockVisible = visible;
        fadeTween?.Kill();
        stockGroup.blocksRaycasts = visible;
        fadeTween = stockGroup
            .DOFade(visible ? 1f : 0f, fadeDuration)
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    static PromptEntry[] One(PromptAction action, string text, PromptContext when) =>
        new[]
        {
            new PromptEntry
            {
                action = action,
                text = text,
                when = when,
            },
        };

    void DrawOutline(Transform slot)
    {
        if (slot == null)
        {
            if (outline != null)
                outline.enabled = false;
            return;
        }
        if (outline == null)
        {
            var shader =
                Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null)
                return;
            outlineMaterial = new Material(shader) { color = outlineColor };
            outline = new GameObject("Shelf Slot Highlight").AddComponent<LineRenderer>();
            outline.sharedMaterial = outlineMaterial;
            outline.widthMultiplier = 0.012f;
            outline.useWorldSpace = true;
            outline.loop = false;
        }
        outline.enabled = true;
        // A flat footprint reads as a placement area and does not compete with pickup outlines.
        float halfX = Mathf.Max(0.01f, slotOutlineSize.x) * 0.5f;
        float halfZ = Mathf.Max(0.01f, slotOutlineSize.y) * 0.5f;
        float y = slotSurfaceOffset + 0.003f; // prevents z-fighting with the shelf mesh
        Vector3[] local =
        {
            new(-halfX, y, -halfZ),
            new(halfX, y, -halfZ),
            new(halfX, y, halfZ),
            new(-halfX, y, halfZ),
            new(-halfX, y, -halfZ),
        };
        outline.positionCount = local.Length;
        for (int i = 0; i < local.Length; i++)
            outline.SetPosition(i, slot.TransformPoint(local[i]));
    }

    // RectOffset can't be built in a field initializer (Unity throws), so defaults are set here.
    void Reset() => fitPadding = new RectOffset(12, 12, 12, 12);

    void OnValidate()
    {
        fadeDuration = Mathf.Max(0f, fadeDuration);
        fitMinWidth = Mathf.Max(0f, fitMinWidth);
        fitMaxWidth = Mathf.Max(0f, fitMaxWidth);
        if (fitMaxWidth > 0f && fitMaxWidth < fitMinWidth)
            fitMaxWidth = fitMinWidth;
    }

    void OnDestroy()
    {
        fadeTween?.Kill();
        if (outline != null)
            Destroy(outline.gameObject);
        if (outlineMaterial != null)
            Destroy(outlineMaterial);
    }
}
