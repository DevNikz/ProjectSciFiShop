using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public enum RegisterState
{
    Idle, // no customer
    Scanning, // customer at the counter, items are scanned as they land
    AwaitingChange, // paid and totaled: waiting for the player to give change
}

public struct SaleReceipt
{
    public int revenue; // what the customer owed
    public int items;
    public int cost; // what the shop paid for those items
    public int Profit => revenue - cost;
}

/// <summary>Where one customer product rests on the counter.</summary>
public struct BasketSlot
{
    public Vector3 position;
    public Quaternion rotation;
}

/// <summary>
/// The checkout counter. The customer AI calls TryBeginCheckout, then ItemsReady. Products resting on
/// the counter are scanned automatically. Payment is automatic; the player gives back change.
/// </summary>
public class Register : MonoBehaviour
{
    public static Register Instance;

    [SerializeField]
    private CustomerOrderUIMode orderUIMode = CustomerOrderUIMode.WorldSpace;
    public CustomerOrderUIMode OrderUIMode => orderUIMode;

    [SerializeField]
    private Transform screenAnchor;
    public Transform ScreenAnchor => screenAnchor;

    [Tooltip("Counter area. Settled products inside it are scanned automatically.")]
    [SerializeField]
    private PickupZone counter;

    [Tooltip(
        "Optional point on the counter where customers unload their shopping. Keep it beside the scanner, outside its PickupZone. Automatically positioned beside the scanner if empty."
    )]
    [SerializeField]
    Transform customerBasketPoint;

    [Tooltip(
        "Area where customers put their shopping, from the basket point, as the customer sees it (metres). Free drop picks random spots inside it. "
            + "Grid layout: X = top (far) to bottom (near), Y = left to right. Items start at the top-left corner and fill X first (down a column), then Y (the next column to the right). When the area is full, the next items stack on top."
    )]
    [SerializeField]
    private Vector2 basketAreaSize = new(0.5f, 0.9f);

    [Tooltip("Gap between neighbouring products and columns on the counter (metres).")]
    [SerializeField]
    private float layoutGap = 0.03f;

    [Tooltip(
        "Off = free drop: customers let products fall onto the counter and physics settles them. "
            + "On = the old tidy grid, where products are placed exactly in their slots."
    )]
    [SerializeField]
    private bool useGridLayout;

    [Tooltip(
        "Free drop: how high above the counter each product is released (metres). Higher = a livelier tumble."
    )]
    [SerializeField]
    private float dropHeight = 0.1f;

    [Tooltip(
        "Free drop: random spots tried to find one clear of other products. If none is found, the product is dropped on top of them."
    )]
    [SerializeField]
    private int placementAttempts = 8;

    [Tooltip(
        "Free drop: each product is turned a random amount left or right of facing the customer (degrees)."
    )]
    [SerializeField]
    private float yawJitter = 30f;

    [Tooltip(
        "Grid layout only. While the register is selected, draw the last basket layout: columns, the start corner, each product's footprint (spacing) and stack layers."
    )]
    [SerializeField]
    private bool showLayoutGizmos = true;

    [Tooltip(
        "Free drop: seconds after release that a product is watched. One that falls off the counter in that time is put back."
    )]
    [SerializeField]
    private float dropWatchSeconds = 6f;

    private const float SurfaceClearance = 0.015f;

    // Free drop: how far below the counter a product must fall to count as lost, and how high it is put back.
    private const float FallRecoverDistance = 0.3f;
    private const float RecoverLift = 0.2f;

    // A product released with physics, watched for a while in case it rolls off the counter.
    private struct DropWatch
    {
        public ProductCarriable product;
        public float until;
    }

    private readonly List<DropWatch> _watched = new();
    private Vector3 _lastCustomerPosition;

    // The last PlanCustomerBasket result, kept only so the gizmos can draw it.
    private struct LayoutDebugItem
    {
        public Vector3 center; // world middle of the product's bounds
        public Vector3 size; // in the basket frame
        public int layer;
    }

