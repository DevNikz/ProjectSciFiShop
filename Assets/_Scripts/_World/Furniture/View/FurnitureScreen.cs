using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The furniture screen: pick pieces, see the total, buy. Needs an EventSystem in the scene.</summary>
public class FurnitureScreen : MonoBehaviour, IScreen, IDesktopApp
{
    [Header("Layout mode")]
    [Tooltip(
        "BuiltIn: the window, list and buttons are made in code. Custom: use the scene objects below."
    )]
    [SerializeField]
    UILayoutMode mode = UILayoutMode.BuiltIn;

    [Tooltip("Look of the built-in layout. Empty = the default look (same as the desktop window).")]
    [SerializeField]
    UIStyle style;

    [Tooltip("Built-in window title when opened on its own. Empty = this object's name.")]
    [SerializeField]
    string windowTitle;

    [Header("Custom layout (Custom mode only)")]
    [Tooltip("Shown while open. Must be a child: this object has to stay active.")]
    [SerializeField]
    GameObject panel;

    [Tooltip("Rows are created here (e.g. a ScrollRect content).")]
    [SerializeField]
    Transform rowParent;

    [SerializeField]
    FurnitureRow rowPrefab;

    [Header("Labels and buttons")]
    [SerializeField]
    TMP_Text totalLabel;

    [SerializeField]
    TMP_Text coinsLabel;

    [Tooltip("Optional. Crates on the way, and order results.")]
    [SerializeField]
    TMP_Text statusLabel;

    [SerializeField]
    Button buyButton;

    [SerializeField]
    Button closeButton;

    [Tooltip("Seconds an order result stays in the status line.")]
    [SerializeField]
    float messageTime = 2f;

    readonly FurnitureCart _cart = new();
    readonly List<FurnitureRow> _rows = new();
    CurrencyManager _wallet;
    ProgressionManager _progression;
    bool _open,
        _built,
        _hosted; // shown as a desktop window: the desktop owns the input and Esc
    string _message,
        _lastStatus;
    float _messageUntil;
    GameObject _canvasRoot; // BuiltIn: this screen's own canvas, shown only when not inside the desktop

    public bool IsOpen => _open;
    public bool IsWindowOpen => _hosted && _open;
    public Transform WindowRoot => panel != null ? panel.transform : null;

    public ScreenId Id => ScreenId.Furniture;

    public event Action WindowClosed;

    void OnEnable()
    {
        if (mode == UILayoutMode.BuiltIn && !TryBuildLayout())
        {
            enabled = false;
            return;
        }
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
        panel.SetActive(false);
    }

