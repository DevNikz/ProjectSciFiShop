using UnityEngine;

/// <summary>
/// Carries "which slot to load" from the main menu scene to the game scene. SaveManager takes it
/// when the game scene starts. Nothing pending = a new game.
/// </summary>
public static class SaveSession
{
    public static string PendingSlot { get; private set; }

    public static void RequestLoad(string slotId) => PendingSlot = slotId;

    public static void RequestNewGame() => PendingSlot = null;

    /// <summary>True (and the request is cleared) if a slot was asked for.</summary>
    public static bool TryConsume(out string slotId)
    {
        slotId = PendingSlot;
        PendingSlot = null;
        return !string.IsNullOrEmpty(slotId);
    }

    // Reset statics for "Enter Play Mode without Domain Reload".
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => PendingSlot = null;
}
