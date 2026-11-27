using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// What the slot list shows about a save. Stored in each slot file under the "meta" key, so listing
/// slots never loads the whole save.
/// </summary>
[Serializable]
public class SaveSlotInfo
{
    [Tooltip("File name of the slot (no folder, no extension). Set from the file, never shown.")]
    public string slotId;

    public string displayName;

    [Tooltip("UTC time of the save, ISO 8601.")]
    public string savedAtUtc;

    public int day;
    public int coins;
    public int version;

    [ES3NonSerializable]
    public bool IsAutosave => slotId == SaveManager.AutosaveSlot;

    [ES3NonSerializable]
    public bool IsCompatible => version == SaveManager.CurrentVersion;

    /// <summary>The save time in the player's time zone, or "?" if it can't be read.</summary>
    public string GetLocalTimeText() =>
        DateTime.TryParse(
            savedAtUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var utc
        )
            ? utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : "?";
}
