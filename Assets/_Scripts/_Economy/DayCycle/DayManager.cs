using System;
using System.Collections.Generic;
using UnityEngine;

public enum StorePhase
{
    Waiting, // between days (summary showing, or before the first day)
    Open, // customers may come in
    Closing, // timer is up: waiting for the register to finish
}

[Serializable]
public struct DaySummary
{
    public int day;
    public int sales; // customers served
    public int items;
    public int revenue;
    public int cost; // what the shop paid for the items sold
    public int Profit => revenue - cost;
}

//Save Data
[Serializable]
public class DayData
{
    public int day;
    public int phase; // StorePhase
    public float timer;
    public int sales,
        items,
        revenue,
        cost;
    public DaySummary lastSummary; // shown again when a save is loaded between days
}

/// <summary>
/// A simple day timer: open, close, then a summary. Customer AI should only spawn while
/// DayManager.Instance.IsOpen. The player opens each day after stocking.
/// It knows no other game scripts: sales come in through RecordSale, the day waits for every
/// registered IDayEndBlocker, and everything else listens to the events.
/// </summary>
[DefaultExecutionOrder(-50)]
public class DayManager : MonoBehaviour
{
    public static DayManager Instance;

    [Tooltip("How long the store is open each day (s).")]
    [SerializeField]
    private float dayLengthSeconds = 300f;

    [Tooltip("Start day 1 as soon as the scene starts.")]
    [SerializeField]
    private bool startFirstDayOnStart;

    private StorePhase _phase = StorePhase.Waiting;
    private int _day;
    private float _timer;
    private float nextClosingCheck;
    private int _sales,
        _items,
        _revenue,
        _cost;
    private DaySummary _lastSummary;
    private readonly List<IDayEndBlocker> _blockers = new();

    public int Day => _day;
    public StorePhase Phase => _phase;
    public bool IsOpen => _phase == StorePhase.Open;
    public float DayLength => dayLengthSeconds;

    /// <summary>Seconds of opening time left (0 when not open).</summary>
    public float TimeLeft => _phase == StorePhase.Open ? Mathf.Max(0f, _timer) : 0f;

    /// <summary>Raised first when a day starts, before DayStarted. For systems that release things on a new day.</summary>
    public event Action<int> DayStarting; // day number

    public event Action<int> DayStarted; // day number
    public event Action<int> DayChanged;
    public event Action<StorePhase> PhaseChanged;
    public event Action StoreOpened;
    public event Action StoreClosed;
    public event Action<DaySummary> DayEnded;

    /// <summary>Raised by Resume() after a load, so screens can drop anything they were showing.</summary>
    public event Action StateRestored;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this; // not DontDestroyOnLoad: it points at scene objects
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        if (startFirstDayOnStart && _day == 0)
            StartNextDay();
    }

    /// <summary>Opens the next day. False unless the previous day has ended.</summary>
    public bool StartNextDay()
    {
        if (!isActiveAndEnabled || _phase != StorePhase.Waiting)
            return false;

        _timer = dayLengthSeconds; // before the setters, so listeners see a ready clock
        SetDay(_day + 1);
        SetPhase(StorePhase.Open);
        DayStarting?.Invoke(_day);
        DayStarted?.Invoke(_day);
        StoreOpened?.Invoke();
        return true;
    }

    private void Update()
    {
        switch (_phase)
        {
            case StorePhase.Open:
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                    CloseStore();
                break;
            case StorePhase.Closing:
                if (Time.time < nextClosingCheck)
                    break;
                nextClosingCheck = Time.time + 0.5f;
                // Allow shoppers and the entire queue to finish, not just the current sale.
                bool isBlocked = false;
                foreach (var blocker in _blockers)
                    if (!blocker.CanDayEnd)
                    {
                        isBlocked = true;
                        break;
                    }
                if (!isBlocked)
                    EndDay();
                break;
        }
    }

    public void CloseStore()
    {
        if (_phase != StorePhase.Open)
            return;
        _timer = 0f;
        nextClosingCheck = 0f;
        SetPhase(StorePhase.Closing);
        StoreClosed?.Invoke();
    }

    private void EndDay()
    {
        var summary = new DaySummary
        {
            day = _day,
            sales = _sales,
            items = _items,
            revenue = _revenue,
            cost = _cost,
        };
        _sales = _items = _revenue = _cost = 0; // a late sale counts toward the next day
        _lastSummary = summary;
        SetPhase(StorePhase.Waiting);
        DayEnded?.Invoke(summary);
    }

    /// <summary>Counts one finished sale toward the current day. Called by the register.</summary>
    public void RecordSale(int items, int revenue, int cost)
    {
        _sales++;
        _items += items;
        _revenue += revenue;
        _cost += cost;
    }

    /// <summary>While any registered blocker has CanDayEnd false, a closing store keeps waiting.</summary>
    public void RegisterBlocker(IDayEndBlocker blocker)
    {
        if (blocker == null)
        {
            Debug.LogWarning($"{name}: RegisterBlocker needs a blocker.", this);
            return;
        }
        if (!_blockers.Contains(blocker))
            _blockers.Add(blocker);
    }

    /// <summary>Safe to call twice, or for a blocker that was never registered.</summary>
    public void UnregisterBlocker(IDayEndBlocker blocker) => _blockers.Remove(blocker);

    public DayData CaptureState() =>
        new DayData
        {
            day = _day,
            phase = (int)_phase,
            timer = _timer,
            sales = _sales,
            items = _items,
            revenue = _revenue,
            cost = _cost,
            lastSummary = _lastSummary,
        };

    /// <summary>
    /// Restores the clock and counters. Raises only DayChanged and PhaseChanged (when they
    /// differ), so the HUD follows. Call Resume() after for the rest.
    /// </summary>
    public void RestoreState(DayData state)
    {
        if (state == null)
            return;
        _timer = Mathf.Max(0f, state.timer);
        _sales = Mathf.Max(0, state.sales);
        _items = Mathf.Max(0, state.items);
        _revenue = Mathf.Max(0, state.revenue);
        _cost = Mathf.Max(0, state.cost);
        _lastSummary = state.lastSummary;
        // Last, so listeners reading the counters or timer see the restored values.
        SetDay(Mathf.Max(0, state.day));
        SetPhase(
            Enum.IsDefined(typeof(StorePhase), state.phase)
                ? (StorePhase)state.phase
                : StorePhase.Waiting
        );
    }

    /// <summary>
    /// Call after RestoreState. Tells screens a load happened, and brings the summary back up
    /// if the save was made between days.
    /// </summary>
    public void Resume()
    {
        StateRestored?.Invoke();
        if (_phase == StorePhase.Waiting && _day > 0)
            DayEnded?.Invoke(_lastSummary);
    }

    // Route every write to Day and Phase through these, including RestoreState.
    void SetDay(int value)
    {
        if (Day == value)
            return;
        _day = value;
        DayChanged?.Invoke(value);
    }

    void SetPhase(StorePhase value)
    {
        if (Phase == value)
            return;
        _phase = value;
        PhaseChanged?.Invoke(value);
    }

    [ContextMenu("Debug: close the store now")]
    private void DebugCloseNow()
    {
        if (Application.isPlaying && _phase == StorePhase.Open)
            _timer = 0f;
    }

    private void OnValidate()
    {
        dayLengthSeconds = Mathf.Max(1f, dayLengthSeconds);
    }
}
