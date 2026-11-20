/// <summary>
/// Put on anything that must finish before a closing store can end the day (customers, the
/// register). Register with DayManager.RegisterBlocker when enabled, and unregister when disabled.
/// </summary>
public interface IDayEndBlocker
{
    /// <summary>False while this still needs the day to stay open.</summary>
    bool CanDayEnd { get; }
}
