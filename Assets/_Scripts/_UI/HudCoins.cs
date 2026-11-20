using DG.Tweening;
using TMPro;
using UnityEngine;

public class HudCoins : MonoBehaviour
{
    private TextMeshProUGUI coinText;
    private RectTransform poolTransform;
    private int displayedCoins;

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

    private void OnEnable() => CurrencyManager.Instance.OnCoinsChanged += OnCoinsChanged;

    private void OnDestroy() => CurrencyManager.Instance.OnCoinsChanged -= OnCoinsChanged;

    private void OnCoinsChanged(int coins)
    {
        int startValue = displayedCoins;
        int targetValue = CurrencyManager.Instance.GetCoins();

        CoinUIPool.Instance.SpawnAtUIElement(poolTransform, coins);

        _countTween?.Kill();
        float t = 0f;

        _countTween = DOTween
            .To(() => t, v => t = v, 1f, countDuration)
            .OnUpdate(() =>
            {
                displayedCoins = startValue + (int)((targetValue - startValue) * t);
                RefreshTextInstant();
            })
            .OnComplete(() =>
            {
                displayedCoins = targetValue; // snap to exact value
                RefreshTextInstant();
            });
    }

    private void RefreshTextInstant()
    {
        if (coinText != null)
            coinText.text = displayedCoins.ToString("N0") + "$";
    }
}
