using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Every piece of furniture the shop can sell. Saves match furniture by id, never by order.</summary>
[CreateAssetMenu(fileName = "FurnitureCatalog", menuName = "Shop/Furniture Catalog")]
public class FurnitureCatalog : ScriptableObject
{
    [SerializeField]
    FurnitureDefinition[] items = Array.Empty<FurnitureDefinition>();

    [NonSerialized]
    Dictionary<string, FurnitureDefinition> _byId;

    public int Count => items != null ? items.Length : 0;

    public FurnitureDefinition Get(int index) => index >= 0 && index < Count ? items[index] : null;

    /// <summary>Position of this definition in the catalog, or -1 if it is not listed.</summary>
    public int IndexOf(FurnitureDefinition def)
    {
        if (def == null)
            return -1;
        for (int i = 0; i < Count; i++)
            if (items[i] == def)
                return i;
        return -1;
    }

    /// <summary>The furniture with this id. False if it is not in the catalog.</summary>
    public bool TryGetById(string id, out FurnitureDefinition def)
    {
        def = null;
        if (string.IsNullOrEmpty(id))
            return false;
        if (_byId == null)
            Build();
        return _byId.TryGetValue(id, out def);
    }

    void OnEnable() => _byId = null; // never trust a cache from a previous play session

    void Build()
    {
        _byId = new Dictionary<string, FurnitureDefinition>(Count);
        for (int i = 0; i < Count; i++)
        {
            var d = items[i];
            if (d == null)
                continue;
            if (!_byId.TryAdd(d.Id, d))
                Debug.LogError($"{name}: furniture id '{d.Id}' is listed twice (entry {i}).", this);
        }
    }

    void OnValidate()
    {
        _byId = null;
        var ids = new HashSet<string>();
        foreach (var d in items)
            if (d != null && !ids.Add(d.Id))
                Debug.LogWarning($"{name}: duplicate furniture id '{d.Id}'.", this);
    }
}
