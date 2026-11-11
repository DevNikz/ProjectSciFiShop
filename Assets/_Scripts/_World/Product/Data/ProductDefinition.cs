using UnityEngine;

public enum ProductCategory
{
    Other,
    Produce,
    Dairy,
    Drinks,
    Snacks,
    Household,
}

/// <summary>
/// Read-only authored data for one kind of product.
/// Writable values (sell price etc.) live in runtime state, never here.
/// </summary>
[CreateAssetMenu(fileName = "Product", menuName = "Shop/Product Definition")]
public class ProductDefinition : ScriptableObject
{
    [Tooltip("Stable unique id used by saves. Never change after release. Empty = asset name.")]
    [SerializeField]
    string id;

    [Tooltip("Shown on the hotbar and scanner. Empty = asset name.")]
    [SerializeField]
    string displayName;

    [Tooltip("Hotbar and scanner icon. Empty = shows Display Name instead.")]
    [SerializeField]
    Sprite icon;

    [SerializeField]
    ProductCategory category = ProductCategory.Other;

    [Tooltip("Starting market price (coins, min 1).")]
    [SerializeField]
    int baseMarketPrice = 1;

    [Tooltip("What the shop pays per unit when ordering (coins, min 1).")]
    [SerializeField]
    int unitCost = 1;

    [Tooltip("Units in one delivery box (min 1). Check it fits with the box's Fill test.")]
    [SerializeField]
    int unitsPerBox = 1;

    [Tooltip("Coins to unlock this product for ordering. 0 = available from the start.")]
    [SerializeField]
    int unlockCost;

    [Tooltip("Prefab spawned for each unit. Its ProductPickup must use this definition.")]
    [SerializeField]
    ProductCarriable prefab;

    [Tooltip("Instances created at scene start. 0 = grow on demand.")]
    [SerializeField, Min(0)]
    int prewarmCount;

    public Carriable CarriablePrefab => prefab.Carriable;
    public int PrewarmCount => prewarmCount;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public ProductCategory Category => category;
    public int BaseMarketPrice => baseMarketPrice;
    public int UnitCost => unitCost;
    public int UnitsPerBox => unitsPerBox;
    public int UnlockCost => unlockCost;
    public ProductCarriable Prefab => prefab;

    void OnValidate()
    {
        baseMarketPrice = Mathf.Max(1, baseMarketPrice);
        unitCost = Mathf.Max(1, unitCost);
        unitsPerBox = Mathf.Max(1, unitsPerBox);
        unlockCost = Mathf.Max(0, unlockCost);
        if (unitCost >= baseMarketPrice)
            Debug.LogWarning($"{name}: unit cost is not below the market price (no margin).", this);
        if (prefab != null && prefab.Definition != this)
            Debug.LogWarning($"{name}: prefab '{prefab.name}' uses a different definition.", this);
        if (string.IsNullOrEmpty(id)) // lock an id in once, so renaming the asset can't break saves
            id = name;
    }
}
