using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One product line on the order screen.</summary>
public class OrderRow : MonoBehaviour
{
    [SerializeField]
    Image icon;

    [SerializeField]
    TMP_Text nameLabel;

    [Tooltip("Units, cost and price line.")]
    [SerializeField]
    TMP_Text detailLabel;

    [Tooltip("Boxes in the cart.")]
    [SerializeField]
    TMP_Text qtyLabel;

    [SerializeField]
    Button plusButton;

    [SerializeField]
    Button minusButton;

    [Tooltip("Optional. Shown on locked products; buys the unlock.")]
    [SerializeField]
    Button unlockButton;

    [Tooltip("Optional. Shows the unlock cost (put it on the unlock button).")]
    [SerializeField]
    TMP_Text unlockLabel;

    Action<ProductDefinition, int> _onStep;
    Action<ProductDefinition> _onUnlock;

    /// <summary>
    /// Builds the default row layout on this object and fills the fields above. Used by the built-in screen,
    /// so the scene prefab is not needed.
    /// </summary>
    public void BuildDefault(UIStyle style)
    {
        if (transform is not RectTransform rect)
        {
            Debug.LogError($"{name}: a row needs a RectTransform.", this);
            return;
        }
        var parts = ShopPanelBuilder.BuildRowLayout(rect, style);
        if (parts == null)
            return;
        icon = parts.icon;
        nameLabel = parts.nameLabel;
        detailLabel = parts.detailLabel;
        qtyLabel = parts.qtyLabel;
        plusButton = parts.plusButton;
        minusButton = parts.minusButton;
        unlockButton = parts.unlockButton;
        unlockLabel = parts.unlockLabel;
    }

    public ProductDefinition Definition { get; private set; }

    /// <summary>Set once. onStep gets +1 or -1 box. onUnlock (optional) is called by the unlock button.</summary>
    public void Bind(
        ProductDefinition def,
        Action<ProductDefinition, int> onStep,
        Action<ProductDefinition> onUnlock = null
    )
    {
        if (
            def == null
            || nameLabel == null
            || detailLabel == null
            || qtyLabel == null
            || plusButton == null
            || minusButton == null
        )
        {
            Debug.LogError($"{name}: row references or definition missing.", this);
            return;
        }

        Definition = def;
        _onStep = onStep;
        _onUnlock = onUnlock;
        nameLabel.text = def.DisplayName;
        if (icon != null)
        {
            icon.sprite = def.Icon;
            icon.enabled = def.Icon != null;
        }
        plusButton.onClick.AddListener(() => _onStep?.Invoke(Definition, 1));
        minusButton.onClick.AddListener(() => _onStep?.Invoke(Definition, -1));
        if (unlockButton != null)
        {
            unlockButton.onClick.AddListener(() => _onUnlock?.Invoke(Definition));
            unlockButton.gameObject.SetActive(false); // shown by SetLocked
        }
    }

    public void SetDetail(string text)
    {
        if (detailLabel != null)
            detailLabel.text = text;
    }

    /// <summary>
    /// Locked rows can't add boxes and show the unlock button (greyed out if canAfford is false).
    /// </summary>
    public void SetLocked(bool locked, int cost, bool canAfford)
    {
        if (plusButton != null)
            plusButton.interactable = !locked;
        if (unlockButton == null)
            return;
        unlockButton.gameObject.SetActive(locked);
        unlockButton.interactable = canAfford;
        if (unlockLabel != null)
            unlockLabel.text = $"Unlock: {cost}";
    }

    public void SetBoxes(int boxes)
    {
        if (qtyLabel != null)
            qtyLabel.text = boxes.ToString();
        if (minusButton != null)
            minusButton.interactable = boxes > 0;
    }

    void OnDestroy()
    {
        if (plusButton != null)
            plusButton.onClick.RemoveAllListeners();
        if (minusButton != null)
            minusButton.onClick.RemoveAllListeners();
        if (unlockButton != null)
            unlockButton.onClick.RemoveAllListeners();
        _onStep = null;
        _onUnlock = null;
    }
}
