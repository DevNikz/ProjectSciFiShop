using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The order screen: pick boxes, see the total, buy. Needs an EventSystem in the scene.</summary>
public class OrderScreen : MonoBehaviour, IScreen, IDesktopApp
{
    [Header("Layout")]
    [Tooltip("Shown while open. Must be a child: this object has to stay active.")]
    [SerializeField]
    GameObject panel;

    [Tooltip("Rows are created here (e.g. a ScrollRect content).")]
    [SerializeField]
    Transform rowParent;

    [SerializeField]
    OrderRow rowPrefab;

    [Header("Labels and buttons")]
    [SerializeField]
    TMP_Text totalLabel;

    [SerializeField]
    TMP_Text coinsLabel;

    [Tooltip("Optional. Boxes on the way, and order results.")]
    [SerializeField]
    TMP_Text statusLabel;

    [SerializeField]
    Button buyButton;

    [SerializeField]
    Button closeButton;

    [Tooltip("Optional. Opens the progression screen. Hidden while no progression screen exists.")]
    [SerializeField]
    Button upgradesButton;

    [Tooltip("Seconds an order result stays in the status line.")]
    [SerializeField]
    float messageTime = 2f;

    readonly OrderCart _cart = new();
    readonly List<OrderRow> _rows = new();
    CurrencyManager _wallet;
    DayManager _day;
    ProgressionManager _progression;
    bool _open,
        _built,
        _hosted; // shown as a desktop window: the desktop owns the input and Esc
    string _message,
        _lastStatus;
    float _messageUntil;

    public ScreenId Id => ScreenId.Orders;
    public bool IsOpen => _open;
    public bool IsWindowOpen => _hosted && _open;
    public Transform WindowRoot => panel != null ? panel.transform : null;

    public event Action WindowClosed;

    void OnEnable()
    {
        if (
            panel == null
            || rowParent == null
            || rowPrefab == null
            || totalLabel == null
            || coinsLabel == null
            || buyButton == null
            || closeButton == null
        )
        {
            Debug.LogError($"{name}: layout/labels/buttons missing.", this);
            enabled = false;
            return;
        }
        buyButton.onClick.AddListener(HandleBuy);
        closeButton.onClick.AddListener(Close);
        if (upgradesButton != null)
            upgradesButton.onClick.AddListener(HandleUpgrades);
        var router = UIScreenRouter.Instance; // null only while quitting
        if (router != null)
        {
            router.Changed += UpdateUpgradesButton;
            router.Register(this);
        }
        UpdateUpgradesButton();
        panel.SetActive(false);
    }

