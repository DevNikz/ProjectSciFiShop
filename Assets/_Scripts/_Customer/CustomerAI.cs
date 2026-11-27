using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.AI;

public enum CustomerState
{
    JoiningQueue,
    WaitingForCheckout,
    Leaving,
    Shopping,
}

/// <summary>
/// Navigation and checkout-line behaviour for one customer.
/// Order, scanning, and payment are deliberately handled by the cashier system.
/// </summary>
[DisallowMultipleComponent]
public class CustomerAI : MonoBehaviour, IOrderedCheckoutCustomer
{
    [SerializeField]
    CustomerQueue checkoutQueue;

    [SerializeField]
    Transform exitPoint;

    [SerializeField, Min(0.1f)]
    float arrivalTolerance = 0.15f;

    [Tooltip("How long a newly spawned customer remains visible at Spawn Point before shopping.")]
    [SerializeField, Min(0f)]
    float spawnPause = 1f;

    [Header("Appearance")]
    [Tooltip(
        "Each customer gets a repeatable height between these values (metres). The default NavMeshAgent height is 2."
    )]
    [SerializeField, Min(0.5f)]
    float minimumHeight = 1.6f;

    [SerializeField, Min(0.5f)]
    float maximumHeight = 2f;

    [Tooltip("Body mesh. Stretched vertically to match the customer's height.")]
    [SerializeField]
    Transform capsuleVisual;

    [Tooltip("Head mesh. Scaled uniformly and raised to match the customer's height.")]
    [SerializeField]
    Transform headVisual;

    [Header("Movement")]
    [Tooltip(
        "Off uses simple direct movement between queue markers. Turn this on later when the store has a reliable NavMesh."
    )]
    [SerializeField]
    bool useNavMeshAgent;

    [SerializeField, Min(0.1f)]
    float walkSpeed = 3.5f;

    [SerializeField, Min(1f)]
    float turnSpeed = 540f;

    [Tooltip(
        "Places this customer exactly on its checkout or queue marker. Useful for the prototype queue."
    )]
    [SerializeField]
    bool snapToQueueMarker;

    [SerializeField]
    bool destroyAfterLeaving = true;

    [SerializeField]
    bool joinQueueOnStart = true;

    [Header("Cashier")]
    [Tooltip(
        "Extra coins paid above the order total. The customer waits until this change is returned. Zero pays exact cash."
    )]
    [SerializeField, Min(0)]
    int cashOverpay = 5;

    [Tooltip(
        "Choose a random extra payment for each customer. Turn off to use Cash Overpay above."
    )]
    [SerializeField]
    bool randomizeCashOverpay = true;

    [SerializeField, Min(1)]
    int minimumCashOverpay = 1;

    [SerializeField, Min(1)]
    int maximumCashOverpay = 19;

    [Tooltip("How many different products this customer requests.")]
    [SerializeField, Range(1, 4)]
    int minimumProductTypes = 1;

    [SerializeField, Range(1, 4)]
    int maximumProductTypes = 2;

    [SerializeField, Range(1, 5)]
    int maximumQuantityPerProduct = 2;

    [Header("Shopping")]
    [SerializeField, Min(0f)]
    float shelfPickupDelay = 0.6f;

    [Tooltip("Give up on an unreachable shelf after this many seconds.")]
    [SerializeField, Min(1f)]
    float shelfTravelTimeout = 25f;

    [SerializeField, Min(0f)]
    float shelfLeaveDelay = 0.6f;

    [Tooltip(
        "A customer looks for items this long (seconds, random between min and max) before giving up."
    )]
    [SerializeField, Min(1f)]
    float minimumLifetime = 45f;

    [SerializeField, Min(1f)]
    float maximumLifetime = 90f;

    [Tooltip(
        "Chance (0-1) that a wanted item has a preferred brand. The rest accept any brand of it."
    )]
    [SerializeField, Range(0f, 1f)]
    float brandLoyaltyChance = 0.4f;

    [Tooltip(
        "Only look for products that belong to an item group (branded products). A product with no group "
            + "(a leftover or test asset) is never wanted unless it is already on a shelf."
    )]
    [SerializeField]
    bool onlyWantGroupedProducts = true;

    [Tooltip(
        "Seconds a customer keeps looking for a preferred brand before accepting another brand of the same item."
    )]
    [SerializeField, Min(0f)]
    float brandWaitSeconds = 12f;

    [Tooltip(
        "How far from a shelf (metres) a customer wanders between shelf checks. Only used when the scene has no Store Bounds."
    )]
    [SerializeField, Min(0f)]
    float wanderRadius = 3f;

    [Tooltip("Seconds spent looking at a shelf before its stock is checked.")]
    [SerializeField, Min(0f)]
    float shelfLookDelay = 0.8f;

    [Header("Ticketing")]
    [Tooltip(
        "A shelf only lets so many customers browse at once (Shelf Tickets). A customer without a ticket wanders and tries another shelf. "
            + "After this many seconds of being turned away (the clock resets whenever a ticket is won) the customer stops shopping."
    )]
    [SerializeField, Min(1f)]
    float ticketPatience = 20f;

