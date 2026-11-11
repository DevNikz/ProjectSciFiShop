using TMPro;
using UnityEngine;

/// <summary>
/// HUD day clock. Shows the day number, the store phase ("Open" / "Closing" / "Closed") and,
/// optionally, the in-game time of day. It listens to DayManager and TimeOfDayCycle events,
/// so it only redraws when something actually changes.
/// </summary>
public class HudClock : MonoBehaviour
{
    [Tooltip("Shows the store phase text.")]
    [SerializeField]
    TMP_Text timeLabel;

    [Tooltip("Optional. Shows the day number.")]
    [SerializeField]
    TMP_Text dayLabel;

    [Tooltip("{0} = day number. Hidden before day 1.")]
    [SerializeField]
    string dayFormat = "DAY {0}";

    [Tooltip("Shown while the store is open.")]
    [SerializeField]
    string openingText = "Open";

    [Tooltip("Shown while the store is closing (waiting for the register to finish).")]
    [SerializeField]
    string closingText = "Closing";

    [Tooltip("Shown between days.")]
    [SerializeField]
    string closedText = "Closed";

    [Header("In-game clock (optional)")]
    [Tooltip("Shows the in-game time of day, e.g. 8:40 AM. Empty = no clock.")]
    [SerializeField]
    TMP_Text clockLabel;

    [SerializeField]
    bool use24Hour;

    DayManager _day;
    TimeOfDayCycle _cycle;
    bool _started;

    // Managers may not exist yet in OnEnable on the first frame, so the first subscribe is in Start.
    void Start()
    {
        _started = true;
        Subscribe();
        RefreshAll();
    }

    void OnEnable()
    {
        if (!_started)
            return;
        Subscribe();
        RefreshAll();
    }

    void OnDisable() => Unsubscribe();

    void Subscribe()
    {
        Unsubscribe(); // never subscribe twice

        _day = DayManager.Instance;
        if (_day != null)
        {
            _day.DayChanged += OnDayChanged;
            _day.PhaseChanged += OnPhaseChanged;
        }
        else
            Debug.LogWarning(
                $"{name}: DayManager missing, so the day and phase labels are off.",
                this
            );

        if (clockLabel == null)
            return;
        _cycle = TimeOfDayCycle.Instance;
        if (_cycle != null)
            _cycle.MinuteChanged += OnMinuteChanged;
        else
            Debug.LogWarning($"{name}: TimeOfDayCycle missing, so the in-game clock is off.", this);
    }

    void Unsubscribe()
    {
        if (_day != null)
        {
            _day.DayChanged -= OnDayChanged;
            _day.PhaseChanged -= OnPhaseChanged;
            _day = null;
        }
        if (_cycle != null)
        {
            _cycle.MinuteChanged -= OnMinuteChanged;
            _cycle = null;
        }
    }

    // Draws the current state once, because events only fire on change.
    void RefreshAll()
    {
        if (_day != null)
        {
            OnDayChanged(_day.Day);
            OnPhaseChanged(_day.Phase);
        }
        if (_cycle != null)
            OnMinuteChanged(_cycle.MinuteOfDay);
    }

    void OnDayChanged(int day)
    {
        if (dayLabel != null)
            dayLabel.text = day >= 1 ? string.Format(dayFormat, day) : "";
    }

    void OnPhaseChanged(StorePhase phase)
    {
        if (timeLabel == null)
            return;
        timeLabel.text =
            phase == StorePhase.Open ? openingText
            : phase == StorePhase.Closing ? closingText
            : closedText;
    }

    void OnMinuteChanged(int minuteOfDay)
    {
        if (clockLabel == null)
            return;
        int h = minuteOfDay / 60;
        int m = minuteOfDay % 60;
        clockLabel.text = use24Hour
            ? $"{h:00}:{m:00}"
            : $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h < 12 ? "AM" : "PM")}";
    }

    void OnValidate()
    {
        if (string.IsNullOrEmpty(dayFormat))
            dayFormat = "DAY {0}";
    }
}
