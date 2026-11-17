using System;
using System.Collections.Generic;
using UnityEngine;

//Save Data
[Serializable]
public class UpgradeLevelEntry
{
    public string id; // UpgradeDefinition.Id
    public int level;
}

[Serializable]
public class ProgressionData
{
    public bool saved; // false = nothing saved: progression starts from scratch
    public string[] unlockedProducts; // ProductDefinition.Id of each bought unlock
    public string[] expansions; // ExpansionDefinition.Id of each bought expansion
    public string[] unlockedFurniture; // FurnitureDefinition.Id of each bought furniture unlock
    public UpgradeLevelEntry[] upgrades; // only upgrades above level 0
}

/// <summary>
/// What the player has bought with coins: product unlocks, store expansions and upgrades. This is the
/// only place that spends coins for progression and changes this state, so screens just call the Try*
/// methods and listen to Changed. Without a ProgressionManager in the scene every product counts as
/// unlocked and every multiplier is 1 (use the static IsUnlocked and MultiplierFor).
/// </summary>
public class ProgressionManager : MonoBehaviour
{
    public static ProgressionManager Instance;

    [Tooltip("Every product. Used to match saved unlocks by id.")]
    [SerializeField]
    private ProductCatalog catalog;

    [Tooltip("All upgrades the player can buy. Order doesn't matter.")]
    [SerializeField]
    private UpgradeDefinition[] upgrades = Array.Empty<UpgradeDefinition>();

    [Tooltip("All store expansions the player can buy. Order doesn't matter.")]
    [SerializeField]
    private ExpansionDefinition[] expansions = Array.Empty<ExpansionDefinition>();

    [Tooltip(
        "Every piece of furniture. Used to match saved furniture unlocks by id. Empty = furniture can't be unlocked."
    )]
    [SerializeField]
    private FurnitureCatalog furnitureCatalog;

    private readonly HashSet<string> _unlocked = new();
    private readonly HashSet<string> _bought = new();
    private readonly HashSet<string> _furnitureUnlocked = new();
    private readonly Dictionary<string, int> _levels = new();

    /// <summary>
    /// Raised after any purchase and after RestoreState. Listeners should apply the new state
    /// immediately (not on a later frame): a load needs expansions active before the world restores.
    /// </summary>
    public event Action Changed;

    /// <summary>Every upgrade that can be bought (for the progression screen).</summary>
    public IReadOnlyList<UpgradeDefinition> Upgrades => upgrades;

    /// <summary>Every expansion that can be bought (for the progression screen).</summary>
    public IReadOnlyList<ExpansionDefinition> Expansions => expansions;

    private void Awake()
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

        Instance = this; // not DontDestroyOnLoad: it points at scene objects
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ---- Safe lookups for systems that must work with or without a ProgressionManager ----

    /// <summary>True if the product can be ordered. Always true when there is no ProgressionManager.</summary>
    public static bool IsUnlocked(ProductDefinition def) =>
        def != null && (Instance == null || Instance.IsProductUnlocked(def));

    /// <summary>True if the furniture can be ordered (its unlock is paid). Always true when there is no ProgressionManager.</summary>
    public static bool IsUnlocked(FurnitureDefinition def) =>
        def != null && (Instance == null || Instance.IsFurnitureUnlocked(def));

    /// <summary>The combined multiplier of this kind. Always 1 when there is no ProgressionManager.</summary>
    public static float MultiplierFor(UpgradeKind kind) =>
        Instance != null ? Instance.GetMultiplier(kind) : 1f;

    // ---- Product unlocks ----

    /// <summary>Unlock cost 0 means unlocked from the start.</summary>
    public bool IsProductUnlocked(ProductDefinition def) =>
        def != null && (def.UnlockCost <= 0 || _unlocked.Contains(def.Id));

    /// <summary>Pays the unlock cost. False (nothing spent) if already unlocked, unknown, or too expensive.</summary>
    public bool TryUnlockProduct(ProductDefinition def)
    {
        if (
            !isActiveAndEnabled
            || def == null
            || catalog.IndexOf(def) < 0
            || IsProductUnlocked(def)
        )
            return false;
        if (!TrySpend(def.UnlockCost))
            return false;

        _unlocked.Add(def.Id);
        Changed?.Invoke();
        return true;
    }