    private struct LayoutDebugColumn
    {
        public Vector3 center; // world middle of the column's floor strip
        public Vector2 size; // width, depth
    }

    // A product already planned in this free drop: the circle it covers on the counter and where its top rests.
    private struct DropSpot
    {
        public Vector2 xz;
        public float radius;
        public float top;
    }

    // One column of the basket grid: the items stacked top to bottom, and the room they use.
    private sealed class BasketColumn
    {
        public readonly List<int> items = new();
        public float width,
            depth,
            height,
            left; // distance from the left edge of the area
        public int layer;
    }

    private readonly List<LayoutDebugItem> _debugItems = new();
    private readonly List<LayoutDebugColumn> _debugColumns = new();
    private Vector3 _debugStart, // top-left corner of the area (world)
        _debugRight, // unit vector: left to right
        _debugDown; // unit vector: top to bottom
    private Quaternion _debugFrame = Quaternion.identity;

    // What a product measures in the basket frame (right, up, forward) in the pose it will rest in.
    private readonly struct ProductFootprint
    {
        public readonly Vector3 Size; // width, height, depth
        public readonly Vector3 CenterOffset; // from the product's pivot to the middle of its bounds
        public readonly float BottomOffset; // how far the pivot is above the lowest point

        public ProductFootprint(Vector3 size, Vector3 centerOffset, float bottomOffset)
        {
            Size = size;
            CenterOffset = centerOffset;
            BottomOffset = bottomOffset;
        }
    }

    private Transform BasketBasis =>
        customerBasketPoint != null ? customerBasketPoint : counter.transform;

    private Vector3 BasketOrigin =>
        customerBasketPoint != null ? customerBasketPoint.position : counter.CustomerStagingOrigin;

    // Flat frame of the basket area: only the turn around the vertical axis counts.
    private Quaternion BasketFrame => Quaternion.Euler(0f, BasketBasis.eulerAngles.y, 0f);

    // basketAreaSize is X = top to bottom, Y = left to right.
    private float AreaDepth => basketAreaSize.x;
    private float AreaWidth => basketAreaSize.y;

    /// <summary>
    /// True in free-drop mode. The caller should then release each product at its slot with physics on
    /// (not snap it into place) and let it settle. False in grid mode.
    /// </summary>
    public bool UsesFreeDrop => !useGridLayout;

    /// <summary>
    /// Free drop (default): gives each product a random spot in the basket area, a little above the counter,
    /// for the caller to release with physics. With Use Grid Layout on, it lays the customer's products out
    /// on the counter as the customer sees it: from the top-left corner
    /// of the basket area, down each column from the far edge to the near edge, then on to the next
    /// column to the right. Products of one type sit together. When the area is full, the next items
    /// start a new layer on top. Returns one slot per product, in the same order. Products keep their
    /// current pose: the caller moves them into the slots.
    /// </summary>
    public List<BasketSlot> PlanCustomerBasket(
        IReadOnlyList<ProductCarriable> products,
        Vector3 customerPosition
    )
    {
        _lastCustomerPosition = customerPosition;
        var rotation = GetBasketRotation(customerPosition);
        var frame = BasketFrame;
        _debugItems.Clear();
        _debugColumns.Clear();
        _debugFrame = frame;
        var slots = new List<BasketSlot>(products.Count);
        var footprints = new ProductFootprint[products.Count];
        var groups = new List<List<int>>();
        var groupOf = new Dictionary<ProductDefinition, List<int>>();

        for (int i = 0; i < products.Count; i++)
        {
            slots.Add(new BasketSlot { position = BasketOrigin, rotation = rotation });
            var product = products[i];
            if (product == null)
                continue;

            var original = product.transform.rotation;
            product.transform.rotation = rotation; // measure it as it will rest
            footprints[i] = MeasureProduct(product, frame);
            product.transform.rotation = original;

            if (
                product.Definition == null
                || !groupOf.TryGetValue(product.Definition, out var group)
            )
            {
                group = new List<int>();
                groups.Add(group);
                if (product.Definition != null)
                    groupOf[product.Definition] = group;
            }
            group.Add(i);
        }

        var order = new List<int>(products.Count); // one product type after another
        foreach (var group in groups)
            order.AddRange(group);
        if (useGridLayout)
            LayGrid(order, footprints, rotation, customerPosition, slots);
        else
            PlanFreeDrop(order, footprints, rotation, slots);
        return slots;
    }

