using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using TMPro;
using UnityEngine;

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

    void OnValidate() => fadeDuration = Mathf.Max(0f, fadeDuration);

    void OnDestroy()
    {
        fadeTween?.Kill();
        if (outline != null)
            Destroy(outline.gameObject);
        if (outlineMaterial != null)
            Destroy(outlineMaterial);
    }
}