    [Header("Counter Placement")]
    [Tooltip("Seconds one item takes to arc from the customer onto the counter.")]
    [SerializeField, Min(0.05f)]
    float flightDuration = 0.3f;

    [Tooltip("Pause between one item landing and the next leaving (seconds).")]
    [SerializeField, Min(0f)]
    float placeInterval = 0.05f;

    [Tooltip("How far above the straight line an item rises on its way to the counter (metres).")]
    [SerializeField, Min(0f)]
    float arcHeight = 0.25f;

    const float DefaultHeight = 2f; // default NavMeshAgent height the authored meshes are sized for

    sealed class ShoppingItem
    {
        public ShelfInventory shelf;
        public ProductCarriable product;
        public bool collected;
        public bool delivered;
    }

    // One thing the customer is looking for: an item (group), maybe one preferred brand of it.
    sealed class WantedItem
    {
        public UnityEngine.Object groupKey;
        public ProductDefinition preferred; // null = any brand
        public int remaining;
        public float brandWaitStart = -1f; // -1 = not started
    }

    readonly List<ShoppingItem> shoppingItems = new();
    readonly List<WantedItem> wants = new();
    bool didReachPoint;
    ShelfTickets heldTicket;
    float ticketWaitStart = -1f; // -1 = not being turned away
    Transform shoppingMarker;
    Coroutine shoppingRoutine;
    Coroutine deliveryRoutine;
    readonly HashSet<ShelfInventory> watchedShelves = new();
    readonly HashSet<ShelfInventory> staleShelves = new(); // visited, nothing taken: skipped until stock changes
    int stockChanges; // counts Changed events from watched shelves
    bool didLogNoMatch;

    NavMeshAgent agent;
    Transform destination;
    bool registered;
    bool checkoutReadyRaised;
    bool checkoutStarted;
    float nextCheckoutAttempt;
    int chosenCashOverpay = -1;
    readonly List<CustomerOrderLine> order = new();

    public readonly struct CustomerOrderLine
    {
        public readonly ProductDefinition Product;
        public readonly int Quantity;

        public CustomerOrderLine(ProductDefinition product, int quantity)
        {
            Product = product;
            Quantity = quantity;
        }
    }

    public event Action<CustomerAI> ReadyForCheckout;
    public event Action<CustomerAI> LeftStore;

