using System;
using System.Collections.Generic;
using UnityEngine;

//Save Data
[Serializable]
public class PendingOrderEntry
{
    public string id; // ProductDefinition.Id
    public int boxes;
    public float remaining; // Fixed Delay seconds left
    public bool waitForDay;
}

[Serializable]
public class QueuedBoxEntry
{
    public string id;
    public int boxes;
}

[Serializable]
public class OrderManagerData
{
    public PendingOrderEntry[] pending; // paid, not released yet
    public QueuedBoxEntry[] queued; // released, waiting for a free slot, in spawn order
}

public enum DeliveryMode
{
    Instant, // boxes start appearing right after paying
    FixedDelay, // after Delivery Seconds
    DayCycle, // when NotifyDayStarted() is called
}

public enum OrderingWindow
{
    Always, // no lock
    OnlyWhenOpen, // the order screen works only while customers can come in
    OnlyWhenClosed, // only while the store is not open (closing time counts as closed)
}

public class OrderManager : MonoBehaviour
{
    public static OrderManager Instance;

    [Header("Orders")]
    [Tooltip("Every orderable product. Orders with products outside it are refused.")]
    [SerializeField]
    private ProductCatalog catalog;

    [Tooltip("When paid orders arrive.")]
    [SerializeField]
    private DeliveryMode mode = DeliveryMode.FixedDelay;

    [Tooltip("Fixed Delay mode: seconds from paying to the first box.")]
    [SerializeField]
    private float deliverySeconds = 20f;

    [Tooltip(
        "When ordering is allowed. Needs a DayManager in the scene: without one, ordering is never locked."
    )]
    [SerializeField]
    private OrderingWindow orderingWindow = OrderingWindow.Always;

    [Header("Delivery")]
    [Tooltip("Box spawned for each delivery. Needs CardboardBox + DeliveryBox.")]
    [SerializeField]
    private DeliveryBox boxPrefab;

    [Tooltip(
        "Center of the delivery pad. The slot grid is centered on it, along its right (X) and forward (Z)."
    )]
    [SerializeField]
    private Transform zone;

    [Tooltip("Slots along the pad's X.")]
    [SerializeField]
    private int columns = 3;

    [Tooltip("Slots along the pad's Z.")]
    [SerializeField]
    private int rows = 3;

    [Tooltip("Distance between slot centers (m).")]
    [SerializeField]
    private float spacing = 0.8f;

    [Tooltip("Half size of the free-space test per slot. Match your box size (m).")]
    [SerializeField]
    private Vector3 slotHalfExtents = new(0.35f, 0.25f, 0.3f);

    [Tooltip("What blocks a slot (player, boxes, furniture).")]
    [SerializeField]
    private LayerMask blockMask = ~0;

    [Tooltip("Slots start this far above the Zone (m), so the floor or pad under it can't block.")]
    [SerializeField]
    private float slotLift = 0.1f;

    [Tooltip("Seconds between spawned boxes (also the retry time when the pad is full).")]
    [SerializeField]
    private float spawnInterval = 0.25f;

    [Header("Debug")]
    [Tooltip("Log when an order is paid, when boxes spawn, and why the pad is blocked.")]
    [SerializeField]
    private bool logDeliveries = true;

    [Tooltip("Product for the 'Debug: order one box' context menu (Play mode).")]
    [SerializeField]
    private ProductDefinition testProduct;

    private struct Pending
    {
        public ProductDefinition def;
        public int boxes;
        public float remaining; // Fixed Delay only
        public bool waitForDay; // DayCycle only
    }

    private enum SpawnResult
    {
        Spawned,
        Blocked, // pad full: try again
        Failed, // nothing could be stored: give up on this box
    }

    private readonly List<Pending> _pending = new();
    private readonly Queue<ProductDefinition> _spawnQueue = new();
    private float _spawnTimer;
    private float _blockedFor;
    private const float BlockedReportSeconds = 5f;

