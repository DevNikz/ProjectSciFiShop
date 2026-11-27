using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Names of the screens the router can open.</summary>
public enum ScreenId
{
    Orders,
    Furniture,
    Progression,
    Desktop,
    SaveLoad,
    Settings,
}

/// <summary>A full-screen UI that takes the cursor while open. Implemented by OrderScreen, ProgressionScreen.</summary>
public interface IScreen
{
    ScreenId Id { get; }
    bool IsOpen { get; }
    void Open();
    void Close();
}

/// <summary>
/// Lets screens open each other without knowing each other. A screen registers itself while enabled,
/// asks for another by ScreenId, and can go back to the one that sent it. Created on first use, like InputManager.
/// </summary>
public class UIScreenRouter : MonoBehaviour
{
    static UIScreenRouter _instance;
    static bool _quitting;

    /// <summary>The router, created on first use. Null only while quitting.</summary>
    public static UIScreenRouter Instance
    {
        get
        {
            if (_instance == null && !_quitting)
                Create();
            return _instance;
        }
    }

    /// <summary>The router if one exists. Never creates one: use it in OnDisable and Close.</summary>
    public static UIScreenRouter Existing => _instance;

    readonly Dictionary<ScreenId, IScreen> _screens = new();
    IScreen _returnTo; // the screen Back goes to
    bool _switching; // true while the router itself is closing and opening screens

    /// <summary>A screen was registered or unregistered.</summary>
    public event Action Changed;

    // Reset statics for "Enter Play Mode without Domain Reload".
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _instance = null;
        _quitting = false;
    }

    static void Create() => new GameObject("[UIScreenRouter]").AddComponent<UIScreenRouter>();

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
    }

    void OnApplicationQuit() => _quitting = true;

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    /// <summary>Call from OnEnable. A second screen with the same id replaces the first.</summary>
    public void Register(IScreen screen)
    {
        if (screen == null)
            return;
        _screens[screen.Id] = screen;
        Changed?.Invoke();
    }

    /// <summary>Call from OnDisable. Safe if it was never registered.</summary>
    public void Unregister(IScreen screen)
    {
        if (screen == null)
            return;
        if (!_screens.TryGetValue(screen.Id, out var current) || current != screen)
            return;
        _screens.Remove(screen.Id);
        if (_returnTo == screen)
            _returnTo = null;
        Changed?.Invoke();
    }

    public bool IsRegistered(ScreenId id) => _screens.ContainsKey(id);

    /// <summary>True if Back from this screen has somewhere to go.</summary>
    public bool CanGoBack(IScreen from) => _returnTo != null && _returnTo != from;

    /// <summary>
    /// Closes <paramref name="from"/> and opens the target, remembering <paramref name="from"/> for GoBack.
    /// False (nothing changed) if the target is missing or did not open.
    /// </summary>
    public bool TryShow(ScreenId target, IScreen from)
    {
        if (!_screens.TryGetValue(target, out var screen))
        {
            Debug.LogWarning($"{name}: no screen registered for {target}.", this);
            return false;
        }
        if (screen == from)
            return false;

        _switching = true;
        var previous = _returnTo;
        _returnTo = from; // set first: the target reads CanGoBack while opening
        from?.Close();
        screen.Open();
        bool opened = screen.IsOpen;
        if (!opened)
        {
            _returnTo = previous;
            from?.Open(); // never leave the player with no screen
            Debug.LogWarning($"{name}: {target} did not open.", this);
        }
        _switching = false;
        return opened;
    }

    /// <summary>Closes <paramref name="from"/> and reopens the screen that sent it, if any.</summary>
    public void GoBack(IScreen from)
    {
        var target = _returnTo;
        _returnTo = null;

        _switching = true;
        from?.Close();
        if (target != null)
        {
            target.Open();
            if (!target.IsOpen)
                Debug.LogWarning($"{name}: {target.Id} did not open.", this);
        }
        _switching = false;
    }

    /// <summary>Screens call this from Close. A close the router did not cause ends the Back chain.</summary>
    public void NotifyClosed(IScreen screen)
    {
        if (!_switching)
            _returnTo = null;
    }
}
