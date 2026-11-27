using DG.Tweening;
using TMPro;
using UnityEngine;

public class HudCoins : MonoBehaviour
{
    private TextMeshProUGUI coinText;
    private RectTransform poolTransform;
    private int displayedCoins;
    private int _lastTotal;

    //Tweening
    [Header("Tween")]
    [SerializeField]
    private float countDuration = 0.4f;
    private Tween _countTween;

    void Awake()
    {
        if (coinText == null)
            coinText = transform.GetComponentInChildren<TextMeshProUGUI>();
        if (poolTransform == null)
            poolTransform = transform.Find("Pool").GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        var wallet = CurrencyManager.Instance;
        if (wallet == null)
        {
            Debug.LogError($"{name}: CurrencyManager missing.", this);
            return;
        }
        _lastTotal = displayedCoins = wallet.GetCoins();
        RefreshTextInstant();
        wallet.OnCoinsChanged += OnCoinsChanged;
    }

    private void OnDisable()
    {
        _countTween?.Kill();
        var wallet = CurrencyManager.Instance; // null only while quitting
        if (wallet != null)
            wallet.OnCoinsChanged -= OnCoinsChanged;
    }

    // The event passes the new total. The popup shows how much it changed by.
    private void OnCoinsChanged(int total)
    {
        int startValue = displayedCoins;
        int delta = total - _lastTotal;
        _lastTotal = total;

        _countTween?.Kill();
        float t = 0f;

        // Tween first: a popup problem must never leave the label stale.
        _countTween = DOTween
            .To(() => t, v => t = v, 1f, countDuration)
            .OnUpdate(() =>
            {
                displayedCoins = startValue + (int)((total - startValue) * t);
                RefreshTextInstant();
            })
            .OnComplete(() =>
            {
                displayedCoins = total; // snap to exact value
                RefreshTextInstant();
            });

        if (delta != 0 && CoinUIPool.Instance != null)
            CoinUIPool.Instance.SpawnAtUIElement(poolTransform, delta);
    }

    private void RefreshTextInstant()
    {
        if (coinText != null)
            coinText.text = displayedCoins.ToString("N0") + "$";
    }
}
