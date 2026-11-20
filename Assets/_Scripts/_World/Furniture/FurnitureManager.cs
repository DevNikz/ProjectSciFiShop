using System;
using System.Collections.Generic;
using UnityEngine;

//Save Data
[Serializable]
public class PendingFurnitureEntry
{
    public string id; // FurnitureDefinition.Id
    public int count;
    public float remaining; // Fixed Delay seconds left
    public bool waitForDay;
}

[Serializable]
public class QueuedCrateEntry
{
    public string id;
    public int count;
}

[Serializable]
public class FurnitureManagerData
{
    public PendingFurnitureEntry[] pending; // paid, not released yet
    public QueuedCrateEntry[] queued; // released, waiting for a free pad slot, in spawn order
}

/// <summary>
/// Sells furniture and takes it back. A paid order becomes crates on the delivery pad, on the same
/// schedule as product orders (it reads OrderManager's delivery mode and delay). Interacting with a
/// crate starts FurniturePlacer. Needs an OrderManager (for the pad) and a CurrencyManager.
/// </summary>
public class FurnitureManager : MonoBehaviour
{
    public static FurnitureManager Instance;

    [Header("Furniture")]
    [Tooltip("Every piece that can be ordered. Orders with furniture outside it are refused.")]
    [SerializeField]
    private FurnitureCatalog catalog;

    [Tooltip(
        "Crate spawned on the delivery pad for each bought piece. Needs FurnitureCrate; its Prefab Id must be in the World Prefab Catalog."
    )]
    [SerializeField]
    private FurnitureCrate cratePrefab;

    [Tooltip("Share of the price paid back when a piece is sold (0 to 100).")]
    [SerializeField, Range(0, 100)]
    private int refundPercent = 50;

    [Tooltip(
        "When furniture can be placed or moved. Needs a DayManager in the scene: without one, placing is never locked."
    )]
    [SerializeField]
    private OrderingWindow placementWindow = OrderingWindow.Always;

    [Tooltip("Seconds between spawned crates (also the retry time when the pad is full).")]
    [SerializeField]
    private float spawnInterval = 0.5f;

    [Header("Debug")]
    [Tooltip("Log when an order is paid, when crates spawn, and why the pad is blocked.")]
    [SerializeField]
    private bool logDeliveries = true;

    [Tooltip("Furniture for the 'Debug: order one' context menu (Play mode).")]
    [SerializeField]
    private FurnitureDefinition testFurniture;

    private struct Pending
    {
        public FurnitureDefinition def;
        public int count;
        public float remaining; // Fixed Delay only
        public bool waitForDay; // Day Cycle only
    }

    private readonly List<Pending> _pending = new();
    private readonly Queue<FurnitureDefinition> _spawnQueue = new();
    private float _spawnTimer;
    private float _blockedFor;
    private const float BlockedReportSeconds = 5f;

    /// <summary>Raised after a paid order is accepted (total coins, total pieces).</summary>
    public event Action<int, int> OrderPlaced;

    /// <summary>Raised after a piece is sold (the piece, coins paid back).</summary>
    public event Action<FurnitureDefinition, int> Sold;

    /// <summary>Orderable furniture (the furniture screen lists these).</summary>
    public FurnitureCatalog Catalog => catalog;

    /// <summary>Crates paid for that have not appeared yet.</summary>
    public int PendingCrates
    {
        get
        {
            int n = _spawnQueue.Count;
            foreach (var p in _pending)
                n += p.count;
            return n;
        }
    }