    /// <summary>Raised after a paid order is accepted (total coins, total boxes).</summary>
    public event Action<int, int> OrderPlaced;

    /// <summary>Raised after a filled box appears on the pad.</summary>
    public event Action<DeliveryBox> DeliveryArrived;

    /// <summary>Orderable products (the order screen lists these).</summary>
    public ProductCatalog Catalog => catalog;

    /// <summary>Why ordering is locked right now, or null if it is allowed.</summary>
    public string LockReason
    {
        get
        {
            var day = DayManager.Instance;
            if (day == null)
                return null;
            return orderingWindow switch
            {
                OrderingWindow.OnlyWhenOpen when !day.IsOpen =>
                    "Ordering is only possible while the store is open.",
                OrderingWindow.OnlyWhenClosed when day.IsOpen =>
                    "Ordering is only possible while the store is closed.",
                _ => null,
            };
        }
    }

    public bool CanOrderNow => LockReason == null;

    /// <summary>When paid orders arrive. Furniture deliveries follow the same mode.</summary>
    public DeliveryMode Mode => mode;

    /// <summary>Seconds from paying to the first delivery (Fixed Delay mode), delivery upgrades included.</summary>
    public float DeliveryDelay =>
        deliverySeconds * ProgressionManager.MultiplierFor(UpgradeKind.DeliveryTime);

    /// <summary>Rotation of the delivery pad. Boxes and crates face this way.</summary>
    public Quaternion PadRotation => zone != null ? zone.rotation : Quaternion.identity;