    // Columns first: each takes items from the top until its depth is used up, so its width is the widest
    // item in it. Then the columns go left to right, and one that no longer fits the width starts a new layer.
    private void LayGrid(
        List<int> order,
        ProductFootprint[] footprints,
        Quaternion rotation,
        Vector3 customerPosition,
        List<BasketSlot> slots
    )
    {
        var columns = new List<BasketColumn>();
        BasketColumn column = null;
        foreach (int i in order)
        {
            var size = footprints[i].Size;
            if (size.x > AreaWidth || size.z > AreaDepth)
                Debug.LogWarning(
                    $"{name}: a product ({size.x:0.00} x {size.z:0.00} m) is bigger than the basket area.",
                    this
                );
            if (
                column == null
                || (column.items.Count > 0 && column.depth + layoutGap + size.z > AreaDepth)
            )
            {
                column = new BasketColumn();
                columns.Add(column);
            }
            column.depth += (column.items.Count > 0 ? layoutGap : 0f) + size.z;
            column.width = Mathf.Max(column.width, size.x);
            column.height = Mathf.Max(column.height, size.y);
            column.items.Add(i);
        }

        var layerHeights = new List<float> { 0f };
        int layer = 0;
        float left = 0f;
        foreach (var c in columns)
        {
            if (left > 0f && left + c.width > AreaWidth)
            {
                layer++;
                left = 0f;
                layerHeights.Add(0f);
            }
            c.layer = layer;
            c.left = left;
            left += c.width + layoutGap;
            layerHeights[layer] = Mathf.Max(layerHeights[layer], c.height);
        }

        var layerBases = new float[layerHeights.Count];
        for (int l = 1; l < layerBases.Length; l++)
            layerBases[l] = layerBases[l - 1] + layerHeights[l - 1] + layoutGap;

        // The area is read as the customer sees it: far edge on top, the customer's left on the left.
        var frame = BasketFrame;
        var origin = BasketOrigin;
        bool customerIsBehind = IsCustomerBehind(customerPosition);
        float rightSign = customerIsBehind ? 1f : -1f;
        float downSign = customerIsBehind ? -1f : 1f;
        float topZ = customerIsBehind ? AreaDepth : 0f;
        float leftX = rightSign * -AreaWidth * 0.5f;
        _debugStart = origin + frame * new Vector3(leftX, 0f, topZ);
        _debugRight = frame * new Vector3(rightSign, 0f, 0f);
        _debugDown = frame * new Vector3(0f, 0f, downSign);

        for (int c = 0; c < columns.Count; c++)
        {
            var col = columns[c];
            float columnX = leftX + rightSign * (col.left + col.width * 0.5f);
            float depth = 0f;
            foreach (int i in col.items)
            {
                var footprint = footprints[i];
                var center =
                    origin
                    + frame
                        * new Vector3(
                            columnX,
                            0f,
                            topZ + downSign * (depth + footprint.Size.z * 0.5f)
                        );
                var pivot =
                    center
                    - frame * new Vector3(footprint.CenterOffset.x, 0f, footprint.CenterOffset.z);
                float surfaceY = FindSurfaceY(new Vector3(pivot.x, origin.y, pivot.z), origin.y);
                pivot.y =
                    surfaceY + footprint.BottomOffset + SurfaceClearance + layerBases[col.layer];

                slots[i] = new BasketSlot { position = pivot, rotation = rotation };
                _debugItems.Add(
                    new LayoutDebugItem
                    {
                        center = pivot + frame * footprint.CenterOffset,
                        size = footprint.Size,
                        layer = col.layer,
                    }
                );
                depth += footprint.Size.z + layoutGap;
            }

            if (col.layer == 0)
                _debugColumns.Add(
                    new LayoutDebugColumn
                    {
                        center =
                            origin
                            + frame * new Vector3(columnX, 0f, topZ + downSign * col.depth * 0.5f),
                        size = new Vector2(col.width, col.depth),
                    }
                );
        }
    }

