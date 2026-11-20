using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Upgrades and store expansions: see the cost, buy. It never changes progression itself, it only calls
/// ProgressionManager.Try* and redraws when it changes. Needs an EventSystem in the scene.
/// </summary>
public class ProgressionScreen : MonoBehaviour, IScreen
{
    [Header("Layout")]
    [Tooltip("Shown while open. Must be a child: this object has to stay active.")]
    [SerializeField]
    GameObject panel;

    [Tooltip("Rows are created here (e.g. a ScrollRect content).")]
    [SerializeField]
    Transform rowParent;

    [SerializeField]
    ProgressionRow rowPrefab;

    [Header("Labels and buttons")]
    [SerializeField]
    TMP_Text coinsLabel;

    [Tooltip("Optional. Purchase results.")]
    [SerializeField]
    TMP_Text statusLabel;

    [SerializeField]
    Button closeButton;

    [Tooltip("Optional. Goes back to the screen that opened this one. Hidden when there is none.")]
    [SerializeField]
    Button backButton;

    [Tooltip("Seconds a purchase result stays in the status line.")]
    [SerializeField]
    float messageTime = 2f;

    struct UpgradeRow
    {
        public ProgressionRow row;
        public UpgradeDefinition def;
    }

    struct ExpansionRow
    {
        public ProgressionRow row;
        public ExpansionDefinition def;
    }

    readonly List<UpgradeRow> _upgradeRows = new();
    readonly List<ExpansionRow> _expansionRows = new();
    ProgressionManager _progression;
    CurrencyManager _wallet;
    bool _open,
        _built;
    string _message,
        _lastStatus;
    float _messageUntil;

    public ScreenId Id => ScreenId.Progression;
    public bool IsOpen => _open;

    void OnEnable()
    {
        if (
            panel == null
            || panel == gameObject
            || rowParent == null
            || rowPrefab == null
            || coinsLabel == null
            || closeButton == null
        )
        {
            Debug.LogError(
                $"{name}: layout/labels/buttons missing, or panel is this object.",
                this
            );
            enabled = false;
            return;
        }
        closeButton.onClick.AddListener(Close);
        if (backButton != null)
        {
            backButton.onClick.AddListener(HandleBack);
            backButton.gameObject.SetActive(false); // Open() decides
        }
        var router = UIScreenRouter.Instance; // null only while quitting
        if (router != null)
            router.Register(this);
        panel.SetActive(false);
    }

    void OnDisable()
    {
        Close(); // never leave the cursor free or input blocked
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);
        if (backButton != null)
            backButton.onClick.RemoveListener(HandleBack);
        var router = UIScreenRouter.Existing; // never create one while shutting down
        if (router != null)
            router.Unregister(this);
    }

    /// <summary>Open the screen. Does nothing if already open or there is no ProgressionManager.</summary>
    public void Open()
    {
        if (_open || !isActiveAndEnabled)
            return;
        var input = InputManager.Instance;
        if (input == null)
            return;
        _progression = ProgressionManager.Instance;
        if (_progression == null)
        {
            Debug.LogWarning($"{name}: no ProgressionManager in the scene.", this);
            return;
        }

        BuildRows();
        input.EnterUIMode(this);
        _open = true;
        panel.SetActive(true);
        UpdateBackButton();

        _progression.Changed += Refresh;
        _wallet = CurrencyManager.Instance;
        if (_wallet != null)
            _wallet.OnCoinsChanged += HandleCoins;
        Refresh();
    }

    public void Close()
    {
        if (!_open)
            return;
        _open = false;

        if (_progression != null)
            _progression.Changed -= Refresh;
        _progression = null;
        if (_wallet != null)
            _wallet.OnCoinsChanged -= HandleCoins;
        _wallet = null;

        if (panel != null) // gone on scene unload
            panel.SetActive(false);
        var input = InputManager.Instance; // null only while quitting
        if (input != null)
            input.ExitUIMode(this);
        var router = UIScreenRouter.Existing;
        if (router != null)
            router.NotifyClosed(this);
    }

    void Update()
    {
        if (!_open)
            return;
        var input = InputManager.Instance;
        if (input != null && input.UICancelPressed)
        {
            Close();
            return;
        }
        UpdateStatus();
    }

    // The router closes this screen (handing the input back) before the previous one opens.
    void HandleBack()
    {
        var router = UIScreenRouter.Instance;
        if (router != null)
            router.GoBack(this);
        else
            Close();
    }

    void UpdateBackButton()
    {
        if (backButton == null)
            return;
        var router = UIScreenRouter.Existing;
        backButton.gameObject.SetActive(router != null && router.CanGoBack(this));
    }

    // One row per upgrade, then one per expansion. Built the first time the screen opens.
    void BuildRows()
    {
        if (_built)
            return;

        foreach (var def in _progression.Upgrades)
        {
            if (def == null)
                continue;
            var row = Instantiate(rowPrefab, rowParent);
            row.Bind("Upgrade", def.DisplayName, def.Description, () => HandleBuyUpgrade(def));
            _upgradeRows.Add(new UpgradeRow { row = row, def = def });
        }
        foreach (var def in _progression.Expansions)
        {
            if (def == null)
                continue;
            var row = Instantiate(rowPrefab, rowParent);
            row.Bind("Expansion", def.DisplayName, def.Description, () => HandleBuyExpansion(def));
            _expansionRows.Add(new ExpansionRow { row = row, def = def });
        }

        if (_upgradeRows.Count == 0 && _expansionRows.Count == 0)
            Debug.LogWarning(
                $"{name}: ProgressionManager has no upgrades or expansions to list.",
                this
            );
        _built = true;
    }

    void HandleBuyUpgrade(UpgradeDefinition def)
    {
        bool ok = _progression != null && _progression.TryBuyUpgrade(def);
        Say(ok ? $"Bought {def.DisplayName}!" : "Couldn't buy it.");
        Refresh();
    }

    void HandleBuyExpansion(ExpansionDefinition def)
    {
        bool ok = _progression != null && _progression.TryBuyExpansion(def);
        Say(ok ? $"Bought {def.DisplayName}!" : "Couldn't buy it.");
        Refresh();
    }

    void HandleCoins(int _) => Refresh();

    void Refresh()
    {
        if (_progression == null)
            return;
        int coins = _wallet != null ? _wallet.GetCoins() : 0;
        coinsLabel.text = $"Coins: {coins}";

        foreach (var u in _upgradeRows)
        {
            int level = _progression.GetUpgradeLevel(u.def);
            string effect = level > 0 ? $"  (x{u.def.MultiplierAt(level):0.##})" : "";
            string status = $"Level {level}/{u.def.LevelCount}{effect}";
            if (_progression.TryGetNextUpgradeCost(u.def, out int cost))
                u.row.SetState(status, true, $"Buy: {cost}", coins >= cost);
            else
                u.row.SetState(status + " - max", false, "", false);
        }

        foreach (var e in _expansionRows)
        {
            if (_progression.IsExpansionBought(e.def))
                e.row.SetState("Owned", false, "", false);
            else
                e.row.SetState("Not bought", true, $"Buy: {e.def.Cost}", coins >= e.def.Cost);
        }
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
        string text = Time.unscaledTime < _messageUntil ? _message : "";
        if (text == _lastStatus)
            return;
        _lastStatus = text;
        statusLabel.text = text;
    }

    void OnValidate() => messageTime = Mathf.Max(0.5f, messageTime);
}