    // ---- Furniture unlocks ----

    /// <summary>Unlock cost 0 means unlocked from the start.</summary>
    public bool IsFurnitureUnlocked(FurnitureDefinition def) =>
        def != null && (def.UnlockCost <= 0 || _furnitureUnlocked.Contains(def.Id));

    /// <summary>True if the piece needs no expansion, or the one it needs is bought.</summary>
    public bool IsFurnitureRequirementMet(FurnitureDefinition def) =>
        def != null && (def.RequiredExpansion == null || IsExpansionBought(def.RequiredExpansion));

    /// <summary>
    /// Pays the unlock cost. False (nothing spent) if already unlocked, unknown, missing its
    /// expansion, or too expensive.
    /// </summary>
    public bool TryUnlockFurniture(FurnitureDefinition def)
    {
        if (
            !isActiveAndEnabled
            || def == null
            || furnitureCatalog == null
            || furnitureCatalog.IndexOf(def) < 0
            || IsFurnitureUnlocked(def)
            || !IsFurnitureRequirementMet(def)
            || !TrySpend(def.UnlockCost)
        )
            return false;

        _furnitureUnlocked.Add(def.Id);
        Changed?.Invoke();
        return true;
    }

    // ---- Upgrades ----

    public int GetUpgradeLevel(UpgradeDefinition def) =>
        def != null && _levels.TryGetValue(def.Id, out int level) ? level : 0;

    public bool IsMaxLevel(UpgradeDefinition def) =>
        def != null && GetUpgradeLevel(def) >= def.LevelCount;

    /// <summary>Coins for the next level. False if the upgrade is at its top level.</summary>
    public bool TryGetNextUpgradeCost(UpgradeDefinition def, out int cost)
    {
        cost = 0;
        return def != null && def.TryGetCost(GetUpgradeLevel(def) + 1, out cost);
    }

    /// <summary>Buys the next level. False (nothing spent) if unknown, maxed out, or too expensive.</summary>
    public bool TryBuyUpgrade(UpgradeDefinition def)
    {
        if (
            !isActiveAndEnabled
            || def == null
            || !IsListed(def)
            || !TryGetNextUpgradeCost(def, out int cost)
            || !TrySpend(cost)
        )
            return false;

        _levels[def.Id] = GetUpgradeLevel(def) + 1;
        Changed?.Invoke();
        return true;
    }

    /// <summary>All owned upgrades of this kind multiplied together (1 = none).</summary>
    public float GetMultiplier(UpgradeKind kind)
    {
        float m = 1f;
        foreach (var u in upgrades)
            if (u != null && u.Kind == kind)
                m *= u.MultiplierAt(GetUpgradeLevel(u));
        return m;
    }

    // ---- Expansions ----

    public bool IsExpansionBought(ExpansionDefinition def) =>
        def != null && _bought.Contains(def.Id);

    /// <summary>Buys the expansion. False (nothing spent) if unknown, already bought, or too expensive.</summary>
    public bool TryBuyExpansion(ExpansionDefinition def)
    {
        if (
            !isActiveAndEnabled
            || def == null
            || !IsListed(def)
            || IsExpansionBought(def)
            || !TrySpend(def.Cost)
        )
            return false;

        _bought.Add(def.Id);
        Changed?.Invoke();
        return true;
    }

    // ---- Save and load ----

    public ProgressionData CaptureState()
    {
        var entries = new List<UpgradeLevelEntry>();
        foreach (var pair in _levels)
            if (pair.Value > 0)
                entries.Add(new UpgradeLevelEntry { id = pair.Key, level = pair.Value });
        entries.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

        return new ProgressionData
        {
            saved = true,
            unlockedProducts = Sorted(_unlocked),
            expansions = Sorted(_bought),
            unlockedFurniture = Sorted(_furnitureUnlocked),
            upgrades = entries.ToArray(),
        };
    }

