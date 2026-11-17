using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One furniture line on the furniture screen. Same layout as an order row: duplicate that prefab and swap the component.</summary>
public class FurnitureRow : MonoBehaviour
{
    [SerializeField]
    Image icon;

    [SerializeField]
    TMP_Text nameLabel;

    [Tooltip("Price, refund or why it is locked.")]
    [SerializeField]
    TMP_Text detailLabel;

    [Tooltip("Pieces in the cart.")]
    [SerializeField]
    TMP_Text qtyLabel;

    [SerializeField]
    Button plusButton;

    [SerializeField]
    Button minusButton;

    [Tooltip("Optional. Shown on locked furniture that can be unlocked; buys the unlock.")]
    [SerializeField]
    Button unlockButton;

    [Tooltip("Optional. Shows the unlock cost (put it on the unlock button).")]
    [SerializeField]
    TMP_Text unlockLabel;

    Action<FurnitureDefinition, int> _onStep;
    Action<FurnitureDefinition> _onUnlock;
    bool _locked;
    int _count;

    public FurnitureDefinition Definition { get; private set; }

    /// <summary>Set once. onStep gets +1 or -1 piece. onUnlock (optional) is called by the unlock button.</summary>
    public void Bind(
        FurnitureDefinition def,
        Action<FurnitureDefinition, int> onStep,
        Action<FurnitureDefinition> onUnlock = null
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
    /// Locked rows can't add pieces. The unlock button shows only if canUnlock (no missing expansion)
    /// and is greyed out if canAfford is false.
    /// </summary>
    public void SetLocked(bool locked, bool canUnlock, int cost, bool canAfford)
    {
        _locked = locked;
        RefreshButtons();
        if (unlockButton == null)
            return;
        unlockButton.gameObject.SetActive(locked && canUnlock);
        unlockButton.interactable = canAfford;
        if (unlockLabel != null)
            unlockLabel.text = $"Unlock: {cost}";
    }

    public void SetCount(int count)
    {
        _count = count;
        if (qtyLabel != null)
            qtyLabel.text = count.ToString();
        RefreshButtons();
    }

    void RefreshButtons()
    {
        if (plusButton != null)
            plusButton.interactable = !_locked && _count < FurnitureCart.MaxPerLine;
        if (minusButton != null)
            minusButton.interactable = _count > 0;
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
