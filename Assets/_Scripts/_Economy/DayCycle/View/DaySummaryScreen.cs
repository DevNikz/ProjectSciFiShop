using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>End-of-day summary: sales, revenue, cost and profit, with a button for the next day. Needs an EventSystem.</summary>
public class DaySummaryScreen : MonoBehaviour
{
    [SerializeField]
    DayManager manager;

    [Tooltip("Shown while open. Must be a child: this object has to stay active.")]
    [SerializeField]
    GameObject panel;

    [SerializeField]
    TMP_Text dayLabel;

    [SerializeField]
    TMP_Text profitLabel;

    [Header("Optional labels")]
    [SerializeField]
    TMP_Text salesLabel;

    [SerializeField]
    TMP_Text itemsLabel;

    [SerializeField]
    TMP_Text revenueLabel;

    [SerializeField]
    TMP_Text costLabel;

    [SerializeField]
    Button nextDayButton;

    [Header("Colors")]
    [SerializeField]
    Color32 profitColor = new(0x3F, 0xB6, 0x8B, 0xFF);

    [SerializeField]
    Color32 lossColor = new(0xFF, 0x6B, 0x6B, 0xFF);

    bool _open;

    public bool IsOpen => _open;

    void OnEnable()
    {
        if (
            manager == null
            || panel == null
            || dayLabel == null
            || profitLabel == null
            || nextDayButton == null
            || panel == gameObject
        )
        {
            Debug.LogError($"{name}: manager/panel/labels/button missing, or panel is this object.", this);
            enabled = false;
            return;
        }
        manager.DayEnded += Open;
        manager.DayStarted += HandleDayStarted;
        manager.StateRestored += Close; // a load replaces whatever day state was showing
        nextDayButton.onClick.AddListener(HandleNextDay);
        panel.SetActive(false);
    }

    void OnDisable()
    {
        Close(); // never leave the cursor free or input blocked
        if (manager != null)
        {
            manager.DayEnded -= Open;
            manager.DayStarted -= HandleDayStarted;
            manager.StateRestored -= Close;
        }
        if (nextDayButton != null)
            nextDayButton.onClick.RemoveListener(HandleNextDay);
    }

    void Open(DaySummary s)
    {
        if (_open)
            return;
        var input = InputManager.Instance;
        if (input == null)
            return;

        input.EnterUIMode(this);
        _open = true;
        panel.SetActive(true);

        dayLabel.text = $"Day {s.day}";
        SetText(salesLabel, $"Customers served: {s.sales}");
        SetText(itemsLabel, $"Items sold: {s.items}");
        SetText(revenueLabel, $"Revenue: {s.revenue}");
        SetText(costLabel, $"Cost of goods: {s.cost}");
        profitLabel.text = $"Profit: {s.Profit:+0;-0;0}";
        profitLabel.color = s.Profit < 0 ? lossColor : profitColor;
    }

    public void Close()
    {
        if (!_open)
            return;
        _open = false;
        if (panel != null) // gone on scene unload
            panel.SetActive(false);
        var input = InputManager.Instance; // null only while quitting
        if (input != null)
            input.ExitUIMode(this);
    }

    void HandleNextDay()
    {
        // Return to stocking time. The player explicitly opens the store when ready.
        Close();
    }

    void HandleDayStarted(int day) => Close();

    static void SetText(TMP_Text label, string text)
    {
        if (label != null)
            label.text = text;
    }
}
