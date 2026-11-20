using System;
using System.Collections.Generic;
using UnityEngine;

//Runtime state for one product. Definitions stay read-only; only this changes.
[Serializable]
public struct ProductState
{
    public int sellPrice; // what customers pay (player-set)
    public int marketPrice; // reference price (day system sets it later)
}

//Save Data
[Serializable]
public class ProductPriceEntry
{
    public string id;
    public int sellPrice;
    public int marketPrice;
}

[Serializable]
public class PriceBookData
{
    public ProductPriceEntry[] entries;
}

[DefaultExecutionOrder(-100)]
public class PriceBook : MonoBehaviour
{
    public static PriceBook Instance;

    [Tooltip("Every product. Sell and market prices start at each Base Market Price.")]
    [SerializeField]
    private ProductCatalog catalog;

    private ProductState[] _states = Array.Empty<ProductState>();

    public event Action<ProductDefinition> PriceChanged; // sell or market price changed

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (catalog == null)
        {
            Debug.LogError($"{name}: ProductCatalog missing.", this);
            enabled = false;
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _states = new ProductState[catalog.Count];
        for (int i = 0; i < _states.Length; i++)
        {
            var def = catalog.Get(i);
            int price = def != null ? def.BaseMarketPrice : 1;
            _states[i] = new ProductState { sellPrice = price, marketPrice = price };
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Customer-facing price in coins. 0 = unknown product.
    /// ONLY CALL THIS TO GET SELL PRICE OF PRODUCT DATA!!
    /// </summary>
    public int GetSellPrice(ProductDefinition def) => TryGetState(def, out var s) ? s.sellPrice : 0;

    /// <summary>Reference price in coins. 0 = unknown product.</summary>
    public int GetMarketPrice(ProductDefinition def) =>
        TryGetState(def, out var s) ? s.marketPrice : 0;

    /// <summary>Copy of the product's state. False if it is not in the catalog.</summary>
    public bool TryGetState(ProductDefinition def, out ProductState state)
    {
        int i = IndexOf(def);
        if (i < 0)
        {
            state = default;
            return false;
        }
        state = _states[i];
        return true;
    }

    /// <summary>Set the sell price (min 1). False if unknown or invalid.</summary>
    ///
    public bool TrySetSellPrice(ProductDefinition def, int price)
    {
        int i = IndexOf(def);
        if (i < 0 || price < 1)
            return false;
        if (_states[i].sellPrice == price)
            return true; // nothing to do

        _states[i].sellPrice = price;
        PriceChanged?.Invoke(def);
        return true;
    }

    /// <summary>Set the market price (min 1). Does not touch the sell price.</summary>
    public void SetMarketPrice(ProductDefinition def, int price)
    {
        int i = IndexOf(def);
        price = Math.Max(1, price);
        if (i < 0 || _states[i].marketPrice == price)
            return;

        _states[i].marketPrice = price;
        PriceChanged?.Invoke(def);
    }

    public PriceBookData CaptureState()
    {
        var entries = new ProductPriceEntry[_states.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            var def = catalog.Get(i);
            entries[i] = new ProductPriceEntry
            {
                id = def != null ? def.Id : null,
                sellPrice = _states[i].sellPrice,
                marketPrice = _states[i].marketPrice,
            };
        }
        return new PriceBookData { entries = entries };
    }

    public void RestoreState(PriceBookData state)
    {
        if (state == null || state.entries == null || catalog == null)
            return;

        // Match by id, so catalog reorders/removals can't scramble prices.
        var byId = new Dictionary<string, int>(_states.Length);
        for (int i = 0; i < _states.Length; i++)
        {
            var def = catalog.Get(i);
            if (def != null)
                byId[def.Id] = i;
        }

        foreach (var e in state.entries)
        {
            if (e == null || e.id == null || !byId.TryGetValue(e.id, out int i))
                continue; // product no longer exists: skip it

            _states[i].sellPrice = Math.Max(1, e.sellPrice);
            _states[i].marketPrice = Math.Max(1, e.marketPrice);
            PriceChanged?.Invoke(catalog.Get(i));
        }
    }

    int IndexOf(ProductDefinition def)
    {
        int i = catalog != null ? catalog.IndexOf(def) : -1;
        if (i < 0 || i >= _states.Length)
        {
            Debug.LogWarning(
                $"{name}: '{(def != null ? def.name : "null")}' is not in the catalog.",
                this
            );
            return -1;
        }
        return i;
    }
}