    public CustomerQueue CheckoutQueue => checkoutQueue;
    public CustomerState State { get; private set; } = CustomerState.JoiningQueue;
    public bool IsReadyForCheckout => State == CustomerState.WaitingForCheckout;
    public bool IsCheckingOut => checkoutStarted;
    public bool HasOrder => order.Count > 0;
    public IReadOnlyList<CustomerOrderLine> Order => order;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null && !useNavMeshAgent)
            agent.enabled = false;

        ApplyHeight(GetDeterministicHeight());
    }

    // Same customer, same height: seeded by the instance id, so global Random state is untouched.
    float GetDeterministicHeight()
    {
        var seeded = new System.Random(GetInstanceID());
        float t = (float)seeded.NextDouble();
        return Mathf.Lerp(minimumHeight, Mathf.Max(minimumHeight, maximumHeight), t);
    }

    // The meshes are authored for DefaultHeight, so their current scale and Y position are the baseline.
    void ApplyHeight(float height)
    {
        float k = height / DefaultHeight;
        if (agent != null)
            agent.height = height;

        if (capsuleVisual != null)
        {
            var scale = capsuleVisual.localScale;
            scale.y *= k; // width stays fixed
            capsuleVisual.localScale = scale;
            capsuleVisual.localPosition = ScaleY(capsuleVisual.localPosition, k);
        }
        else
            Debug.LogWarning(
                $"{name}: Capsule Visual not assigned, so height only changes the agent.",
                this
            );

        if (headVisual != null)
        {
            headVisual.localScale *= k; // proportional head
            headVisual.localPosition = ScaleY(headVisual.localPosition, k);
        }
        else
            Debug.LogWarning(
                $"{name}: Head Visual not assigned, so height only changes the agent.",
                this
            );
    }

    static Vector3 ScaleY(Vector3 position, float factor)
    {
        position.y *= factor;
        return position;
    }

    void Start()
    {
        // Movement is controlled by this script or NavMeshAgent, never physics.
        if (TryGetComponent<Rigidbody>(out var body))
        {
            body.isKinematic = true;
            body.useGravity = false;
        }

        if (checkoutQueue == null)
            checkoutQueue = FindFirstObjectByType<CustomerQueue>();

        if (useNavMeshAgent)
            PlaceOnNavMesh();

        if (StoreBounds.All.Count == 0)
            Debug.LogWarning(
                $"{name}: no Store Bounds in the scene, so the whole scene counts as the store.",
                this
            );

        if (joinQueueOnStart)
            shoppingRoutine = StartCoroutine(ShopAfterSpawnPause());
    }

    void Update()
    {
        if (
            State == CustomerState.JoiningQueue
            && IsAtDestination()
            && checkoutQueue != null
            && checkoutQueue.CurrentCustomer == this
        )
        {
            State = CustomerState.WaitingForCheckout;
            if (!checkoutReadyRaised)
            {
                checkoutReadyRaised = true;
                ReadyForCheckout?.Invoke(this);
                checkoutQueue.NotifyCustomerReady(this);
            }
        }

        if (
            IsReadyForCheckout
            && !checkoutStarted
            && Register.Instance != null
            && Time.time >= nextCheckoutAttempt
        )
        {
            nextCheckoutAttempt = Time.time + 1f;
            BeginCashierCheckout();
        }

        if (State == CustomerState.Leaving && IsAtDestination())
        {
            LeftStore?.Invoke(this);
            if (destroyAfterLeaving)
                Destroy(gameObject);
            else
                gameObject.SetActive(false);
        }
    }

    // Run simple movement after ordinary Update calls so no other scene behaviour can overwrite it.
    void LateUpdate()
    {
        if (!useNavMeshAgent)
            MoveDirectlyToDestination();
    }

    public void SetQueue(CustomerQueue queue) => checkoutQueue = queue;

    public void SetExitPoint(Transform point)
    {
        if (point != null)
            exitPoint = point;
    }

    public void JoinCheckoutQueue()
    {
        if (registered || checkoutQueue == null || !HasOrder)
        {
            Debug.LogWarning(
                $"Registered: {registered} | Queue: {checkoutQueue} | HasOrder: {HasOrder}"
            );
            return;
        }

        registered = checkoutQueue.Join(this);
    }

    IEnumerator ShopAfterSpawnPause()
    {
        State = CustomerState.Shopping;
        if (spawnPause > 0f)
            yield return new WaitForSeconds(spawnPause);

        var shelves = new List<ShelfInventory>();
        RefreshBrowsableShelves(shelves);
        if (shelves.Count == 0)
            Debug.LogWarning(
                $"{name}: no shelves with Shelf Tickets inside the store, so there is nothing to browse.",
                this
            );
        BuildWantList(shelves);
        shoppingMarker = new GameObject("Customer Shelf Destination").transform;
        shoppingMarker.SetParent(transform.parent, false);

        float deadline =
            Time.time
            + UnityEngine.Random.Range(
                minimumLifetime,
                Mathf.Max(minimumLifetime, maximumLifetime)
            );
        ticketWaitStart = -1f;
        if (!StoreBounds.IsInsideStore(transform.position))
            yield return WalkIntoStoreRoutine(deadline);

        WatchShelves(shelves);
        var matches = new List<ShelfInventory>();
        while (IsStillLooking(deadline) && !IsTicketPatienceOver())
        {
            yield return null; // a frame every pass, so a customer that is already there never spins
            var shelf = PickNextShelf(matches, out bool hasFreeTicket);
            if (shelf == null)
            {
                // Nothing in the store holds a wanted item right now: wait for restocking, then wander.
                StartPreferredBrandWaits();
                yield return WaitForStockRoutine(shelves, deadline);
                continue;
            }

            yield return WalkToRoutine(shelf.CustomerPickupPosition, deadline);
            if (!didReachPoint)
            {
                if (Time.time < deadline)
                    Debug.LogWarning(
                        $"{name}: could not reach a browse point within {shelfTravelTimeout}s.",
                        this
                    );
                continue;
            }

            if (!shelf.TryGetComponent(out ShelfTickets tickets) || tickets.TryTake(this))
            {
                heldTicket = tickets;
                ticketWaitStart = -1f;
                int itemsBefore = shoppingItems.Count;
                yield return LookAtShelfRoutine(shelf);
                ReleaseTicket();
                if (shoppingItems.Count == itemsBefore)
                    staleShelves.Add(shelf); // it held nothing for us after all
            }
            else
            {
                if (ticketWaitStart < 0f)
                    ticketWaitStart = Time.time;
                if (!hasFreeTicket) // every matching shelf is busy: wander, then try again
                    yield return WalkToRoutine(PickWanderPoint(shelves), deadline);
            }
        }
        UnwatchShelves();

        ReleaseReservations();
        RebuildCollectedOrder();
        destination = null;
        Destroy(shoppingMarker.gameObject);
        shoppingMarker = null;
        shoppingRoutine = null;
        if (!HasOrder)
        {
            Debug.LogWarning(
                $"{name}: nothing collected ({shoppingItems.Count} item(s) were reserved).",
                this
            );
            CustomerNotificationUI.Show(
                IsTicketPatienceOver()
                    ? "Customer left — the shelves were too busy"
                    : "Customer left — nothing they wanted was in stock"
            );
            LeaveStore();
        }
        else
        {
            State = CustomerState.JoiningQueue;
            JoinCheckoutQueue();
            if (!registered)
                LeaveStore();
        }
    }

    // No wants at all (nothing to look for) still wanders until the lifetime ends.
    bool IsStillLooking(float deadline) =>
        Time.time < deadline && (wants.Count == 0 || wants.Exists(want => want.remaining > 0));

    bool IsTicketPatienceOver() =>
        ticketWaitStart >= 0f && Time.time - ticketWaitStart >= ticketPatience;

    bool IsWantedDefinition(ProductDefinition definition) =>
        wants.Exists(want => want.remaining > 0 && IsAcceptable(want, definition));

    // Nearest shelf in the store that holds a wanted item. A shelf with a free ticket beats a nearer busy one.
    ShelfInventory PickNextShelf(List<ShelfInventory> matches, out bool hasFreeTicket)
    {
        hasFreeTicket = false;
        ShelfStockFinder.FindShelves(transform.position, IsWantedDefinition, matches);
        ShelfInventory nearestBusy = null;
        foreach (var shelf in matches)
        {
            if (staleShelves.Contains(shelf))
                continue;
            if (!shelf.TryGetComponent(out ShelfTickets tickets) || tickets.HasFreeTicket)
            {
                hasFreeTicket = true;
                return shelf;
            }
            nearestBusy ??= shelf;
        }
        return nearestBusy;
    }

    // No shelf holds the preferred brand: its wait before accepting another brand starts now.
    void StartPreferredBrandWaits()
    {
        foreach (var want in wants)
            if (want.remaining > 0 && want.preferred != null && want.brandWaitStart < 0f)
                want.brandWaitStart = Time.time;
    }

    // Stands still until a shelf changes or brandWaitSeconds pass, then wanders before the next look.
    IEnumerator WaitForStockRoutine(List<ShelfInventory> shelves, float deadline)
    {
        RefreshBrowsableShelves(shelves); // a bought expansion adds its shelves
        WatchShelves(shelves);
        if (!didLogNoMatch)
        {
            didLogNoMatch = true;
            Debug.LogWarning(
                $"{name}: no shelf in the store holds a wanted item, waiting for stock. {DescribeNoMatch(shelves)}",
                this
            );
        }

        int seen = stockChanges;
        float end = Mathf.Min(Time.time + brandWaitSeconds, deadline);
        while (Time.time < end && stockChanges == seen)
            yield return null;
        if (stockChanges == seen && Time.time < deadline)
            yield return WalkToRoutine(PickWanderPoint(shelves), deadline);
    }

    // For the no-match warning: what this customer still wants and what each browsable shelf holds.
    string DescribeNoMatch(List<ShelfInventory> shelves)
    {
        var text = new StringBuilder("Wants: ");
        foreach (var want in wants)
        {
            if (want.remaining <= 0)
                continue;
            text.Append(want.groupKey != null ? want.groupKey.name : "?")
                .Append(" x")
                .Append(want.remaining);
            if (want.preferred != null)
                text.Append(" (brand ").Append(want.preferred.DisplayName).Append(')');
            text.Append("; ");
        }
        text.Append("Browsable shelves: ").Append(shelves.Count).Append(". ");

        var stock = new Dictionary<string, int>();
        var names = new Dictionary<string, string>();
        foreach (var shelf in shelves)
        {
            if (shelf == null)
                continue;
            shelf.GetStock(stock, names);
            text.Append(shelf.ShelfName).Append(": ");
            if (stock.Count == 0)
                text.Append("empty");
            foreach (var pair in stock)
                text.Append(names[pair.Key]).Append(" x").Append(pair.Value).Append(", ");
            text.Append("| ");
        }
        AppendWantMatchDetail(text, shelves);
        return text.ToString();
    }

    // Debug: why a want that looks stocked still matches nothing (units filtered out, or a group key mismatch).
    void AppendWantMatchDetail(StringBuilder text, List<ShelfInventory> shelves)
    {
        var available = new List<ProductCarriable>();
        int shelved = 0;
        foreach (var shelf in shelves)
        {
            if (shelf == null)
                continue;
            shelved += shelf.Count;
            shelf.GetAvailableProducts(available);
        }
        text.Append("\nDEBUG units on shelves: ")
            .Append(shelved)
            .Append(", free and usable: ")
            .Append(available.Count)
            .Append(".");

        foreach (var want in wants)
        {
            if (want.remaining <= 0 || want.groupKey == null)
                continue;
            int sameName = 0,
                sameKey = 0,
                acceptable = 0;
            foreach (var product in available)
            {
                var key = product.Definition.GroupKey;
                if (key != null && key.name == want.groupKey.name)
                    sameName++;
                if (key == want.groupKey)
                    sameKey++;
                if (IsAcceptable(want, product.Definition))
                    acceptable++;
            }
            text.Append("\nDEBUG ")
                .Append(want.groupKey.name)
                .Append(" (")
                .Append(want.groupKey.GetType().Name)
                .Append(", id ")
                .Append(want.groupKey.GetInstanceID())
                .Append("): same name ")
                .Append(sameName)
                .Append(", same key ")
                .Append(sameKey)
                .Append(", acceptable ")
                .Append(acceptable)
                .Append(", brand wait over ")
                .Append(IsBrandWaitOver(want))
                .Append(".");
        }
    }

    void WatchShelves(List<ShelfInventory> shelves)
    {
        foreach (var shelf in shelves)
            if (shelf != null && watchedShelves.Add(shelf))
                shelf.Changed += HandleShelfChanged;
    }

    void UnwatchShelves()
    {
        foreach (var shelf in watchedShelves)
            if (shelf != null)
                shelf.Changed -= HandleShelfChanged;
        watchedShelves.Clear();
        staleShelves.Clear();
    }

    void HandleShelfChanged()
    {
        stockChanges++;
        staleShelves.Clear();
    }

    // Browsable = has Shelf Tickets and its pickup point is inside an open store area.
    static void RefreshBrowsableShelves(List<ShelfInventory> shelves)
    {
        shelves.Clear();
        foreach (var tickets in ShelfTickets.All)
            if (tickets != null && StoreBounds.IsInsideStore(tickets.Shelf.CustomerPickupPosition))
                shelves.Add(tickets.Shelf);
    }

    // Spawn points are outside the store: walk to the nearest open store point before browsing.
    IEnumerator WalkIntoStoreRoutine(float giveUpAt)
    {
        if (!StoreBounds.TryGetClosestPoint(transform.position, useNavMeshAgent, out var entry))
        {
            Debug.LogWarning(
                $"{name}: no open store area to walk into, so it shops where it stands.",
                this
            );
            yield break;
        }

        yield return WalkToRoutine(entry, giveUpAt);
        if (!didReachPoint)
            Debug.LogWarning(
                $"{name}: could not walk into the store within {shelfTravelTimeout}s.",
                this
            );
    }

    void ReleaseTicket()
    {
        if (heldTicket != null)
            heldTicket.Release(this);
        heldTicket = null;
    }

    // Walks to a point and sets didReachPoint. Gives up at shelfTravelTimeout or the lifetime deadline.
    IEnumerator WalkToRoutine(Vector3 point, float giveUpAt)
    {
        point.y = transform.position.y;
        shoppingMarker.position = point;
        SetDestination(shoppingMarker);
        float timeout = Mathf.Min(Time.time + shelfTravelTimeout, giveUpAt);
        while (!IsAtDestination() && Time.time < timeout)
            yield return null;
        didReachPoint = IsAtDestination();
    }

    // A shelf is checked only when the customer is standing at it: take what matches a want.
    IEnumerator LookAtShelfRoutine(ShelfInventory shelf)
    {
        if (shelfLookDelay > 0f)
            yield return new WaitForSeconds(shelfLookDelay);
        var stock = new List<ProductCarriable>();
        shelf.GetAvailableProducts(stock);
        foreach (var want in wants)
        {
            foreach (var pickup in stock)
            {
                if (want.remaining <= 0)
                    break;
                if (pickup == null || !IsAcceptable(want, pickup.Definition))
                    continue;
                if (shelf == null || !shelf.TryReserveProduct(pickup, this))
                    continue;
                var item = new ShoppingItem { shelf = shelf, product = pickup };
                shoppingItems.Add(item);
                yield return CollectFromShelfRoutine(item);
                if (item.collected)
                    want.remaining--;
            }
            // Still short after this shelf: the preferred brand's wait starts now.
            if (want.remaining > 0 && want.preferred != null && want.brandWaitStart < 0f)
                want.brandWaitStart = Time.time;
        }
    }

    IEnumerator CollectFromShelfRoutine(ShoppingItem item)
    {
        yield return new WaitForSeconds(shelfPickupDelay);
        if (
            item.shelf == null
            || item.product == null
            || !item.shelf.TryCollectProduct(item.product, this)
        )
        {
            Debug.LogWarning($"{name}: could not collect a reserved item from its shelf.", this);
            yield break;
        }
        item.collected = true;
        item.product.transform.SetParent(transform, true);
        item.product.Carriable.Body.isKinematic = true;
        item.product.Carriable.Body.useGravity = false;
        item.product.gameObject.SetActive(false); // carried in the customer's basket
        yield return new WaitForSeconds(shelfLeaveDelay);
    }

    bool IsAcceptable(WantedItem want, ProductDefinition definition)
    {
        if (definition == null || definition.GroupKey != want.groupKey)
            return false;
        if (want.preferred == null || definition == want.preferred)
            return true;
        return IsBrandWaitOver(want); // another brand only once the wait for the preferred one is over
    }

    bool IsBrandWaitOver(WantedItem want) =>
        want.brandWaitStart >= 0f && Time.time - want.brandWaitStart >= brandWaitSeconds;

    Vector3 PickWanderPoint(List<ShelfInventory> shelves)
    {
        if (StoreBounds.TryGetRandomPoint(useNavMeshAgent, out var storePoint))
            return storePoint;

        var anchor = transform.position;
        if (shelves.Count > 0)
        {
            var shelf = shelves[UnityEngine.Random.Range(0, shelves.Count)];
            if (shelf != null)
                anchor = shelf.CustomerPickupPosition;
        }
        var offset = UnityEngine.Random.insideUnitCircle * wanderRadius;
        var point = anchor + new Vector3(offset.x, 0f, offset.y);
        if (
            useNavMeshAgent
            && NavMesh.SamplePosition(point, out var hit, wanderRadius + 1f, NavMesh.AllAreas)
        )
            point = hit.position;
        return point;
    }

    /// <summary>Called by CustomerQueue whenever this customer's position changes.</summary>
    internal void SetQueuePosition(int index, Transform target)
    {
        if (State == CustomerState.Leaving || target == null)
            return;

        if (destination != target)
        {
            State = CustomerState.JoiningQueue;
            checkoutReadyRaised = false;
            SetDestination(target);
        }

        if (snapToQueueMarker)
            transform.position = target.position;
    }

    /// <summary>Called by CustomerQueue after a successful cashier transaction.</summary>
    public void LeaveStore()
    {
        UnwatchShelves();
        if (shoppingRoutine != null)
            StopCoroutine(shoppingRoutine);
        shoppingRoutine = null;
        if (deliveryRoutine != null)
            StopCoroutine(deliveryRoutine);
        deliveryRoutine = null;
        ReleaseReservations();
        ReturnUndeliveredItems();
        registered = false;
        State = CustomerState.Leaving;

        if (exitPoint == null && checkoutQueue != null)
            exitPoint = checkoutQueue.ExitPoint;

        if (exitPoint == null)
        {
            Debug.LogWarning(
                $"{name}: Exit Point is missing, so this customer was removed after checkout.",
                this
            );
            LeftStore?.Invoke(this);
            Destroy(gameObject);
            return;
        }

        SetDestination(exitPoint);
    }

    public void CancelAndLeave()
    {
        if (registered && checkoutQueue != null)
            checkoutQueue.Leave(this);
        registered = false;
        LeaveStore();
    }

    /// <summary>Called by CustomerQueue when this customer reaches CheckoutPoint.</summary>
    internal bool BeginCashierCheckout()
    {
        if (!IsReadyForCheckout || checkoutStarted)
            return checkoutStarted;

        var register = Register.Instance;
        if (register == null)
        {
            Debug.LogWarning($"{name}: no Register exists in this scene.", this);
            return false;
        }

        checkoutStarted = register.TryBeginCheckout(this);
        if (checkoutStarted)
        {
            CustomerNotificationUI.Show("Customer is ready to be served");
            CustomerOrderUI.Show(this);
            deliveryRoutine = StartCoroutine(DeliverBasketRoutine(register));
        }
        return checkoutStarted;
    }

    /// <summary>
    /// Call after this customer's products are on the register counter. The Register then locks the
    /// total, accepts this customer's cash, and waits for change if it is owed.
    /// </summary>
    public bool NotifyItemsReady()
    {
        var register = Register.Instance;
        bool ready = checkoutStarted && register != null && register.ItemsReady(this);
        if (!ready && checkoutStarted)
            CustomerNotificationUI.Show("Scan the requested products before completing checkout");
        return ready;
    }

    public int ChooseCash(int total)
    {
        // Pick once per customer so retries cannot change the amount handed over.
        if (chosenCashOverpay < 0)
        {
            int minimum = Mathf.Max(1, minimumCashOverpay);
            int maximum = Mathf.Max(minimum, maximumCashOverpay);
            chosenCashOverpay = randomizeCashOverpay
                ? UnityEngine.Random.Range(minimum, maximum + 1)
                : Mathf.Max(0, cashOverpay);
        }
        return total + chosenCashOverpay;
    }

    public bool MatchesOrder(IReadOnlyList<ProductCarriable> scannedProducts)
    {
        if (order.Count == 0)
            return false;

        var scanned = new Dictionary<ProductDefinition, int>();
        for (int i = 0; i < scannedProducts.Count; i++)
        {
            var product = scannedProducts[i];
            if (product == null || product.Definition == null)
                return false;
            scanned.TryGetValue(product.Definition, out int count);
            scanned[product.Definition] = count + 1;
        }

        if (scanned.Count != order.Count)
            return false;
        for (int i = 0; i < order.Count; i++)
        {
            var line = order[i];
            if (!scanned.TryGetValue(line.Product, out int count) || count != line.Quantity)
                return false;
        }
        return true;
    }

    public string OrderSummary()
    {
        if (order.Count == 0)
            return "No order generated";

        var text = new StringBuilder();
        for (int i = 0; i < order.Count; i++)
        {
            if (i > 0)
                text.Append('\n');
            text.Append(order[i].Product.DisplayName).Append("  × ").Append(order[i].Quantity);
        }
        return text.ToString();
    }

    /// <summary>Called by Register when payment/change is finished or the sale was cancelled.</summary>
    public void OnCheckoutEnded(bool completed)
    {
        checkoutStarted = false;
        CustomerOrderUI.Hide(this);
        if (completed)
        {
            CustomerNotificationUI.Show("Customer checkout complete");
            if (checkoutQueue != null)
                checkoutQueue.CompleteCheckout(this);
            return;
        }

        CustomerNotificationUI.Show("Customer checkout cancelled");
        foreach (var item in shoppingItems)
            if (
                item.product != null
                && !item.product.Carriable.IsHeld
                && !item.product.Carriable.IsClaimed
            )
                item.delivered = false;
        CancelAndLeave();
    }

    [ContextMenu("Cashier: Items Ready")]
    void DebugItemsReady() => NotifyItemsReady();

    // Items come from unlocked products whether or not they are on a shelf right now.
    // A product with no group counts as its own item.
    void BuildWantList(List<ShelfInventory> shelves)
    {
        wants.Clear();
        order.Clear();

        var brandsByItem = new Dictionary<UnityEngine.Object, List<ProductDefinition>>();
        foreach (var definition in CollectWantableDefinitions(shelves))
        {
            var key = definition.GroupKey;
            if (!brandsByItem.TryGetValue(key, out var brands))
                brandsByItem[key] = brands = new List<ProductDefinition>();
            brands.Add(definition);
        }
        if (brandsByItem.Count == 0)
        {
            Debug.LogWarning(
                $"{name}: no unlocked products, so there is nothing to look for.",
                this
            );
            return;
        }

        var items = new List<UnityEngine.Object>(brandsByItem.Keys);
        int min = Mathf.Clamp(minimumProductTypes, 1, items.Count);
        int max = Mathf.Clamp(maximumProductTypes, min, items.Count);
        int lineCount = UnityEngine.Random.Range(min, max + 1);
        for (int i = 0; i < lineCount; i++)
        {
            int picked = UnityEngine.Random.Range(0, items.Count);
            var key = items[picked];
            items.RemoveAt(picked); // every line is a different item
            var brands = brandsByItem[key];
            // A preferred brand only means something when the item has more than one brand.
            bool isLoyal = brands.Count > 1 && UnityEngine.Random.value < brandLoyaltyChance;
            wants.Add(
                new WantedItem
                {
                    groupKey = key,
                    preferred = isLoyal ? brands[UnityEngine.Random.Range(0, brands.Count)] : null,
                    remaining = UnityEngine.Random.Range(1, maximumQuantityPerProduct + 1),
                }
            );
        }
    }

    // Free products plus anything on a shelf now (stocked products are unlocked).
    // Resources only finds definitions that are loaded, so swap in the product catalog when it is known.
    List<ProductDefinition> CollectWantableDefinitions(List<ShelfInventory> shelves)
    {
        var result = new List<ProductDefinition>();
        foreach (var definition in Resources.FindObjectsOfTypeAll<ProductDefinition>())
            if (
                definition.UnlockCost <= 0
                && !result.Contains(definition)
                && !(onlyWantGroupedProducts && IsUngrouped(definition))
            )
                result.Add(definition);
        var stock = new List<ProductCarriable>();
        foreach (var shelf in shelves)
            if (shelf != null)
                shelf.GetAvailableProducts(stock);
        foreach (var product in stock)
            if (product != null && !result.Contains(product.Definition))
                result.Add(product.Definition);
        return result;
    }

    // A product with no group is its own item: its group key is the definition itself.
    static bool IsUngrouped(ProductDefinition definition) =>
        definition.GroupKey == (UnityEngine.Object)definition;

    void RebuildCollectedOrder()
    {
        order.Clear();
        var counts = new Dictionary<ProductDefinition, int>();
        foreach (var item in shoppingItems)
            if (item.collected && item.product != null)
            {
                counts.TryGetValue(item.product.Definition, out int count);
                counts[item.product.Definition] = count + 1;
            }
        foreach (var pair in counts)
            order.Add(new CustomerOrderLine(pair.Key, pair.Value));
    }

    void ReleaseReservations()
    {
        ReleaseTicket();
        foreach (var item in shoppingItems)
            if (item.shelf != null)
                item.shelf.ReleaseCustomerReservations(this);
    }

    // Items leave the customer one at a time, arc onto their slot in the register's lanes, and wait there
    // (frozen in grid mode, dropped with physics in free-drop mode) for the player to pick up and scan. An item that has not landed yet stays undelivered, so
    // leaving mid-way sends it back to its shelf.
    IEnumerator DeliverBasketRoutine(Register register)
    {
        var pending = new List<ShoppingItem>();
        var products = new List<ProductCarriable>();
        foreach (var item in shoppingItems)
        {
            if (!item.collected || item.delivered || item.product == null)
                continue;
            pending.Add(item);
            products.Add(item.product);
        }

        var slots = register.PlanCustomerBasket(products, transform.position);
        for (int i = 0; i < pending.Count; i++)
        {
            var product = pending[i].product;
            if (product == null)
                continue;
            yield return FlyToSlot(product, slots[i], register);
            if (product == null)
                continue;
            pending[i].delivered = true;
            if (placeInterval > 0f)
                yield return new WaitForSeconds(placeInterval);
        }
        deliveryRoutine = null;
    }

    IEnumerator FlyToSlot(ProductCarriable product, BasketSlot slot, Register register)
    {
        product.transform.SetParent(null, true);
        product.transform.position = transform.position + Vector3.up;
        product.gameObject.SetActive(true);

        var start = product.transform.position;
        var startRotation = product.transform.rotation;
        float elapsed = 0f;
        while (elapsed < flightDuration)
        {
            if (product == null)
                yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flightDuration);
            var point = Vector3.Lerp(start, slot.position, t);
            point.y += Mathf.Sin(t * Mathf.PI) * arcHeight;
            product.transform.SetPositionAndRotation(
                point,
                Quaternion.Slerp(startRotation, slot.rotation, t)
            );
            yield return null;
        }
        if (product == null)
            yield break;

        product.transform.SetPositionAndRotation(slot.position, slot.rotation);
        if (register != null && register.UsesFreeDrop)
            register.ReleaseOnCounter(product); // physics takes over; the Register puts it back if it falls off
        else
            product.Carriable.FreezeInPlace(); // the player's pickup or drag lifts the freeze
    }

    void ReturnUndeliveredItems()
    {
        foreach (var item in shoppingItems)
        {
            if (!item.collected || item.delivered || item.product == null)
                continue;
            var product = item.product;
            var shelf = item.shelf;
            var returnPosition = transform.position + Vector3.up;
            item.collected = false;
            if (!gameObject.activeInHierarchy)
                NextFrame.Run(() => ReturnProduct(product, shelf, returnPosition));
            else
                ReturnProduct(product, shelf, returnPosition);
        }
    }

    static void ReturnProduct(ProductCarriable product, ShelfInventory shelf, Vector3 position)
    {
        if (product == null || product.Carriable.IsHeld || product.Carriable.IsClaimed)
            return;
        product.transform.SetParent(null, true);
        product.transform.position = position;
        product.gameObject.SetActive(true);
        product.Carriable.Unfreeze(); // it may be frozen on the counter
        // Restore a normal pickup before storing, so the shelf remembers usable physics.
        product.Carriable.Body.isKinematic = false;
        product.Carriable.Body.useGravity = true;
        if (shelf == null || !shelf.TryStoreFromBox(product.Carriable))
        {
            product.Carriable.Body.isKinematic = false;
            product.Carriable.Body.useGravity = true;
        }
    }

    void SetDestination(Transform target)
    {
        destination = target;
        if (!useNavMeshAgent)
            return;

        if (agent == null)
            return;

        if (!agent.isOnNavMesh && !PlaceOnNavMesh())
        {
            Debug.LogWarning($"{name}: spawned too far away from the baked NavMesh.", this);
            return;
        }

        // Targets are authored marker transforms. Do not snap them to a distant piece of NavMesh,
        // otherwise a customer can walk somewhere other than the visible checkout/queue marker.
        if (NavMesh.SamplePosition(target.position, out _, 0.5f, NavMesh.AllAreas))
            agent.SetDestination(target.position);
        else
            Debug.LogWarning($"{name}: '{target.name}' is not on the baked NavMesh.", target);
    }

    bool PlaceOnNavMesh()
    {
        if (agent == null || agent.isOnNavMesh)
            return agent != null && agent.isOnNavMesh;
        if (!NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas))
            return false;

        return agent.Warp(hit.position);
    }

    bool IsAtDestination()
    {
        if (!useNavMeshAgent)
        {
            if (destination == null)
                return false;
            var offset = destination.position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= arrivalTolerance * arrivalTolerance;
        }

        if (destination == null || agent == null || !agent.isOnNavMesh || agent.pathPending)
            return false;
        return agent.remainingDistance <= agent.stoppingDistance + arrivalTolerance;
    }

    void MoveDirectlyToDestination()
    {
        if (destination == null)
            return;

        var target = destination.position;
        target.y = transform.position.y;
        var direction = target - transform.position;
        if (direction.sqrMagnitude <= arrivalTolerance * arrivalTolerance)
        {
            // Direct movement has no pathfinding correction, so finish exactly on the authored marker.
            transform.position = target;
            return;
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            target,
            walkSpeed * Time.deltaTime
        );
        var facing = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            facing,
            turnSpeed * Time.deltaTime
        );
    }

    void OnDisable()
    {
        UnwatchShelves();
        if (shoppingRoutine != null)
            StopCoroutine(shoppingRoutine);
        shoppingRoutine = null;
        if (deliveryRoutine != null)
            StopCoroutine(deliveryRoutine);
        deliveryRoutine = null;
        ReleaseReservations();
        ReturnUndeliveredItems();
        if (shoppingMarker != null)
            Destroy(shoppingMarker.gameObject);
        CustomerOrderUI.Hide(this);
        if (checkoutStarted && Register.Instance != null)
            Register.Instance.CancelCheckout(this);
        checkoutStarted = false;
        if (registered && checkoutQueue != null)
            checkoutQueue.Leave(this);
        registered = false;
    }

    void OnDrawGizmos()
    {
        if (destination == null)
            return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(transform.position, destination.position);
        Gizmos.DrawWireSphere(destination.position, 0.12f);
    }
}