    void OnDisable()
    {
        Close(); // never leave the cursor free or input blocked
        if (buyButton != null)
            buyButton.onClick.RemoveListener(HandleBuy);
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);
    }

    // BuiltIn: makes this screen's own canvas, window frame and panel, and puts them in the layout fields
    // below (in memory only, the scene is not changed). The scene panel is hidden and not used.
    bool TryBuildLayout()
    {
        if (_canvasRoot != null)
            return true; // built before: OnEnable can run again
        if (style == null)
            Debug.LogWarning($"{name}: no UI Style, using the default look.", this);
        var look = style != null ? style : UIStyle.Fallback;

        var canvas = UIBuilder.CreateOverlayCanvas("[FurnitureCanvas]", transform, 100);
        var content = UIBuilder.BuildWindowFrame(
            canvas.transform,
            look.Window,
            Close,
            out _,
            out TMP_Text title
        );
        var parts = ShopPanelBuilder.BuildPanel(content, look, "Item", "Details", "Qty", false);
        if (parts == null)
        {
            Destroy(canvas.gameObject);
            return false;
        }
        title.text = string.IsNullOrEmpty(windowTitle) ? name : windowTitle;

        var template = new GameObject(
            "FurnitureRowTemplate",
            typeof(RectTransform),
            typeof(FurnitureRow)
        ).GetComponent<FurnitureRow>();
        template.transform.SetParent(parts.templateHolder, false);
        template.BuildDefault(look);

        if (panel != null)
            panel.SetActive(false); // the scene (Custom) panel is not used in BuiltIn
        panel = parts.panel.gameObject;
        rowParent = parts.rowParent;
        rowPrefab = template;
        totalLabel = parts.totalLabel;
        coinsLabel = parts.coinsLabel;
        statusLabel = parts.statusLabel;
        buyButton = parts.buyButton;
        closeButton = parts.closeButton;

        _canvasRoot = canvas.gameObject;
        _canvasRoot.SetActive(false);
        return true;
    }

    bool IsPanelInOwnCanvas() =>
        _canvasRoot != null && panel != null && panel.transform.IsChildOf(_canvasRoot.transform);

    void ShowOwnCanvas(bool visible)
    {
        if (_canvasRoot != null)
            _canvasRoot.SetActive(visible);
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
        ShowOwnCanvas(true);
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
        ShowOwnCanvas(false);
        var input = InputManager.Instance; // null only while quitting
        if (input != null)
            input.ExitUIMode(this);
    }

    /// <summary>Show as a window inside the desktop. The desktop owns the input and Esc.</summary>
    public void ShowWindow()
    {
        if (_open || !isActiveAndEnabled)
            return;
        _hosted = true;
        BeginSession();
        ShowOwnCanvas(IsPanelInOwnCanvas()); // a BuiltIn desktop takes the panel; otherwise it stays here
    }

    /// <summary>Hide the desktop window. Safe to call twice.</summary>
    public void HideWindow()
    {
        if (!_hosted)
            return;
        _hosted = false;
        EndSession();
        ShowOwnCanvas(false);
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

    // One row per piece, built the first time the screen opens.
    void BuildRows()
    {
        if (_built)
            return;
        var manager = FurnitureManager.Instance;
        var catalog = manager != null ? manager.Catalog : null;
        if (catalog == null)
        {
            Debug.LogWarning($"{name}: no FurnitureManager/catalog yet.", this);
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

    void HandleStep(FurnitureDefinition def, int delta)
    {
        if (delta > 0)
        {
            var manager = FurnitureManager.Instance; // a locked row has no working + button: second guard
            if (manager != null && manager.GetBuyLockReason(def) == null)
                _cart.Add(def, 1);
        }
        else
            _cart.Remove(def, 1);
    }

    void HandleUnlock(FurnitureDefinition def)
    {
        var progression = ProgressionManager.Instance;
        if (progression != null && progression.TryUnlockFurniture(def))
            Say($"Unlocked {def.DisplayName}!");
        else
            Say("Couldn't unlock it.");
        Refresh();
    }

    void HandleCoins(int _) => Refresh();

    void HandleBuy()
    {
        var manager = FurnitureManager.Instance;
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
        var manager = FurnitureManager.Instance;
        int total = _cart.TotalCost();
        int coins = _wallet != null ? _wallet.GetCoins() : 0;

        foreach (var row in _rows)
        {
            var def = row.Definition;
            string requirement = manager != null ? manager.GetRequirementReason(def) : null;
            bool locked = manager != null && manager.GetBuyLockReason(def) != null;
            row.SetCount(_cart.GetCount(def));
            row.SetLocked(locked, requirement == null, def.UnlockCost, coins >= def.UnlockCost);
            row.SetDetail(requirement ?? (locked ? "Locked" : DetailText(manager, def)));
        }
        totalLabel.text = $"Total: {total}";
        coinsLabel.text = $"Coins: {coins}";
        buyButton.interactable =
            !_cart.IsEmpty && coins >= total && manager != null && AllBuyable(manager);
    }

    // A cart line can become locked after it was added (for example after a load).
    bool AllBuyable(FurnitureManager manager)
    {
        foreach (var line in _cart.Lines)
            if (manager.GetBuyLockReason(line.definition) != null)
                return false;
        return true;
    }

    static string DetailText(FurnitureManager manager, FurnitureDefinition def)
    {
        string text = $"Price {def.Price}";
        return manager != null ? $"{text}  |  sells back for {manager.GetRefund(def)}" : text;
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
            var manager = FurnitureManager.Instance;
            int pending = manager != null ? manager.PendingCrates : 0;
            text = pending > 0 ? $"Crates on the way: {pending}" : "";
        }
        if (text == _lastStatus)
            return;
        _lastStatus = text;
        statusLabel.text = text;
    }

    void OnValidate() => messageTime = Mathf.Max(0.5f, messageTime);
}
