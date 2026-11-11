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

/// <summary>
/// The checkout counter. The customer AI calls TryBeginCheckout, then ItemsReady. Products resting on
/// the counter are scanned automatically. Payment is automatic; the player gives back change.
/// </summary>
public class Register : MonoBehaviour
{
    public static Register Instance;

    [Tooltip("Counter area. Settled products inside it are scanned automatically.")]
    [SerializeField]
    private PickupZone counter;

    [Tooltip(
        "Optional point on the counter where customers unload their shopping. Keep it beside the scanner, outside its PickupZone. Automatically positioned beside the scanner if empty."
    )]
    [SerializeField]
    Transform customerBasketPoint;

    /// <summary>Stages the actual shelf products beside the scanner for the player to pick up.</summary>
    public Vector3 CustomerBasketPosition(ProductCarriable product, int index, int count)
    {
        var basis = customerBasketPoint != null ? customerBasketPoint : counter.transform;
        var origin = customerBasketPoint != null ? basis.position : counter.CustomerStagingOrigin;
        int columns = Mathf.Min(3, count);
        var position =
            origin
            + basis.right * ((index % columns - (columns - 1) * 0.5f) * 0.22f)
            + basis.forward * (index / columns * 0.22f);
        // Find the counter surface so products do not fall through or spawn inside it.
        float surfaceY = origin.y;
        float nearest = float.PositiveInfinity;
        foreach (
            var hit in Physics.RaycastAll(
                position + Vector3.up * 2f,
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
        float bottomOffset = 0.1f;
        var renderers = product.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            bottomOffset = product.transform.position.y - bounds.min.y;
        }
        position.y = surfaceY + bottomOffset + 0.015f;
        return position;
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

    private void OnValidate() => scanInterval = Mathf.Max(0.02f, scanInterval);
}
