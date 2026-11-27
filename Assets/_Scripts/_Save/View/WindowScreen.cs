using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared base of the save/load and settings screens: a window that can open on its own (own canvas, own
/// cursor) or as an app inside the DesktopScreen (the desktop owns the cursor and Esc). Same pattern as
/// OrderScreen and FurnitureScreen. BuiltIn mode makes the window in code; Custom mode uses the scene
/// objects assigned on the child screen.
/// </summary>
public abstract class WindowScreen : MonoBehaviour, IScreen, IDesktopApp
{
    [Header("Layout mode")]
    [Tooltip("BuiltIn: the window and its controls are made in code. Custom: use the scene objects below.")]
    [SerializeField]
    UILayoutMode mode = UILayoutMode.BuiltIn;

    [Tooltip("Look of the built-in layout. Empty = the default look (same as the desktop window).")]
    [SerializeField]
    UIStyle style;

    [Tooltip("Built-in window title when opened on its own. Empty = the screen's default title.")]
    [SerializeField]
    string windowTitle;

    [Header("Custom layout (Custom mode only)")]
    [Tooltip("Shown while open. Must be a child: this object has to stay active.")]
    [SerializeField]
    GameObject panel;

    [Tooltip("Optional. Closes the screen.")]
    [SerializeField]
    Button closeButton;

    bool _open,
        _hosted; // shown as a desktop window: the desktop owns the input and Esc
    GameObject _canvasRoot; // BuiltIn: this screen's own canvas, shown only when not inside the desktop

    public abstract ScreenId Id { get; }
    public bool IsOpen => _open;
    public bool IsWindowOpen => _hosted && _open;
    public Transform WindowRoot => panel != null ? panel.transform : null;

    public event Action WindowClosed;

    protected UILayoutMode Mode => mode;
    protected UIStyle Look => style != null ? style : UIStyle.Fallback;
    protected abstract string DefaultTitle { get; }

    /// <summary>BuiltIn: fill the panel with controls and keep references to them. False = give up.</summary>
    protected abstract bool TryBuildContent(RectTransform panelRect, UIStyle look);

    /// <summary>Custom: true if every required scene reference is assigned.</summary>
    protected abstract bool HasCustomReferences();

    /// <summary>Called when the window becomes visible (on its own or in the desktop).</summary>
    protected abstract void OnSessionBegin();

    /// <summary>Called when the window is hidden.</summary>
    protected abstract void OnSessionEnd();

    protected virtual void OnEnable()
    {
        if (mode == UILayoutMode.BuiltIn && !TryBuildLayout())
        {
            enabled = false;
            return;
        }
        if (panel == null || !HasCustomReferences())
        {
            Debug.LogError($"{name}: layout references missing.", this);
            enabled = false;
            return;
        }
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
        var router = UIScreenRouter.Instance; // null only while quitting
        if (router != null)
            router.Register(this);
        panel.SetActive(false);
    }

    protected virtual void OnDisable()
    {
        Close(); // never leave the cursor free or input blocked
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);
        var router = UIScreenRouter.Existing; // never create one while shutting down
        if (router != null)
            router.Unregister(this);
    }

    // BuiltIn: own canvas, window frame and panel, in memory only (the scene is not changed).
    bool TryBuildLayout()
    {
        if (_canvasRoot != null)
            return true; // built before: OnEnable can run again
        if (style == null)
            Debug.LogWarning($"{name}: no UI Style, using the default look.", this);
        var look = Look;

        var canvas = UIBuilder.CreateOverlayCanvas($"[{name}Canvas]", transform, 100);
        var content = UIBuilder.BuildWindowFrame(
            canvas.transform,
            look.Window,
            Close,
            out _,
            out TMP_Text title
        );
        title.text = string.IsNullOrEmpty(windowTitle) ? DefaultTitle : windowTitle;

        var panelRect = UIBuilder.CreateStretched("Panel", content);
        if (!TryBuildContent(panelRect, look))
        {
            Destroy(canvas.gameObject);
            return false;
        }

        if (panel != null)
            panel.SetActive(false); // the scene (Custom) panel is not used in BuiltIn
        panel = panelRect.gameObject;
        _canvasRoot = canvas.gameObject;
        _canvasRoot.SetActive(false);
        return true;
    }

    bool IsPanelInOwnCanvas() =>
        _canvasRoot != null && panel != null && panel.transform.IsChildOf(_canvasRoot.transform);

    void ShowOwnCanvas(bool visible)
    {
        if (_canvasRoot != null)
            _canvasRoot.SetActive(visible);
    }

    /// <summary>Open the screen on its own. Does nothing if already open.</summary>
    public void Open()
    {
        if (_open || !isActiveAndEnabled)
            return;
        var input = InputManager.Instance;
        if (input == null)
            return;

        input.EnterUIMode(this);
        BeginSession();
        ShowOwnCanvas(true);
    }

    public void Close()
    {
        if (!_open)
            return;
        if (_hosted) // a desktop window: the desktop keeps the input
        {
            HideWindow();
            return;
        }
        EndSession();
        ShowOwnCanvas(false);
        var input = InputManager.Instance; // null only while quitting
        if (input != null)
            input.ExitUIMode(this);
        var router = UIScreenRouter.Existing;
        if (router != null)
            router.NotifyClosed(this);
    }

    /// <summary>Show as a window inside the desktop. The desktop owns the input and Esc.</summary>
    public void ShowWindow()
    {
        if (_open || !isActiveAndEnabled)
            return;
        _hosted = true;
        BeginSession();
        ShowOwnCanvas(IsPanelInOwnCanvas()); // a BuiltIn desktop takes the panel; otherwise it stays here
    }

    /// <summary>Hide the desktop window. Safe to call twice.</summary>
    public void HideWindow()
    {
        if (!_hosted)
            return;
        _hosted = false;
        EndSession();
        ShowOwnCanvas(false);
        WindowClosed?.Invoke();
    }

    void BeginSession()
    {
        _open = true;
        panel.SetActive(true);
        OnSessionBegin();
    }

    void EndSession()
    {
        _open = false;
        OnSessionEnd();
        if (panel != null) // gone on scene unload
            panel.SetActive(false);
    }

    protected virtual void Update()
    {
        if (!_open || _hosted) // as a window, the desktop handles Esc
            return;
        var input = InputManager.Instance;
        if (input != null && input.UICancelPressed)
            Close();
    }
}
