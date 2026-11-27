/// <summary>
/// Optional home for the player's key rebinds. Set InputRemapper.Store to use it instead of the
/// keybinds file in the persistent data folder. The content is a JSON string owned by InputRemapper.
/// </summary>
public interface IRebindStore
{
    /// <summary>The saved rebinds, or null/empty if there are none yet.</summary>
    string Load();

    void Save(string json);
}
