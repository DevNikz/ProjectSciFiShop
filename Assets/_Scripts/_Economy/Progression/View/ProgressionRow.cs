using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One upgrade or expansion line on the progression screen.</summary>
public class ProgressionRow : MonoBehaviour
{
    [Tooltip("Optional. 'Upgrade' or 'Expansion'.")]
    [SerializeField]
    TMP_Text typeLabel;

    [SerializeField]
    TMP_Text nameLabel;

    [Tooltip("Optional. Hidden when the definition has no description.")]
    [SerializeField]
    TMP_Text descriptionLabel;

    [Tooltip("Level, or owned / not bought.")]
    [SerializeField]
    TMP_Text statusLabel;

    [SerializeField]
    Button buyButton;

    [Tooltip("Optional. The cost text, e.g. on the buy button.")]
    [SerializeField]
    TMP_Text buyLabel;

    Action _onBuy;

    /// <summary>Set once. onBuy is called by the buy button.</summary>
    public void Bind(string type, string title, string description, Action onBuy)
    {
        if (nameLabel == null || statusLabel == null || buyButton == null)
        {
            Debug.LogError($"{name}: name label, status label or buy button missing.", this);
            return;
        }

        _onBuy = onBuy;
        if (typeLabel != null)
            typeLabel.text = type;
        nameLabel.text = title;
        if (descriptionLabel != null)
        {
            descriptionLabel.text = description;
            descriptionLabel.gameObject.SetActive(!string.IsNullOrEmpty(description));
        }
        buyButton.onClick.AddListener(HandleClick);
    }

    /// <summary>The button is hidden when showBuy is false (owned or maxed out) and greyed out if !canBuy.</summary>
    public void SetState(string status, bool showBuy, string buyText, bool canBuy)
    {
        if (statusLabel == null || buyButton == null)
            return;
        statusLabel.text = status;
        buyButton.gameObject.SetActive(showBuy);
        buyButton.interactable = canBuy;
        if (buyLabel != null)
            buyLabel.text = buyText;
    }

    void HandleClick() => _onBuy?.Invoke();

    void OnDestroy()
    {
        if (buyButton != null)
            buyButton.onClick.RemoveListener(HandleClick);
        _onBuy = null;
    }
}
