using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>Formats one shelf's stock into a TMP label and provides interaction feedback.</summary>
public class ShelfStockUI : MonoBehaviour
{
    [SerializeField]
    ShelfInventory shelf;

    [SerializeField]
    TMP_Text stockLabel;

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
    [SerializeField]
    TMP_Text promptLabel;

    readonly Dictionary<string, int> stock = new();
    readonly Dictionary<string, string> names = new();
    readonly List<string> ids = new();
    LineRenderer outline;
    Material outlineMaterial;
    string notice;
    float noticeUntil;

    void Awake()
    {
        if (stockLabel == null)
            stockLabel = GetComponent<TMP_Text>();
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
        if (promptLabel != null)
            promptLabel.gameObject.SetActive(false);
    }

    void OnDisable()
    {
        if (shelf != null)
        {
            shelf.Changed -= Refresh;
            shelf.PlacementRejected -= ShowNotice;
        }
        if (outline != null)
            outline.enabled = false;
        if (promptLabel != null)
            promptLabel.gameObject.SetActive(false);
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
        notice = message;
        noticeUntil = Time.unscaledTime + 1.5f;
    }

    void LateUpdate()
    {
        if (shelf == null || interactor == null || interactor.Origin == null)
            return;
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
        Transform slot = null;
        if (focused)
            slot = interactor.IsHolding
                ? interactor.Carrier.Held.TryGetComponent<CardboardBox>(out var box)
                    ? shelf.GetPlacementSlot(box.SelectedItem)
                    : shelf.GetPlacementSlot(interactor.Carrier.Held)
                : aimedProduct != null && aimedProduct.Shelf == shelf
                    ? shelf.GetStoredSlot(aimedProduct)
                    : null;
        DrawOutline(slot);
        UpdatePrompt(focused, aimedProduct);
    }

    void UpdatePrompt(bool focused, ShelfProduct aimedProduct)
    {
        if (promptLabel == null)
            return;
        bool showingNotice = Time.unscaledTime < noticeUntil;
        promptLabel.gameObject.SetActive(focused || showingNotice);
        if (showingNotice)
        {
            promptLabel.color = new Color(1f, 0.72f, 0.3f);
            promptLabel.text = notice;
        }
        else if (interactor.IsHolding)
        {
            promptLabel.color = Color.white;
            if (interactor.Carrier.Held.TryGetComponent<CardboardBox>(out var box))
                promptLabel.text =
                    box.SelectedItem == null
                        ? "["
                            + InputManager.Instance.GetLabel("BoxCycleNext")
                            + "/"
                            + InputManager.Instance.GetLabel("BoxCyclePrev")
                            + "]  Select a box product"
                    : shelf.GetPlacementSlot(box.SelectedItem) == null ? "Shelf full"
                    : "[" + InteractKey() + "]  Place selected product";
            else
                promptLabel.text =
                    interactor.Carrier.Held.GetComponent<ShelfProduct>() == null
                        ? "This item cannot be stocked"
                    : shelf.GetPlacementSlot(interactor.Carrier.Held) == null ? "Shelf full"
                    : "[" + InteractKey() + "]  Place product";
        }
        else
        {
            promptLabel.color = Color.white;
            promptLabel.text =
                aimedProduct != null && aimedProduct.Shelf == shelf
                    ? "[" + InteractKey() + "]  Pick up product"
                : shelf.Count > 0 ? "[" + InteractKey() + "]  Take last product"
                : "Shelf empty";
        }
    }

    static string InteractKey() =>
        InputManager.Instance != null ? InputManager.Instance.GetLabel("Interact") : "E";

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

    void OnDestroy()
    {
        if (outline != null)
            Destroy(outline.gameObject);
        if (outlineMaterial != null)
            Destroy(outlineMaterial);
    }
}