    /// <summary>
    /// Replaces everything. Null or a save with no progression resets to the start. Unknown ids are
    /// skipped with a warning, as in PriceBook.RestoreState, and levels are clamped.
    /// </summary>
    public void RestoreState(ProgressionData state)
    {
        _unlocked.Clear();
        _bought.Clear();
        _furnitureUnlocked.Clear();
        _levels.Clear();

        if (state != null && state.saved)
        {
            if (state.unlockedProducts != null)
                foreach (var id in state.unlockedProducts)
                {
                    if (catalog.TryGetById(id, out var def))
                        _unlocked.Add(def.Id);
                    else
                        Debug.LogWarning(
                            $"{name}: saved unlock '{id}' is not in the catalog, skipped.",
                            this
                        );
                }

            if (state.expansions != null)
                foreach (var id in state.expansions)
                {
                    var e = FindExpansion(id);
                    if (e != null)
                        _bought.Add(e.Id);
                    else
                        Debug.LogWarning(
                            $"{name}: saved expansion '{id}' no longer exists, skipped.",
                            this
                        );
                }

            if (state.unlockedFurniture != null)
                foreach (var id in state.unlockedFurniture)
                {
                    if (furnitureCatalog != null && furnitureCatalog.TryGetById(id, out var piece))
                        _furnitureUnlocked.Add(piece.Id);
                    else
                        Debug.LogWarning(
                            $"{name}: saved furniture unlock '{id}' is not in the furniture catalog, skipped.",
                            this
                        );
                }

            if (state.upgrades != null)
                foreach (var entry in state.upgrades)
                {
                    var u = entry != null ? FindUpgrade(entry.id) : null;
                    if (u == null)
                    {
                        Debug.LogWarning(
                            $"{name}: saved upgrade '{(entry != null ? entry.id : null)}' no longer exists, skipped.",
                            this
                        );
                        continue;
                    }
                    int level = Mathf.Clamp(entry.level, 0, u.LevelCount);
                    if (level > 0)
                        _levels[u.Id] = level;
                }
        }

        Changed?.Invoke();
    }

    // ---- Helpers ----

    // Spends first, so a failed purchase changes nothing.
    private static bool TrySpend(int cost)
    {
        var wallet = CurrencyManager.Instance;
        return wallet != null && wallet.TrySpendCoins(cost); // refuses a cost under 1
    }

    private bool IsListed(UpgradeDefinition def)
    {
        foreach (var u in upgrades)
            if (u == def)
                return true;
        return false;
    }

    private bool IsListed(ExpansionDefinition def)
    {
        foreach (var e in expansions)
            if (e == def)
                return true;
        return false;
    }

    private UpgradeDefinition FindUpgrade(string id)
    {
        foreach (var u in upgrades)
            if (u != null && u.Id == id)
                return u;
        return null;
    }

    private ExpansionDefinition FindExpansion(string id)
    {
        foreach (var e in expansions)
            if (e != null && e.Id == id)
                return e;
        return null;
    }

    private static string[] Sorted(HashSet<string> set)
    {
        var array = new string[set.Count];
        set.CopyTo(array);
        Array.Sort(array, StringComparer.Ordinal);
        return array;
    }

    [ContextMenu("Debug: save/restore round trip")]
    private void DebugRoundTrip()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning($"{name}: Play mode only.", this);
            return;
        }
        string json = JsonUtility.ToJson(CaptureState());
        RestoreState(JsonUtility.FromJson<ProgressionData>(json));
        Debug.Log($"{name}: restored from {json}", this);
    }

    [ContextMenu("Debug: reset progression")]
    private void DebugReset()
    {
        if (Application.isPlaying)
            RestoreState(null);
    }

    private void OnValidate()
    {
        var ids = new HashSet<string>();
        foreach (var u in upgrades)
            if (u != null && !ids.Add(u.Id))
                Debug.LogWarning($"{name}: duplicate upgrade id '{u.Id}'.", this);

        ids.Clear();
        foreach (var e in expansions)
            if (e != null && !ids.Add(e.Id))
                Debug.LogWarning($"{name}: duplicate expansion id '{e.Id}'.", this);
    }
}