    void OnDisable()
    {
        Close(); // never leave the cursor free or input blocked
        if (buyButton != null)
            buyButton.onClick.RemoveListener(HandleBuy);
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);
        if (upgradesButton != null)
            upgradesButton.onClick.RemoveListener(HandleUpgrades);
        var router = UIScreenRouter.Existing; // never create one while shutting down
        if (router != null)
        {
            router.Changed -= UpdateUpgradesButton;
            router.Unregister(this);
        }
    }

    /// <summary>Open the screen. Does nothing if already open.</summary>
    public void Open()
    {
        if (_open || !isActiveAndEnabled)
            return;
        var input = InputManager.Instance;
        if (input == null)
            return;

        input.EnterUIMode(this);
        BeginSession();
    }

    public void Close()
    {
        if (!_open)
            return;
        if (_hosted) // a desktop window: the desktop keeps the input
        {
            HideWindow();
            return;
        }
        EndSession();
        var input = InputManager.Instance; // null only while quitting
        if (input != null)
            input.ExitUIMode(this);
        var router = UIScreenRouter.Existing;
        if (router != null)
            router.NotifyClosed(this);
    }

    /// <summary>Show as a window inside the desktop. The desktop owns the input and Esc.</summary>
    public void ShowWindow()
    {
        if (_open || !isActiveAndEnabled)
            return;
        _hosted = true;
        BeginSession();
        UpdateUpgradesButton(); // hidden while a window
    }

    /// <summary>Hide the desktop window. Safe to call twice.</summary>
    public void HideWindow()
    {
        if (!_hosted)
            return;
        _hosted = false;
        EndSession();
        UpdateUpgradesButton();
        WindowClosed?.Invoke();
    }

    // Everything Open and ShowWindow share: rows, panel, subscriptions.
    void BeginSession()
    {
        BuildRows();
        _open = true;
        panel.SetActive(true);

        _cart.Changed += Refresh;
        _wallet = CurrencyManager.Instance;
        if (_wallet != null)
            _wallet.OnCoinsChanged += HandleCoins;
        _day = DayManager.Instance; // the ordering lock follows the store opening and closing
        if (_day != null)
        {
            _day.StoreOpened += Refresh;
            _day.StoreClosed += Refresh;
        }
        _progression = ProgressionManager.Instance; // an unlock changes the rows
        if (_progression != null)
            _progression.Changed += Refresh;
        Refresh();
    }

    // Everything Close and HideWindow share: subscriptions, panel.
    void EndSession()
    {
        _open = false;

        _cart.Changed -= Refresh;
        if (_wallet != null)
            _wallet.OnCoinsChanged -= HandleCoins;
        _wallet = null;
        if (_day != null)
        {
            _day.StoreOpened -= Refresh;
            _day.StoreClosed -= Refresh;
        }
        _day = null;
        if (_progression != null)
            _progression.Changed -= Refresh;
        _progression = null;

        if (panel != null) // gone on scene unload
            panel.SetActive(false);
    }

    void Update()
    {
        if (!_open)
            return;
        if (!_hosted) // as a window, the desktop handles Esc
        {
            var input = InputManager.Instance;
            if (input != null && input.UICancelPressed)
            {
                Close();
                return;
            }
        }
        UpdateStatus();
    }

    // One row per orderable product, built the first time the screen opens.
    void BuildRows()
    {
        if (_built)
            return;
        var manager = OrderManager.Instance;
        var catalog = manager != null ? manager.Catalog : null;
        if (catalog == null)
        {
            Debug.LogWarning($"{name}: no OrderManager/catalog yet.", this);
            return; // try again next time
        }

        for (int i = 0; i < catalog.Count; i++)
        {
            var def = catalog.Get(i);
            if (def == null)
                continue;
            if (def.Prefab == null)
            {
                    Debug.LogWarning($"{name}: '{def.name}' has no prefab, not listed.", this);
                continue;
            }
            var row = Instantiate(rowPrefab, rowParent);
            row.Bind(def, HandleStep, HandleUnlock);
            _rows.Add(row);
        }
        _built = true;
    }

    void HandleStep(ProductDefinition def, int delta)
    {
        if (delta > 0)
        {
            if (ProgressionManager.IsUnlocked(def)) // a locked row has no working + button: second guard
                _cart.Add(def, 1);
        }
        else
            _cart.Remove(def, 1);
    }

    void HandleUnlock(ProductDefinition def)
    {
        var progression = ProgressionManager.Instance;
        if (progression != null && progression.TryUnlockProduct(def))
            Say($"Unlocked {def.DisplayName}!");
        else
            Say("Couldn't unlock it.");
        Refresh();
    }

    // The router closes this screen (handing the input back) before the progression screen opens.
    void HandleUpgrades()
    {
        var router = UIScreenRouter.Instance;
        if (router != null)
            router.TryShow(ScreenId.Progression, this);
    }

    void UpdateUpgradesButton()
    {
        if (upgradesButton == null)
            return;
        var router = UIScreenRouter.Existing;
        upgradesButton.gameObject.SetActive(
            router != null && !_hosted && router.IsRegistered(ScreenId.Progression)
        );
    }

    void HandleCoins(int _) => Refresh();

    void HandleBuy()
    {
        var manager = OrderManager.Instance;
        if (manager != null && manager.TryPlaceOrder(_cart))
        {
            _cart.Clear(); // raises Changed, which refreshes
            Say("Order placed!");
        }
        else
        {
            Say("Couldn't place the order.");
            Refresh();
        }
    }

    void Refresh()
    {
        int total = _cart.TotalCost();
        int coins = _wallet != null ? _wallet.GetCoins() : 0;

        foreach (var row in _rows)
        {
            var def = row.Definition;
            bool locked = !ProgressionManager.IsUnlocked(def);
            row.SetBoxes(_cart.GetBoxes(def));
            row.SetLocked(locked, def.UnlockCost, coins >= def.UnlockCost);
            row.SetDetail(locked ? "Locked" : DetailText(def));
        }
        totalLabel.text = $"Total: {total}";
        coinsLabel.text = $"Coins: {coins}";
        var orders = OrderManager.Instance;
        buyButton.interactable =
            !_cart.IsEmpty && coins >= total && orders != null && orders.CanOrderNow;
    }

    static string DetailText(ProductDefinition def)
    {
        string text = $"{def.UnitsPerBox} units x {def.UnitCost} = {OrderCart.LineCost(def, 1)}";
        var prices = PriceBook.Instance;
        return prices != null ? $"{text}  |  sells for {prices.GetSellPrice(def)}" : text;
    }

    void Say(string message)
    {
        _message = message;
        _messageUntil = Time.unscaledTime + messageTime;
    }

    void UpdateStatus()
    {
        if (statusLabel == null)
            return;
        string text;
        if (Time.unscaledTime < _messageUntil)
            text = _message;
        else
        {
            var manager = OrderManager.Instance;
            string reason = manager != null ? manager.LockReason : null;
            int pending = manager != null ? manager.PendingBoxes : 0;
            text = reason ?? (pending > 0 ? $"Boxes on the way: {pending}" : "");
        }
        if (text == _lastStatus)
            return;
        _lastStatus = text;
        statusLabel.text = text;
    }

    void OnValidate() => messageTime = Mathf.Max(0.5f, messageTime);
}