    /// <summary>
    /// Free drop: call when a product reaches its slot. Turns its physics on so it falls and settles, and
    /// watches it for a few seconds. One that falls off the counter is put back above the basket area.
    /// </summary>
    public void ReleaseOnCounter(ProductCarriable product)
    {
        if (product == null)
            return;
        var body = product.Carriable.Body;
        if (body == null)
        {
            Debug.LogWarning(
                $"{name}: '{product.name}' has no Rigidbody, so it can't be dropped.",
                this
            );
            return;
        }
        body.isKinematic = false;
        body.useGravity = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
        _watched.Add(new DropWatch { product = product, until = Time.time + dropWatchSeconds });
    }

    // Stops watching a product that is gone, picked up or past its time. One that fell is put back.
    private void WatchDrops()
    {
        for (int i = _watched.Count - 1; i >= 0; i--)
        {
            var watch = _watched[i];
            var product = watch.product;
            if (
                product == null
                || !product.gameObject.activeInHierarchy
                || product.Carriable.IsHeld
                || product.Carriable.IsClaimed
                || Time.time > watch.until
            )
            {
                _watched.RemoveAt(i);
                continue;
            }
            if (product.transform.position.y < BasketOrigin.y - FallRecoverDistance)
            {
                PutBack(product);
                watch.until = Time.time + dropWatchSeconds;
                _watched[i] = watch;
            }
        }
    }

