using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Every product in the game. Order is the index used by runtime state arrays.</summary>
[CreateAssetMenu(fileName = "ProductCatalog", menuName = "Shop/Product Catalog")]
public class ProductCatalog : ScriptableObject
{
    [Tooltip("All products. Index = slot in runtime state. Append only; reordering breaks saves.")]
    [SerializeField]
    ProductDefinition[] products = Array.Empty<ProductDefinition>();

    [NonSerialized]
    Dictionary<ProductDefinition, int> _indices; // built on first lookup

    public int Count => products != null ? products.Length : 0;

    /// <summary>Definition at index i, else null.</summary>
    public ProductDefinition Get(int i) => i >= 0 && i < Count ? products[i] : null;

    /// <summary>Index of def, or -1 if it is null or not in the catalog.</summary>
    public int IndexOf(ProductDefinition def)
    {
        if (def == null)
            return -1;
        if (_indices == null)
            BuildIndices();
        return _indices.TryGetValue(def, out int i) ? i : -1;
    }

    /// <summary>The product with this id (ProductDefinition.Id). False if there is none.</summary>
    public bool TryGetById(string id, out ProductDefinition def)
    {
        def = null;
        if (string.IsNullOrEmpty(id))
            return false;
        for (int i = 0; i < Count; i++)
            if (products[i] != null && products[i].Id == id)
            {
                def = products[i];
                return true;
            }
        return false;
    }

    void OnEnable() => _indices = null; // never trust a cache from a previous play session

    void BuildIndices()
    {
        _indices = new Dictionary<ProductDefinition, int>(Count);
        for (int i = 0; i < Count; i++)
        {
            var def = products[i];
            if (def == null)
            {
                Debug.LogError($"{name}: empty entry at index {i}.", this);
                continue;
            }
            if (!_indices.TryAdd(def, i))
                Debug.LogError($"{name}: '{def.name}' is listed twice (index {i}).", this);
        }
    }

    void OnValidate()
    {
        _indices = null;
        var ids = new HashSet<string>();
        foreach (var def in products)
            if (def != null && !ids.Add(def.Id))
                Debug.LogWarning($"{name}: duplicate product id '{def.Id}'.", this);
    }
}
