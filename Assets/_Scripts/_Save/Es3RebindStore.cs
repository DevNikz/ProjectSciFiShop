using UnityEngine;

/// <summary>Keeps the FPSCore key rebinds in the global settings file instead of a loose JSON file.</summary>
public sealed class Es3RebindStore : IRebindStore
{
    public string Load() => SettingsManager.Instance != null ? SettingsManager.Instance.LoadRebinds() : null;

    public void Save(string json)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SaveRebinds(json);
    }

    // Before any scene object wakes up, so InputRemapper reads its rebinds from here.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register() => InputRemapper.Store = new Es3RebindStore();
}