    /// <summary>Why furniture can't be placed or moved right now, or null if it can.</summary>
    public string PlacementLockReason
    {
        get
        {
            var day = DayManager.Instance;
            if (day == null)
                return null;
            return placementWindow switch
            {
                OrderingWindow.OnlyWhenOpen when !day.IsOpen =>
                    "Furniture can only be placed while the store is open.",
                OrderingWindow.OnlyWhenClosed when day.IsOpen =>
                    "Furniture can only be placed while the store is closed.",
                _ => null,
            };
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (catalog == null || cratePrefab == null)
        {
            Debug.LogError($"{name}: catalog or cratePrefab missing.", this);
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

    // ---- Rules the screens and the placer read ----

    /// <summary>The expansion this piece still needs, as a message, or null if there is none or it is bought.</summary>
    public string GetRequirementReason(FurnitureDefinition def)
    {
        var progression = ProgressionManager.Instance;
        if (
            def == null
            || def.RequiredExpansion == null
            || progression == null
            || progression.IsFurnitureRequirementMet(def)
        )
            return null;
        return $"Requires {def.RequiredExpansion.name}";
    }

    /// <summary>Why this piece can't be ordered right now (requirement or unlock), or null if it can.</summary>
    public string GetBuyLockReason(FurnitureDefinition def)
    {
        if (def == null)
            return "Unknown furniture.";
        string requirement = GetRequirementReason(def);
        if (requirement != null)
            return requirement;
        return ProgressionManager.IsUnlocked(def) ? null : "Locked";
    }

    /// <summary>Coins paid back when this piece is sold.</summary>
    public int GetRefund(FurnitureDefinition def) =>
        def == null ? 0 : Mathf.RoundToInt(def.Price * refundPercent / 100f);

    // ---- Buying ----

    /// <summary>
    /// Pays for the cart and queues the crates. False (nothing spent) if the cart is empty, a piece
    /// is not orderable or locked, or the player can't afford it. Does not clear the cart.
    /// </summary>
    public bool TryPlaceOrder(FurnitureCart cart)
    {
        if (!isActiveAndEnabled || cart == null || cart.IsEmpty)
            return false;

        var orders = OrderManager.Instance;
        if (orders == null)
        {
            Debug.LogWarning($"{name}: no OrderManager, so there is no delivery pad.", this);
            return false;
        }

        foreach (var line in cart.Lines)
        {
            var def = line.definition;
            if (def == null || catalog.IndexOf(def) < 0)
            {
                Debug.LogWarning($"{name}: furniture is not in the catalog.", this);
                return false;
            }
            string lockReason = GetBuyLockReason(def);
            if (lockReason != null)
            {
                if (logDeliveries)
                    Debug.Log($"{name}: order refused. '{def.name}': {lockReason}", this);
                return false;
            }
            if (def.Prefab == null)
            {
                Debug.LogError($"{name}: '{def.name}' has no prefab.", this);
                return false;
            }
        }

        var wallet = CurrencyManager.Instance;
        int total = cart.TotalCost();
        if (wallet == null || total < 1 || !wallet.TrySpendCoins(total))
            return false;

        // Upgrades apply to orders placed from now on; orders already waiting keep their time.
        var mode = orders.Mode;
        float delay = orders.DeliveryDelay;
        foreach (var line in cart.Lines)
        {
            var p = new Pending
            {
                def = line.definition,
                count = line.count,
                remaining = mode == DeliveryMode.FixedDelay ? delay : 0f,
                waitForDay = mode == DeliveryMode.DayCycle,
            };
            if (mode == DeliveryMode.Instant)
                Enqueue(p);
            else
                _pending.Add(p);
        }

        if (logDeliveries)
            Debug.Log(
                $"{name}: paid {total} coins for {cart.TotalCount()} piece(s). "
                    + mode switch
                    {
                        DeliveryMode.FixedDelay => $"First crate in {delay:0.#}s.",
                        DeliveryMode.DayCycle => "Waiting for the next day.",
                        _ => "Spawning now.",
                    },
                this
            );
        OrderPlaced?.Invoke(total, cart.TotalCount());
        return true;
    }

    /// <summary>Day Cycle mode: OrderManager.NotifyDayStarted calls this to release the waiting orders.</summary>
    public void NotifyDayStarted()
    {
        for (int i = 0; i < _pending.Count; i++)
        {
            if (!_pending[i].waitForDay)
                continue;
            Enqueue(_pending[i]);
            _pending.RemoveAt(i);
            i--;
        }
    }

    // ---- Selling ----

    /// <summary>Pays the refund and removes the piece. False if it can't be sold.</summary>
    public bool TrySell(FurnitureCarriable placed)
    {
        if (!isActiveAndEnabled || placed == null || placed.Definition == null)
            return false;
        var wallet = CurrencyManager.Instance;
        if (wallet == null)
            return false;

        var def = placed.Definition;
        int refund = GetRefund(def);
        if (refund > 0)
            wallet.AddCoins(refund);

        placed.gameObject.SetActive(false); // leaves the saved world right away
        Destroy(placed.gameObject);
        Sold?.Invoke(def, refund);
        return true;
    }

    // ---- Save and load ----

    /// <summary>Paid-for furniture that has not appeared yet. Crates already on the pad are saved with the world.</summary>
    public FurnitureManagerData CaptureState()
    {
        var pending = new PendingFurnitureEntry[_pending.Count];
        for (int i = 0; i < pending.Length; i++)
            pending[i] = new PendingFurnitureEntry
            {
                id = _pending[i].def.Id,
                count = _pending[i].count,
                remaining = _pending[i].remaining,
                waitForDay = _pending[i].waitForDay,
            };

        var queued = new List<QueuedCrateEntry>(); // one entry per run of the same piece
        foreach (var def in _spawnQueue)
        {
            if (queued.Count > 0 && queued[^1].id == def.Id)
                queued[^1].count++;
            else
                queued.Add(new QueuedCrateEntry { id = def.Id, count = 1 });
        }
        return new FurnitureManagerData { pending = pending, queued = queued.ToArray() };
    }

    /// <summary>
    /// Replaces the current orders with the saved ones. Null (an older save) clears them. Unknown
    /// furniture is skipped with a warning.
    /// </summary>
    public void RestoreState(FurnitureManagerData state)
    {
        _pending.Clear();
        _spawnQueue.Clear();
        _spawnTimer = 0f;
        if (state == null || catalog == null)
            return;

        var orders = OrderManager.Instance;
        bool dayCycle = orders != null && orders.Mode == DeliveryMode.DayCycle;

        if (state.pending != null)
            foreach (var e in state.pending)
            {
                if (e == null || e.count < 1 || !TryFind(e.id, out var def))
                    continue;
                bool wait = e.waitForDay && dayCycle; // mode changed: don't wait forever
                _pending.Add(
                    new Pending
                    {
                        def = def,
                        count = e.count,
                        remaining = wait ? 0f : Mathf.Max(0f, e.remaining),
                        waitForDay = wait,
                    }
                );
            }

        if (state.queued != null)
            foreach (var e in state.queued)
            {
                if (e == null || !TryFind(e.id, out var def))
                    continue;
                for (int i = 0; i < e.count; i++)
                    _spawnQueue.Enqueue(def);
            }
    }

    private bool TryFind(string id, out FurnitureDefinition def)
    {
        if (catalog.TryGetById(id, out def))
            return true;
        Debug.LogWarning($"{name}: saved furniture '{id}' is not in the catalog, skipped.", this);
        return false;
    }

    // ---- Delivery ----

    private void Update()
    {
        float dt = Time.deltaTime;
        TickPending(dt);
        TickSpawn(dt);
    }

    private void TickPending(float dt)
    {
        for (int i = 0; i < _pending.Count; i++)
        {
            var p = _pending[i];
            if (p.waitForDay)
                continue;

            p.remaining -= dt;
            if (p.remaining > 0f)
            {
                _pending[i] = p;
                continue;
            }
            Enqueue(p);
            _pending.RemoveAt(i);
            i--;
        }
    }

    private void Enqueue(Pending p)
    {
        for (int i = 0; i < p.count; i++)
            _spawnQueue.Enqueue(p.def);
    }

    private void TickSpawn(float dt)
    {
        _spawnTimer -= dt;
        if (_spawnQueue.Count == 0 || _spawnTimer > 0f)
            return;

        _spawnTimer = spawnInterval;
        if (TrySpawnCrate(_spawnQueue.Peek()))
        {
            _spawnQueue.Dequeue();
            _blockedFor = 0f;
            return;
        }

        _blockedFor += spawnInterval;
        if (_blockedFor >= BlockedReportSeconds)
        {
            _blockedFor = 0f;
            Debug.LogWarning(
                $"{name}: {_spawnQueue.Count} crate(s) waiting, the delivery pad is full or missing. "
                    + "Clear the pad, or check the OrderManager's slot settings (a crate must fit a slot).",
                this
            );
        }
    }

    private bool TrySpawnCrate(FurnitureDefinition def)
    {
        var orders = OrderManager.Instance;
        if (orders == null || !orders.TryFindFreeSlot(out Vector3 pos))
            return false;

        var crate = Instantiate(cratePrefab, pos, orders.PadRotation);
        crate.Init(def);
        if (logDeliveries)
            Debug.Log($"{name}: spawned a crate of '{def.name}' at {pos}.", this);
        return true;
    }

    [ContextMenu("Debug: order one")]
    private void DebugOrderOne()
    {
        if (!Application.isPlaying || testFurniture == null)
        {
            Debug.LogWarning($"{name}: needs Play mode and a Test Furniture.", this);
            return;
        }
        var cart = new FurnitureCart();
        cart.Add(testFurniture, 1);
        Debug.Log($"{name}: order placed = {TryPlaceOrder(cart)}", this);
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
        RestoreState(JsonUtility.FromJson<FurnitureManagerData>(json));
        Debug.Log($"{name}: restored from {json}", this);
    }

    private void OnValidate()
    {
        spawnInterval = Mathf.Max(0.05f, spawnInterval);
    }
}
