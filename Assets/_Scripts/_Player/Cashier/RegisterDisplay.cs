using TMPro;
using UnityEngine;

/// <summary>Shows the Register's state, total, payment, change due and tray total. Works on a world-space or overlay canvas.</summary>
public class RegisterDisplay : MonoBehaviour
{
    [Tooltip("The register to show.")]
    [SerializeField]
    Register register;

    [Header("Labels")]
    [Tooltip("Optional. Waiting / scanning / give change.")]
    [SerializeField]
    TMP_Text stateLabel;

    [SerializeField]
    TMP_Text totalLabel;

    [Header("Payment (only while waiting for change)")]
    [Tooltip("Optional. Shown only while waiting for change. Must be a child, not this object.")]
    [SerializeField]
    GameObject paymentGroup;

    [SerializeField]
    TMP_Text paidLabel;

    [SerializeField]
    TMP_Text changeLabel;

    [Tooltip("Coins in the tray so far.")]
    [SerializeField]
    TMP_Text trayLabel;

    [Header("Colors")]
    [Tooltip("Change label and tray label while the tray is short.")]
    [SerializeField]
    Color32 shortColor = new(0xFF, 0x6B, 0x6B, 0xFF);

    [Tooltip("Same labels once the tray covers the change.")]
    [SerializeField]
    Color32 coveredColor = new(0x3F, 0xB6, 0x8B, 0xFF);

    void OnEnable()
    {
        if (register == null || totalLabel == null || paymentGroup == gameObject)
        {
            Debug.LogError(
                $"{name}: register/totalLabel missing, or payment group is this object.",
                this
            );
            enabled = false;
            return;
        }
        register.Changed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (register != null)
            register.Changed -= Refresh;
    }

    void Refresh()
    {
        var state = register.State;
        bool paying = state == RegisterState.AwaitingChange;

        if (stateLabel != null)
            stateLabel.text = state switch
            {
                RegisterState.Scanning => $"Scanning: {register.ItemCount} items",
                RegisterState.AwaitingChange => "Give change",
                _ => "Waiting for a customer",
            };
        totalLabel.text = state == RegisterState.Idle ? "Total: -" : $"Total: {register.Total}";

        if (paymentGroup != null)
            paymentGroup.SetActive(paying);

        bool covered = register.TrayTotal >= register.ChangeDue;
        Color32 color = covered ? coveredColor : shortColor;
        SetText(paidLabel, paying ? $"Paid: {register.CashPaid}" : "");
        SetText(changeLabel, paying ? $"Change due: {register.ChangeDue}" : "", color);
        SetText(trayLabel, paying ? $"In tray: {register.TrayTotal}" : "", color);
    }

    static void SetText(TMP_Text label, string text) => SetText(label, text, null);

    static void SetText(TMP_Text label, string text, Color32? color)
    {
        if (label == null)
            return;
        label.text = text;
        if (color.HasValue)
            label.color = color.Value;
    }
}
