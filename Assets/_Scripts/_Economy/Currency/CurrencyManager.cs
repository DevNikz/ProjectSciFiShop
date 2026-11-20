using System;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

//Data Funcs
[Serializable]
public class CurrencyData
{
    public int coins;
    public float coinRate;
}

[DefaultExecutionOrder(-100)]
public class CurrencyManager : MonoBehaviour
{
    public static CurrencyManager Instance;

    [SerializeField]
    private int startingCoins = 0;

    [SerializeField]
    private int Coins;

    public int GetCoins() => Coins;

    [SerializeField]
    private float baseCoinsPerSecond;

    private float rateMultiplier = 1f;

    public float GetCoinsPerSecond() => baseCoinsPerSecond * rateMultiplier;

    public float GetBaseCoinsPerSecond() => baseCoinsPerSecond;

    public float GetRateMultiplier() => rateMultiplier;

    public event Action<int> OnCoinsChanged; // passes the amount gained/lost
    public event Action<float> OnCoinRateChanged;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Coins = startingCoins;
    }

    void Start()
    {
        OnCoinsChanged?.Invoke(Coins);
    }

    public void AddCoins(int amount)
    {
        if (amount == 0)
            return;

        Coins = Math.Max(0, Coins + amount);
        OnCoinsChanged?.Invoke(amount);
    }

    public bool TrySpendCoins(int amount)
    {
        if (amount <= 0 || Coins < amount)
            return false;

        Coins -= amount;
        OnCoinsChanged?.Invoke(-amount);
        return true;
    }

    public void SetCoinRate(float coinsPerSecond)
    {
        if (Mathf.Approximately(baseCoinsPerSecond, coinsPerSecond))
            return;

        baseCoinsPerSecond = coinsPerSecond;
        OnCoinRateChanged?.Invoke(GetCoinsPerSecond());
    }

    public void SetRateMultiplier(float multiplier)
    {
        multiplier = Mathf.Max(0f, multiplier);
        if (Mathf.Approximately(rateMultiplier, multiplier))
            return;

        rateMultiplier = multiplier;
        OnCoinRateChanged?.Invoke(GetCoinsPerSecond());
    }

    public void ClearRateMultiplier() => SetRateMultiplier(1f);

    public CurrencyData CaptureState()
    {
        Debug.Log($"Coins Saved: {Coins}", this);
        return new CurrencyData { coins = Coins, coinRate = baseCoinsPerSecond };
    }

    public void RestoreState(CurrencyData state)
    {
        if (state == null)
            return;

        int previousCoins = Coins;
        Coins = Math.Max(0, state.coins);

        rateMultiplier = 1f;
        baseCoinsPerSecond = state.coinRate;
        OnCoinRateChanged?.Invoke(GetCoinsPerSecond());

        OnCoinsChanged?.Invoke(Coins - previousCoins);
    }

    public int ComputeEarnedCoins(double seconds)
    {
        float rate = GetCoinsPerSecond();
        if (seconds <= 0 || rate <= 0f)
            return 0;

        return (int)(rate * seconds);
    }
}