    /// <summary>Boxes paid for that have not appeared yet.</summary>
    public int PendingBoxes
    {
        get
        {
            int n = _spawnQueue.Count;
            foreach (var p in _pending)
                n += p.boxes;
            return n;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (catalog == null || boxPrefab == null || zone == null)
        {
            Debug.LogError($"{name}: catalog, boxPrefab or zone missing.", this);
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

    /// <summary>
    /// Pays for the cart and queues the delivery. False (nothing spent) if the cart is empty,
    /// a product is not orderable, or the player can't afford it. Does not clear the cart.
    /// </summary>
    public bool TryPlaceOrder(OrderCart cart)
    {
        if (!isActiveAndEnabled || cart == null || cart.IsEmpty)
            return false;

        string lockReason = LockReason;
        if (lockReason != null)
        {
            if (logDeliveries)
                Debug.Log($"{name}: order refused. {lockReason}", this);
            return false;
        }

        foreach (var line in cart.Lines)
        {
            if (line.definition == null || catalog.IndexOf(line.definition) < 0)
            {
                Debug.LogWarning($"{name}: product is not in the catalog.", this);
                return false;
            }
            if (!ProgressionManager.IsUnlocked(line.definition))
            {
                Debug.LogWarning($"{name}: '{line.definition.name}' is not unlocked.", this);
                return false;
            }
            if (line.definition.Prefab == null)
            {
                Debug.LogError($"{name}: '{line.definition.name}' has no prefab.", this);
                return false;
            }
        }

        var wallet = CurrencyManager.Instance;
        int total = cart.TotalCost();
        if (wallet == null || total < 1 || !wallet.TrySpendCoins(total))
            return false;

        // Upgrades apply to orders placed from now on; orders already waiting keep their time.
        float delay = DeliveryDelay;
        foreach (var line in cart.Lines)
        {
            var p = new Pending
            {
                def = line.definition,
                boxes = line.boxes,
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
                $"{name}: paid {total} coins for {cart.TotalBoxes()} boxes. "
                    + mode switch
                    {
                        DeliveryMode.FixedDelay => $"First box in {delay:0.#}s.",
                        DeliveryMode.DayCycle => "Waiting for NotifyDayStarted().",
                        _ => "Spawning now.",
                    },
                this
            );
        OrderPlaced?.Invoke(total, cart.TotalBoxes());
        return true;
    }

    /// <summary>Day Cycle mode: call when a new day starts to release the waiting orders.</summary>
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

        // Furniture deliveries wait for the same signal.
        if (FurnitureManager.Instance != null)
            FurnitureManager.Instance.NotifyDayStarted();
    }

    /// <summary>Paid-for orders that have not appeared yet. Boxes already on the pad are not saved.</summary>
    public OrderManagerData CaptureState()
    {
        var pending = new PendingOrderEntry[_pending.Count];
        for (int i = 0; i < pending.Length; i++)
            pending[i] = new PendingOrderEntry
            {
                id = _pending[i].def.Id,
                boxes = _pending[i].boxes,
                remaining = _pending[i].remaining,
                waitForDay = _pending[i].waitForDay,
            };

        var queued = new List<QueuedBoxEntry>(); // one entry per run of the same product
        foreach (var def in _spawnQueue)
        {
            if (queued.Count > 0 && queued[^1].id == def.Id)
                queued[^1].boxes++;
            else
                queued.Add(new QueuedBoxEntry { id = def.Id, boxes = 1 });
        }
        return new OrderManagerData { pending = pending, queued = queued.ToArray() };
    }

    /// <summary>Replaces the current orders with the saved ones. Unknown products are skipped.</summary>
    public void RestoreState(OrderManagerData state)
    {
        if (state == null || catalog == null)
            return;

        // Match by id, so catalog reorders/removals can't scramble orders.
        var byId = new Dictionary<string, ProductDefinition>(catalog.Count);
        for (int i = 0; i < catalog.Count; i++)
        {
            var def = catalog.Get(i);
            if (def != null)
                byId[def.Id] = def;
        }

        _pending.Clear();
        _spawnQueue.Clear();
        _spawnTimer = 0f;

        if (state.pending != null)
            foreach (var e in state.pending)
            {
                if (
                    e == null
                    || e.id == null
                    || e.boxes < 1
                    || !byId.TryGetValue(e.id, out var def)
                )
                    continue;
                bool wait = e.waitForDay && mode == DeliveryMode.DayCycle; // mode changed: don't wait forever
                _pending.Add(
                    new Pending
                    {
                        def = def,
                        boxes = e.boxes,
                        remaining = wait ? 0f : Mathf.Max(0f, e.remaining),
                        waitForDay = wait,
                    }
                );
            }

        if (state.queued != null)
            foreach (var e in state.queued)
            {
                if (e == null || e.id == null || !byId.TryGetValue(e.id, out var def))
                    continue;
                for (int i = 0; i < e.boxes; i++)
                    _spawnQueue.Enqueue(def);
            }
    }

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
        for (int i = 0; i < p.boxes; i++)
            _spawnQueue.Enqueue(p.def);
    }

    private void TickSpawn(float dt)
    {
        _spawnTimer -= dt;
        if (_spawnQueue.Count == 0 || _spawnTimer > 0f)
            return;

        _spawnTimer = spawnInterval;
        if (TrySpawnBox(_spawnQueue.Peek()) != SpawnResult.Blocked)
        {
            _spawnQueue.Dequeue();
            _blockedFor = 0f;
            return;
        }

        _blockedFor += spawnInterval;
        if (_blockedFor >= BlockedReportSeconds)
        {
            _blockedFor = 0f;
            ReportBlocked();
        }
    }

    // Says what is sitting in the first slot, so a blocked pad is never a silent failure.
    private void ReportBlocked()
    {
        Collider[] hits = Physics.OverlapBox(
            SlotCenter(0, 0),
            slotHalfExtents,
            zone.rotation,
            blockMask,
            QueryTriggerInteraction.Ignore
        );
        string names = "";
        for (int i = 0; i < hits.Length && i < 4; i++)
            names += (i > 0 ? ", " : "") + hits[i].name;
        Debug.LogWarning(
            $"{name}: {_spawnQueue.Count} box(es) waiting, every slot is blocked. "
                + $"First slot overlaps: {(names.Length > 0 ? names : "nothing (another slot is the issue)")}. "
                + "Move the Zone, raise Slot Lift, shrink Slot Half Extents, or change Block Mask.",
            this
        );
    }

    private SpawnResult TrySpawnBox(ProductDefinition def)
    {
        if (!TryFindFreeSlot(out Vector3 pos))
            return SpawnResult.Blocked;

        DeliveryBox box = Instantiate(boxPrefab, pos, zone.rotation);
        int units = def.UnitsPerBox;
        int stored = box.Fill(def, units);
        if (stored < units) // never charge for stock that didn't arrive
            Refund((units - stored) * def.UnitCost, def);
        if (stored == 0)
        {
            Debug.LogError(
                $"{name}: box for '{def.name}' could not be filled, so it was removed.",
                this
            );
            box.gameObject.SetActive(false);
            Destroy(box.gameObject);
            return SpawnResult.Failed;
        }

        if (logDeliveries)
            Debug.Log($"{name}: spawned a box of '{def.name}' at {pos}.", this);
        DeliveryArrived?.Invoke(box);
        return SpawnResult.Spawned;
    }

    /// <summary>The first free delivery slot. False if the pad is full. Furniture crates use it too.</summary>
    public bool TryFindFreeSlot(out Vector3 pos)
    {
        Quaternion rot = zone.rotation;
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < columns; c++)
        {
            Vector3 p = SlotCenter(c, r);
            if (
                !Physics.CheckBox(
                    p,
                    slotHalfExtents,
                    rot,
                    blockMask,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                pos = p;
                return true;
            }
        }
        pos = default;
        return false;
    }

    // Grid centered on the zone: column/row 0 is the far negative corner.
    private Vector3 SlotCenter(int c, int r) =>
        zone.position
        + zone.rotation
            * new Vector3(
                (c - (columns - 1) * 0.5f) * spacing,
                slotHalfExtents.y + slotLift,
                (r - (rows - 1) * 0.5f) * spacing
            );

    private void Refund(int coins, ProductDefinition def)
    {
        var wallet = CurrencyManager.Instance;
        if (wallet == null || coins < 1)
            return;
        wallet.AddCoins(coins);
        Debug.LogWarning(
            $"{name}: refunded {coins} coins for '{def.name}' that did not fit.",
            this
        );
    }

    [ContextMenu("Debug: order one box")]
    private void DebugOrderOne()
    {
        if (!Application.isPlaying || testProduct == null)
        {
            Debug.LogWarning($"{name}: needs Play mode and a Test Product.", this);
            return;
        }
        var cart = new OrderCart();
        cart.Add(testProduct, 1);
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
        RestoreState(JsonUtility.FromJson<OrderManagerData>(json));
        Debug.Log($"{name}: restored from {json}", this);
    }

    // Cyan slot = free, red slot = blocked right now (works in the Editor too).
    private void OnDrawGizmosSelected()
    {
        if (zone == null)
            return;
        Gizmos.matrix = Matrix4x4.TRS(Vector3.zero, zone.rotation, Vector3.one);
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < columns; c++)
        {
            Vector3 world = SlotCenter(c, r);
            bool blocked = Physics.CheckBox(
                world,
                slotHalfExtents,
                zone.rotation,
                blockMask,
                QueryTriggerInteraction.Ignore
            );
            Gizmos.color = blocked ? Color.red : Color.cyan;
            Gizmos.DrawWireCube(Quaternion.Inverse(zone.rotation) * world, slotHalfExtents * 2f);
        }
    }

    private void OnValidate()
    {
        deliverySeconds = Mathf.Max(0f, deliverySeconds);
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);
        spacing = Mathf.Max(0.1f, spacing);
        slotLift = Mathf.Max(0f, slotLift);
        spawnInterval = Mathf.Max(0.05f, spawnInterval);
        slotHalfExtents = Vector3.Max(slotHalfExtents, Vector3.one * 0.05f);
    }
}
