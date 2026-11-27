using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The player's options (volume, mouse sensitivity, screen, vsync, quality) and their key rebinds,
/// in one global Easy Save file shared by every save slot. Created on first use and kept between scenes,
/// like InputManager. Apply changes a value now; Save writes it to disk.
/// </summary>
[DefaultExecutionOrder(-90)]
public class SettingsManager : MonoBehaviour
{
    static SettingsManager _instance;
    static bool _quitting;

    public static SettingsManager Instance
    {
        get
        {
            if (_instance == null && !_quitting)
                Create();
            return _instance;
        }
    }

    const string SettingsKey = "settings";
    const string RebindsKey = "rebinds";
    const float MinSensitivity = 0.1f;
    const float MaxSensitivity = 3f;

    GameSettingsData _data = new();

    /// <summary>The values in use now. Do not edit: copy it, change the copy, and call Apply.</summary>
    public GameSettingsData Current => _data;

    /// <summary>Raised after Apply changed the values.</summary>
    public event Action Changed;

    /// <summary>Uses the volume and look-sensitivity of a copy, so the settings screen can preview changes.</summary>
    public void Apply(GameSettingsData data)
    {
        if (data == null)
            return;
        _data = Sanitize(data.Clone());
        ApplyAll();
        Changed?.Invoke();
    }

    public void ResetToDefaults() => Apply(new GameSettingsData());

    /// <summary>Writes the current values to the settings file. False if the write failed.</summary>
    public bool Save()
    {
        try
        {
            ES3.Save(SettingsKey, _data, SaveStorage.GlobalSettings());
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{name}: could not save the settings ({e.Message}).", this);
            return false;
        }
    }

    /// <summary>The saved key rebinds (JSON), or null if there are none.</summary>
    public string LoadRebinds()
    {
        try
        {
            var settings = SaveStorage.GlobalSettings();
            if (ES3.FileExists(settings) && ES3.KeyExists(RebindsKey, settings))
                return ES3.Load<string>(RebindsKey, settings);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{name}: could not read the key rebinds ({e.Message}).", this);
        }
        return null;
    }

    public void SaveRebinds(string json)
    {
        try
        {
            ES3.Save(RebindsKey, json, SaveStorage.GlobalSettings());
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{name}: could not save the key rebinds ({e.Message}).", this);
        }
    }

    // Reset statics for "Enter Play Mode without Domain Reload".
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _instance = null;
        _quitting = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        _ = Instance;
    }

    static void Create() => new GameObject("[SettingsManager]").AddComponent<SettingsManager>();

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        transform.SetParent(null); // DontDestroyOnLoad needs a root object
        DontDestroyOnLoad(gameObject);

        LoadFromFile();
        ApplyAll();
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnApplicationQuit() => _quitting = true;

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (_instance == this)
            _instance = null;
    }

    // A new scene may bring a new InputManager, which starts with the default sensitivity.
    void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyLookSensitivity();

    void LoadFromFile()
    {
        try
        {
            var settings = SaveStorage.GlobalSettings();
            if (ES3.FileExists(settings) && ES3.KeyExists(SettingsKey, settings))
            {
                var loaded = ES3.Load<GameSettingsData>(SettingsKey, settings);
                if (loaded != null)
                    _data = Sanitize(loaded);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{name}: could not read the settings, using the defaults ({e.Message}).", this);
        }
    }

    static GameSettingsData Sanitize(GameSettingsData data)
    {
        data.masterVolume = Mathf.Clamp01(data.masterVolume);
        data.mouseSensitivity = Mathf.Clamp(data.mouseSensitivity, MinSensitivity, MaxSensitivity);
        data.screenWidth = Mathf.Max(0, data.screenWidth);
        data.screenHeight = Mathf.Max(0, data.screenHeight);
        if (!Enum.IsDefined(typeof(FullScreenMode), data.fullScreenMode))
            data.fullScreenMode = (int)FullScreenMode.FullScreenWindow;
        if (data.qualityLevel >= QualitySettings.names.Length)
            data.qualityLevel = -1;
        return data;
    }

    void ApplyAll()
    {
        AudioListener.volume = _data.masterVolume;

        // Quality first: a quality level carries its own vsync, which the setting then overrides.
        if (_data.qualityLevel >= 0)
            QualitySettings.SetQualityLevel(_data.qualityLevel, true);
        QualitySettings.vSyncCount = _data.vsync ? 1 : 0;

        var mode = (FullScreenMode)_data.fullScreenMode;
        if (_data.screenWidth > 0 && _data.screenHeight > 0)
            Screen.SetResolution(_data.screenWidth, _data.screenHeight, mode);
        else
            Screen.fullScreenMode = mode;

        ApplyLookSensitivity();
    }

    void ApplyLookSensitivity()
    {
        var input = InputManager.Instance; // null only while quitting
        if (input != null)
            input.LookSensitivity = _data.mouseSensitivity;
    }
}