    // A product that fell off the counter drops again from above a random spot in the basket area.
    private void PutBack(ProductCarriable product)
    {
        var slot = PlanCustomerBasket(new[] { product }, _lastCustomerPosition)[0];
        product.transform.SetPositionAndRotation(
            slot.position + Vector3.up * RecoverLift,
            slot.rotation
        );
        var body = product.Carriable.Body;
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    // Each product gets a random spot clear of the ones already planned, a random turn, and a start a little
    // above the counter. A product that finds no clear spot is dropped on top of what is under it.
    private void PlanFreeDrop(
        List<int> order,
        ProductFootprint[] footprints,
        Quaternion rotation,
        List<BasketSlot> slots
    )
    {
        var frame = BasketFrame;
        var origin = BasketOrigin;
        var placed = new List<DropSpot>(order.Count);
        bool warnedCrowded = false;

        foreach (int i in order)
        {
            var footprint = footprints[i];
            var size = footprint.Size;
            if (size.x > AreaWidth || size.z > AreaDepth)
                Debug.LogWarning(
                    $"{name}: a product ({size.x:0.00} x {size.z:0.00} m) is bigger than the basket area.",
                    this
                );

            float radius = 0.5f * Mathf.Sqrt(size.x * size.x + size.z * size.z);
            var point = GetAreaPoint(radius, origin, frame);
            float overlapTop = GetOverlapTop(point, radius, placed);
            for (int attempt = 1; attempt < placementAttempts; attempt++)
            {
                if (float.IsNegativeInfinity(overlapTop))
                    break;
                point = GetAreaPoint(radius, origin, frame);
                overlapTop = GetOverlapTop(point, radius, placed);
            }
            if (!float.IsNegativeInfinity(overlapTop) && !warnedCrowded)
            {
                warnedCrowded = true;
                Debug.LogWarning(
                    $"{name}: the basket area is crowded, so some products are dropped on top of others.",
                    this
                );
            }

            var turn = Quaternion.Euler(0f, UnityEngine.Random.Range(-yawJitter, yawJitter), 0f);
            var pivot =
                point
                - turn
                    * (frame * new Vector3(footprint.CenterOffset.x, 0f, footprint.CenterOffset.z));
            float surfaceY = FindSurfaceY(new Vector3(point.x, origin.y, point.z), origin.y);
            float floorY = Mathf.Max(surfaceY, overlapTop);
            float restingBottom = floorY + SurfaceClearance;
            pivot.y = restingBottom + footprint.BottomOffset + dropHeight;

            slots[i] = new BasketSlot { position = pivot, rotation = turn * rotation };
            placed.Add(
                new DropSpot
                {
                    xz = new Vector2(point.x, point.z),
                    radius = radius,
                    top = restingBottom + size.y,
                }
            );
        }
    }

    // A random point on the basket area, kept inside it by the product's radius (or its middle if it is wider).
    private Vector3 GetAreaPoint(float radius, Vector3 origin, Quaternion frame)
    {
        float halfWidth = Mathf.Max(0f, AreaWidth * 0.5f - radius);
        float halfDepth = Mathf.Max(0f, AreaDepth * 0.5f - radius);
        float x = UnityEngine.Random.Range(-halfWidth, halfWidth);
        float z = AreaDepth * 0.5f + UnityEngine.Random.Range(-halfDepth, halfDepth);
        return origin + frame * new Vector3(x, 0f, z);
    }

    // The highest top among planned products this circle would touch, or negative infinity if it is clear.
    private float GetOverlapTop(Vector3 point, float radius, List<DropSpot> placed)
    {
        float top = float.NegativeInfinity;
        var xz = new Vector2(point.x, point.z);
        foreach (var spot in placed)
            if (Vector2.Distance(xz, spot.xz) < spot.radius + radius + layoutGap)
                top = Mathf.Max(top, spot.top);
        return top;
    }

    // True when the customer stands on the -forward side of the basket frame.
    private bool IsCustomerBehind(Vector3 customerPosition)
    {
        var toCustomer = customerPosition - BasketOrigin;
        toCustomer.y = 0f;
        return Vector3.Dot(BasketFrame * Vector3.forward, toCustomer) < 0f;
    }

    // Flat, with the product's front (local +Z) toward the side of the counter the customer stands on.
    private Quaternion GetBasketRotation(Vector3 customerPosition)
    {
        var frame = BasketFrame;
        return IsCustomerBehind(customerPosition) ? frame * Quaternion.Euler(0f, 180f, 0f) : frame;
    }

    // Find the counter surface so products do not fall through or spawn inside it.
    private static float FindSurfaceY(Vector3 point, float fallbackY)
    {
        float surfaceY = fallbackY;
        float nearest = float.PositiveInfinity;
        foreach (
            var hit in Physics.RaycastAll(
                point + Vector3.up * 2f,
                Vector3.down,
                3f,
                ~0,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            if (
                hit.collider.GetComponentInParent<Carriable>() != null
                || hit.collider.GetComponentInParent<CustomerAI>() != null
                || hit.distance >= nearest
            )
                continue;
            nearest = hit.distance;
            surfaceY = hit.point.y;
        }
        return surfaceY;
    }

    // Oriented bounds of every renderer, in the basket frame, for the pose the product has right now.
    private static ProductFootprint MeasureProduct(ProductCarriable product, Quaternion frame)
    {
        var toFrame = Quaternion.Inverse(frame);
        var pivot = toFrame * product.transform.position;
        var bounds = new Bounds();
        bool hasBounds = false;
        foreach (var renderer in product.GetComponentsInChildren<Renderer>(true))
        {
            var local = renderer.localBounds;
            var toWorld = renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                var sign = new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f
                );
                var point =
                    toFrame
                    * toWorld.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, sign));
                if (hasBounds)
                    bounds.Encapsulate(point);
                else
                {
                    bounds = new Bounds(point, Vector3.zero);
                    hasBounds = true;
                }
            }
        }
        if (!hasBounds) // no renderers: assume a small box standing on its pivot
            return new ProductFootprint(Vector3.one * 0.2f, Vector3.up * 0.1f, 0.1f);
        return new ProductFootprint(bounds.size, bounds.center - pivot, pivot.y - bounds.min.y);
    }

    [Tooltip(
        "Change tray. Settled coins in it count as change given; coins above the change due are kept by the customer. "
            + "Empty = give change in code with TryCompleteChange."
    )]
    [SerializeField]
    private PickupZone changeTray;

    [Tooltip("Seconds between counter scans.")]
    [SerializeField]
    private float scanInterval = 0.1f;

    [Tooltip("On = sold items disappear when the sale completes (the customer takes them).")]
    [SerializeField]
    private bool consumeItemsOnSale = true;

    private readonly List<ProductCarriable> _basket = new();
    private readonly List<Coin> _coins = new();
    private ICheckoutCustomer _customer;
    private RegisterState _state;
    private float _scanTimer;
    private int _runningTotal,
        _lockedTotal,
        _lockedCost,
        _cashPaid,
        _changeDue,
        _trayTotal,
        _lastCount = -1,
        _lastTotal = -1;
    private bool _finalizingCustomerOrder;

    public RegisterState State => _state;
    public bool HasCustomer => _state != RegisterState.Idle;

    /// <summary>Items currently counted.</summary>
    public int ItemCount => _basket.Count;

    /// <summary>Running total while scanning, the locked total once the customer is done.</summary>
    public int Total => _state == RegisterState.AwaitingChange ? _lockedTotal : _runningTotal;

    public int CashPaid => _cashPaid;
    public int ChangeDue => _changeDue;

    /// <summary>Value of the settled coins in the change tray (while waiting for change).</summary>
    public int TrayTotal => _trayTotal;

    /// <summary>Raised whenever what the display shows may have changed.</summary>
    public event Action Changed;

    /// <summary>Raised when a sale completes.</summary>
    public event Action<SaleReceipt> SaleCompleted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (counter == null)
        {
            Debug.LogError($"{name}: counter PickupZone missing.", this);
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

    /// <summary>A customer steps up. False if the counter is busy or the customer is null.</summary>
    public bool TryBeginCheckout(ICheckoutCustomer customer)
    {
        if (!isActiveAndEnabled || customer == null || _state != RegisterState.Idle)
            return false;

        ResetSale();
        _customer = customer;
        _state = RegisterState.Scanning;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// The customer has put everything down. Locks the total, takes payment, and either completes
    /// the sale (exact cash) or waits for change. False if there is nothing on the counter.
    /// </summary>
    public bool ItemsReady(ICheckoutCustomer customer)
    {
        if (_state != RegisterState.Scanning || !ReferenceEquals(customer, _customer))
            return false;

        var wallet = CurrencyManager.Instance;
        if (wallet == null || PriceBook.Instance == null)
        {
            Debug.LogError($"{name}: CurrencyManager or PriceBook missing in scene.", this);
            return false;
        }

        Rescan(finalizeOrder: false); // final look; never recursively accept payment
        if (_basket.Count == 0)
            return false;

        if (
            _customer is IOrderedCheckoutCustomer orderedCustomer
            && orderedCustomer.HasOrder
            && !orderedCustomer.MatchesOrder(_basket)
        )
        {
            Debug.LogWarning($"{name}: scanned products do not match the customer's order.", this);
            return false;
        }

        _lockedTotal = _runningTotal; // prices are locked from here
        _lockedCost = 0;

        // Cost is what the shop paid per unit, not what the customer pays.
        foreach (var p in _basket)
            _lockedCost += p.Definition.UnitCost;

        _cashPaid = Math.Max(_lockedTotal, customer.ChooseCash(_lockedTotal));
        _changeDue = _cashPaid - _lockedTotal;
        wallet.AddCoins(_cashPaid);

        if (_changeDue <= 0)
        {
            Complete();
            return true;
        }
        _state = RegisterState.AwaitingChange;
        Changed?.Invoke();
        return true;
    }

    /// <summary>The player gave this much change. Completes the sale if it covers what is due.</summary>
    public bool TryCompleteChange(int given)
    {
        if (_state != RegisterState.AwaitingChange || given < _changeDue)
            return false;
        Complete();
        return true;
    }

    /// <summary>
    /// The customer leaves. Before paying: nothing happens. After paying: the change owed is taken
    /// back and the sale completes.
    /// </summary>
    public void CancelCheckout(ICheckoutCustomer customer)
    {
        if (_state == RegisterState.Idle || !ReferenceEquals(customer, _customer))
            return;
        Cancel();
    }

    private void Update()
    {
        if (_watched.Count > 0)
            WatchDrops(); // even between customers: products can still be rolling
        if (_state == RegisterState.Idle)
            return;

        if (IsDestroyed(_customer)) // the AI object vanished mid-sale
        {
            Debug.LogWarning($"{name}: customer was destroyed during checkout.", this);
            Cancel();
            return;
        }

        _scanTimer -= Time.deltaTime;
        if (_scanTimer > 0f)
            return;
        _scanTimer = scanInterval;
        if (_state == RegisterState.Scanning)
            Rescan();
        else
            RescanTray();
    }

    // Coins resting in the tray count as change given. Enough of them completes the sale and the
    // customer takes them all.
    private void RescanTray()
    {
        if (changeTray == null)
            return;

        changeTray.Collect(_coins);
        int sum = 0;
        foreach (var c in _coins)
            sum += c.Value;
        if (sum != _trayTotal)
        {
            _trayTotal = sum;
            Changed?.Invoke();
        }

        if (_trayTotal < _changeDue)
            return;
        // Complete resets the sale (including _coins) and notifies the customer/queue.
        // Keep the accepted coins so the same physical money cannot pay the next customer.
        var acceptedCoins = _coins.ToArray();
        if (!TryCompleteChange(_trayTotal))
            return;
        foreach (var c in acceptedCoins)
        {
            if (c == null || c.Carriable.IsHeld || c.Carriable.IsClaimed) // taken back by the player: leave it
                continue;
            c.gameObject.SetActive(false);
            Destroy(c.gameObject);
        }
        _coins.Clear();
    }

    // Items on the counter, priced at today's sell prices.
    private void Rescan(bool finalizeOrder = true)
    {
        var prices = PriceBook.Instance;
        if (prices == null)
            return;

        counter.Collect(_basket);
        for (int i = _basket.Count - 1; i >= 0; i--)
            if (_basket[i].Definition == null) // product without a definition: can't be priced
                _basket.RemoveAt(i);

        int total = 0;
        foreach (var p in _basket)
            total += prices.GetSellPrice(p.Definition);
        _runningTotal = total;

        if (_basket.Count != _lastCount || total != _lastTotal)
        {
            _lastCount = _basket.Count;
            _lastTotal = total;
            Changed?.Invoke();
        }

        // An AI customer has a precise order, so their checkout can finish the moment all of
        // the requested products have been scanned. The guard prevents ItemsReady's final rescan
        // from recursively trying to finish the same order again.
        if (
            finalizeOrder
            && _state == RegisterState.Scanning
            && !_finalizingCustomerOrder
            && _customer is IOrderedCheckoutCustomer orderedCustomer
            && orderedCustomer.HasOrder
            && orderedCustomer.MatchesOrder(_basket)
        )
        {
            _finalizingCustomerOrder = true;
            try
            {
                ItemsReady(_customer);
            }
            finally
            {
                _finalizingCustomerOrder = false;
            }
        }
    }

    private void Cancel()
    {
        if (_state == RegisterState.AwaitingChange)
        {
            TakeBackChange();
            Complete();
            return;
        }

        var customer = _customer;
        ResetSale();
        Notify(customer, false);
        Changed?.Invoke();
    }

    // The customer walks off with their change: take what is owed (or what the wallet has).
    private void TakeBackChange()
    {
        var wallet = CurrencyManager.Instance;
        if (wallet == null)
            return;
        int amount = Math.Min(_changeDue, wallet.GetCoins());
        if (amount > 0)
            wallet.TrySpendCoins(amount);
    }

    private void Complete()
    {
        var receipt = new SaleReceipt
        {
            revenue = _lockedTotal,
            items = _basket.Count,
            cost = _lockedCost,
        };

        if (consumeItemsOnSale)
            foreach (var p in _basket)
            {
                if (p == null || p.Carriable.IsHeld || p.Carriable.IsClaimed) // taken back by the player: leave it
                    continue;
                p.gameObject.SetActive(false);
                Destroy(p.gameObject);
            }

        var customer = _customer;
        ResetSale();
        DayManager.Instance?.RecordSale(receipt.items, receipt.revenue, receipt.cost);
        SaleCompleted?.Invoke(receipt);
        Notify(customer, true);
        Changed?.Invoke();
    }

    private void ResetSale()
    {
        _state = RegisterState.Idle;
        _customer = null;
        _basket.Clear();
        _runningTotal = _lockedTotal = _lockedCost = _cashPaid = _changeDue = _trayTotal = 0;
        _lastCount = _lastTotal = -1;
        _scanTimer = 0f;
        _finalizingCustomerOrder = false;
    }

    private static bool IsDestroyed(ICheckoutCustomer c) => c is UnityEngine.Object o && o == null;

    private static void Notify(ICheckoutCustomer c, bool completed)
    {
        if (c != null && !IsDestroyed(c))
            c.OnCheckoutEnded(completed);
    }

    // Shows the basket area when the register is selected: the width and depth products fill before
    // they stack. The start corner and fill direction show once a layout has been planned.
    private void OnDrawGizmosSelected()
    {
        if (customerBasketPoint == null && counter == null)
            return;

        var previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(BasketOrigin, BasketFrame, Vector3.one);

        var center = new Vector3(0f, 0f, AreaDepth * 0.5f);
        var size = new Vector3(AreaWidth, 0.002f, AreaDepth);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.15f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
        Gizmos.DrawWireCube(center, size);

        Gizmos.matrix = previousMatrix;

        if (showLayoutGizmos && useGridLayout)
            DrawLayoutGizmos();
    }

    // Columns are the coloured floor strips; each wire box is a product's real footprint, so the gaps
    // between boxes are the spacing. Box colour is the stack layer: green bottom, then yellow, orange, red.
    // The white marker is the start (top-left) corner, with a line down the first column and a short
    // one along the first row.
    private void DrawLayoutGizmos()
    {
        var previousMatrix = Gizmos.matrix;

        for (int c = 0; c < _debugColumns.Count; c++)
        {
            var column = _debugColumns[c];
            var color = Color.HSVToRGB((c * 0.17f) % 1f, 0.5f, 1f);
            Gizmos.matrix = Matrix4x4.TRS(column.center, _debugFrame, Vector3.one);
            var size = new Vector3(column.size.x, 0.002f, column.size.y);
            Gizmos.color = new Color(color.r, color.g, color.b, 0.2f);
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.color = color;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }

        foreach (var item in _debugItems)
        {
            Gizmos.matrix = Matrix4x4.TRS(item.center, _debugFrame, Vector3.one);
            Gizmos.color = Color.HSVToRGB(Mathf.Max(0f, 0.33f - item.layer * 0.11f), 0.9f, 1f);
            Gizmos.DrawWireCube(Vector3.zero, item.size);
        }

        if (_debugItems.Count > 0)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(_debugStart, 0.025f);
            Gizmos.DrawLine(_debugStart, _debugStart + _debugDown * AreaDepth);
            Gizmos.DrawLine(_debugStart, _debugStart + _debugRight * 0.15f);
        }

        Gizmos.matrix = previousMatrix;
    }

    private void OnValidate()
    {
        scanInterval = Mathf.Max(0.02f, scanInterval);
        basketAreaSize = Vector2.Max(basketAreaSize, new Vector2(0.1f, 0.1f));
        layoutGap = Mathf.Max(0f, layoutGap);
        dropHeight = Mathf.Max(0f, dropHeight);
        placementAttempts = Mathf.Max(1, placementAttempts);
        yawJitter = Mathf.Clamp(yawJitter, 0f, 180f);
        dropWatchSeconds = Mathf.Max(0f, dropWatchSeconds);
    }
}
