using UnityEngine;

/// <summary>
/// Makes DayManager the single owner of the in-game clock. While the store is open the hour
/// moves from openHour to closeHour in step with DayManager.TimeLeft; Closing holds closeHour;
/// between days the world runs through the night to openHour so the next day starts in the morning.
/// Sets autoAdvance = false on the cycle. Does not touch DayManager or save data (the hour is
/// derived from DayManager state, so loads restore it for free).
/// </summary>
public class TimeOfDayDriver : MonoBehaviour
{
    [SerializeField]
    DayManager manager;

    [SerializeField]
    TimeOfDayCycle cycle;

    [Header("Mapping")]
    [Tooltip(
        "Hour shown when the store opens. Should sit at or after the Morning phase startHour."
    )]
    [Range(0f, 24f)]
    [SerializeField]
    float openHour = 8f;

    [Tooltip("Hour reached when the timer runs out. 19.5 matches the default Evening phase.")]
    [Range(0f, 24f)]
    [SerializeField]
    float closeHour = 19.5f;

    [Header("Between days")]
    [Tooltip(
        "Run closeHour -> night -> openHour while the summary is up. Off = hold the evening look until the next day starts, then snap."
    )]
    [SerializeField]
    bool animateNight = true;

    [Min(0.1f)]
    [SerializeField]
    float nightSeconds = 6f;

    float _nightT = 1f; // 0 = just closed, 1 = morning ready
    bool _restored;

    void OnEnable()
    {
        if (manager == null || cycle == null)
        {
            Debug.LogError($"{name}: DayManager or TimeOfDayCycle missing.", this);
            enabled = false;
            return;
        }
        cycle.autoAdvance = false; // one writer: this component
        manager.DayEnded += HandleDayEnded;
        manager.StateRestored += HandleRestored;
        Apply();
    }

    void OnDisable()
    {
        if (manager == null)
            return;
        manager.DayEnded -= HandleDayEnded;
        manager.StateRestored -= HandleRestored;
    }

    // Resume() raises StateRestored and then (between days) DayEnded in the same call, so the
    // flag lets the second one snap instead of replaying the night.
    void HandleRestored() => _restored = true;

    void HandleDayEnded(DaySummary _)
    {
        // Loaded between days: show morning. Fresh day end: start at the evening look, then
        // animate (or hold) until the next day starts.
        _nightT = _restored ? 1f : 0f;
    }

    void Update()
    {
        if (manager.Phase == StorePhase.Waiting && manager.Day > 0 && animateNight)
            _nightT = Mathf.Min(1f, _nightT + Time.deltaTime / nightSeconds);

        Apply();
        _restored = false;
    }

    void Apply()
    {
        float hour;
        switch (manager.Phase)
        {
            case StorePhase.Open:
                float len = Mathf.Max(1f, manager.DayLength);
                float t = Mathf.Clamp01(1f - manager.TimeLeft / len);
                hour = Mathf.Lerp(openHour, closeHour, t);
                break;
            case StorePhase.Closing:
                hour = closeHour;
                break;
            default: // Waiting
                if (manager.Day <= 0)
                    hour = openHour; // before day 1
                else
                    // Evening -> next morning; the cycle wraps past 24 and fades through night.
                    hour = Mathf.Lerp(closeHour, openHour + 24f, _nightT);
                break;
        }
        cycle.SetTime(hour);
    }

    void OnValidate()
    {
        nightSeconds = Mathf.Max(0.1f, nightSeconds);
    }
}
