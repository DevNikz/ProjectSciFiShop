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

    sealed class ShoppingItem
    {
        public ShelfInventory shelf;
        public ProductCarriable product;
        public bool collected;
        public bool delivered;
    }

    readonly List<ShoppingItem> shoppingItems = new();
    Transform shoppingMarker;
    Coroutine shoppingRoutine;

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
            return;

        registered = checkoutQueue.Join(this);
    }

    IEnumerator ShopAfterSpawnPause()
    {
        State = CustomerState.Shopping;
        if (spawnPause > 0f)
            yield return new WaitForSeconds(spawnPause);
        GenerateOrder();
        shoppingMarker = new GameObject("Customer Shelf Destination").transform;
        shoppingMarker.SetParent(transform.parent, false);
        foreach (var item in shoppingItems)
        {
            if (item.shelf == null || item.product == null)
                continue;
            var position = item.shelf.CustomerPickupPosition;
            position.y = transform.position.y;
            shoppingMarker.position = position;
            SetDestination(shoppingMarker);
            float timeout = Time.time + shelfTravelTimeout;
            while (!IsAtDestination() && Time.time < timeout)
                yield return null;
            if (!IsAtDestination())
                continue;
            yield return new WaitForSeconds(shelfPickupDelay);
            if (
                item.shelf == null
                || item.product == null
                || !item.shelf.TryCollectProduct(item.product, this)
            )
                continue;
            item.collected = true;
            item.product.transform.SetParent(transform, true);
            item.product.Carriable.Body.isKinematic = true;
            item.product.Carriable.Body.useGravity = false;
            item.product.gameObject.SetActive(false); // carried in the customer's basket
        }
        ReleaseReservations();
        RebuildCollectedOrder();
        destination = null;
        Destroy(shoppingMarker.gameObject);
        shoppingMarker = null;
        shoppingRoutine = null;
        if (!HasOrder)
        {
            CustomerNotificationUI.Show("Customer left — desired items were unavailable");
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
        if (shoppingRoutine != null)
            StopCoroutine(shoppingRoutine);
        shoppingRoutine = null;
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
            DeliverBasket(register);
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

    void GenerateOrder()
    {
        order.Clear();
        var stock = new List<ProductCarriable>();
        foreach (var shelf in FindObjectsByType<ShelfInventory>(FindObjectsSortMode.None))
            shelf.GetAvailableProducts(stock);
        var available = new List<ProductDefinition>();
        foreach (var product in stock)
            if (!available.Contains(product.Definition))
                available.Add(product.Definition);
        if (available.Count == 0)
            return;
        int min = Mathf.Clamp(minimumProductTypes, 1, available.Count);
        int max = Mathf.Clamp(maximumProductTypes, min, available.Count);
        int lineCount = UnityEngine.Random.Range(min, max + 1);
        for (int i = 0; i < lineCount; i++)
        {
            int picked = UnityEngine.Random.Range(0, available.Count);
            var product = available[picked];
            available.RemoveAt(picked); // every line is a different product
            int wanted = UnityEngine.Random.Range(1, maximumQuantityPerProduct + 1);
            int reservedCount = 0;
            foreach (var pickup in stock)
            {
                if (reservedCount >= wanted)
                    break;
                if (pickup == null || pickup.Definition != product)
                    continue;
                var shelf = pickup.GetComponent<ShelfProduct>().Shelf;
                if (shelf != null && shelf.TryReserveProduct(pickup, this))
                {
                    shoppingItems.Add(new ShoppingItem { shelf = shelf, product = pickup });
                    reservedCount++;
                }
            }
        }
    }

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
        foreach (var item in shoppingItems)
            if (item.shelf != null)
                item.shelf.ReleaseCustomerReservations(this);
    }

    void DeliverBasket(Register register)
    {
        int index = 0;
        foreach (var item in shoppingItems)
        {
            if (!item.collected || item.delivered || item.product == null)
                continue;
            var product = item.product;
            product.transform.SetParent(null, true);
            product.transform.rotation = Quaternion.identity;
            product.gameObject.SetActive(true);
            product.transform.position = register.CustomerBasketPosition(
                product,
                index++,
                shoppingItems.Count
            );
            // Products wait beside the scanner for the player to scan them.
            product.Carriable.Body.isKinematic = false;
            product.Carriable.Body.useGravity = true;
            product.Carriable.Body.linearVelocity = Vector3.zero;
            product.Carriable.Body.angularVelocity = Vector3.zero;
            item.delivered = true;
        }
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
        if (shoppingRoutine != null)
            StopCoroutine(shoppingRoutine);
        shoppingRoutine = null;
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
