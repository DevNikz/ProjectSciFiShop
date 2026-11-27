using System;
using UnityEngine;

/// <summary>
/// The player's options. Saved in the global settings file, shared by every save slot.
/// Zero or -1 in a screen or quality field means "keep what the game has now".
/// </summary>
[Serializable]
public class GameSettingsData
{
    [Range(0f, 1f)]
    public float masterVolume = 1f;

    [Tooltip("Multiplier on mouse look. 1 = the default feel.")]
    public float mouseSensitivity = 1f;

    public int screenWidth;
    public int screenHeight;
    public int fullScreenMode = (int)FullScreenMode.FullScreenWindow;
    public bool vsync = true;
    public int qualityLevel = -1;

    public GameSettingsData Clone() => (GameSettingsData)MemberwiseClone();
}
