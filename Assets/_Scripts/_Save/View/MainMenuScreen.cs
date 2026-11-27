using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// A minimal main menu: New Game, Continue (newest save), Load, Settings, Quit. Put it on an object in a
/// menu scene; in BuiltIn mode that is all the scene needs. Starting a game asks the game scene to load
/// through SaveSession, so the menu scene needs no SaveManager. BuiltIn mode makes the layout in code;
/// Custom mode uses the scene objects below (every button is optional).
/// </summary>
public class MainMenuScreen : MonoBehaviour
{
    [Header("Layout mode")]
    [Tooltip("BuiltIn: the menu is made in code. Custom: use the scene objects below.")]
    [SerializeField]
    UILayoutMode mode = UILayoutMode.BuiltIn;

    [Tooltip("Look of the built-in layout. Empty = the default look.")]
    [SerializeField]
    UIStyle style;

    [SerializeField]
    string gameTitle = "Scifi Shop";

    [Header("Game")]
    [Tooltip("Scene loaded by New Game, Continue and Load. It must be in Build Settings.")]
    [SerializeField]
    string gameSceneName = "_SandboxMain";

    [Header("Screens")]
    [Tooltip(
        "Empty = the menu makes its own (a child object). It never picks up other screens in the scene."
    )]
    [SerializeField]
    SaveLoadScreen saveLoadScreen;

    [Tooltip(
        "Empty = the menu makes its own (a child object). It never picks up other screens in the scene."
    )]
    [SerializeField]
    SettingsScreen settingsScreen;

    [Header("Custom layout (Custom mode only)")]
    [Tooltip("Hidden while the Load or Settings window is open.")]
    [SerializeField]
    GameObject root;

    [SerializeField]
    Button newGameButton;

    [SerializeField]
    Button continueButton;

    [SerializeField]
    Button loadButton;

    [SerializeField]
    Button settingsButton;

    [SerializeField]
    Button quitButton;

    readonly List<SaveSlotInfo> _slots = new();
    GameObject _canvasRoot;
    bool _subOpen;

    void OnEnable()
    {
        if (mode == UILayoutMode.BuiltIn && !TryBuildLayout())
        {
            enabled = false;
            return;
        }
        if (root == null)
        {
            Debug.LogError($"{name}: Root is missing.", this);
            enabled = false;
            return;
        }
        EnsureScreens();

        AddClick(newGameButton, HandleNewGame);
        AddClick(continueButton, HandleContinue);
        AddClick(loadButton, HandleLoad);
        AddClick(settingsButton, HandleSettings);
        AddClick(quitButton, HandleQuit);

        var input = InputManager.Instance; // the menu needs the free cursor
        if (input != null)
            input.EnterUIMode(this);
        RefreshContinue();
    }

    void OnDisable()
    {
        RemoveClick(newGameButton, HandleNewGame);
        RemoveClick(continueButton, HandleContinue);
        RemoveClick(loadButton, HandleLoad);
        RemoveClick(settingsButton, HandleSettings);
        RemoveClick(quitButton, HandleQuit);
        var input = InputManager.Instance; // null only while quitting
        if (input != null)
            input.ExitUIMode(this);
    }

    // The menu hides while a window is open on top of it, and Continue is checked again when it comes back.
    void Update()
    {
        bool subOpen =
            (saveLoadScreen != null && saveLoadScreen.IsOpen)
            || (settingsScreen != null && settingsScreen.IsOpen);
        if (subOpen == _subOpen)
            return;
        _subOpen = subOpen;
        root.SetActive(!subOpen);
        if (!subOpen)
            RefreshContinue();
    }

    static void AddClick(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.AddListener(action);
    }

    static void RemoveClick(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.RemoveListener(action);
    }

    void RefreshContinue()
    {
        SaveStorage.ListSlots(_slots);
        bool any = FindNewestCompatible() != null;
        if (continueButton != null)
            continueButton.interactable = any;
        if (loadButton != null)
            loadButton.interactable = _slots.Count > 0;
    }

    string FindNewestCompatible()
    {
        foreach (var slot in _slots)
            if (slot.IsCompatible)
                return slot.slotId;
        return null;
    }

    void HandleNewGame()
    {
        SaveSession.RequestNewGame();
        SceneManager.LoadScene(gameSceneName);
    }

    void HandleContinue()
    {
        string slotId = FindNewestCompatible();
        if (slotId == null)
            return;
        SaveSession.RequestLoad(slotId);
        SceneManager.LoadScene(gameSceneName);
    }

    void HandleLoad()
    {
        EnsureScreens();
        OpenScreen(saveLoadScreen);
    }

    void HandleSettings()
    {
        EnsureScreens();
        OpenScreen(settingsScreen);
    }

    void OpenScreen(WindowScreen screen)
    {
        if (screen == null || !screen.isActiveAndEnabled)
        {
            Debug.LogWarning(
                $"{name}: the {typeof(WindowScreen).Name} could not open (see the earlier error).",
                this
            );
            return;
        }
        screen.Open();
    }

    // Makes a screen again if it was never assigned or has been destroyed, so a click never reaches a dead object.
    void EnsureScreens()
    {
        if (saveLoadScreen == null)
            saveLoadScreen = CreateChildScreen<SaveLoadScreen>("[LoadScreen]");
        if (settingsScreen == null)
            settingsScreen = CreateChildScreen<SettingsScreen>("[SettingsScreen]");
    }

    T CreateChildScreen<T>(string objectName)
        where T : WindowScreen
    {
        var child = new GameObject(objectName);
        child.transform.SetParent(transform, false);
        return child.AddComponent<T>();
    }

    void HandleQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // BuiltIn: a full-screen background with a centered column: title and the buttons.
    bool TryBuildLayout()
    {
        if (_canvasRoot != null)
            return true; // built before: OnEnable can run again
        if (style == null)
            Debug.LogWarning($"{name}: no UI Style, using the default look.", this);
        var look = style != null ? style : UIStyle.Fallback;

        var canvas = UIBuilder.CreateOverlayCanvas("[MainMenuCanvas]", transform, 50);
        _canvasRoot = canvas.gameObject;

        var background = UIBuilder.CreateStretched("Background", canvas.transform);
        background.gameObject.AddComponent<Image>().color = look.List.backgroundColor;
        root = background.gameObject;

        var column = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup));
        var columnRect = (RectTransform)column.transform;
        columnRect.SetParent(background, false);
        columnRect.anchorMin = columnRect.anchorMax = columnRect.pivot = new Vector2(0.5f, 0.5f);
        columnRect.sizeDelta = new Vector2(420f, 0f);
        var layout = column.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        column.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter
            .FitMode
            .PreferredSize;

        var title = UIBuilder.CreateText(columnRect, "Title", look.Window.titleStyle);
        title.text = gameTitle;
        title.fontSize = 56f;
        title.alignment = TextAlignmentOptions.Center;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 110f;

        newGameButton = AddMenuButton(columnRect, look, "NewGame", "New Game");
        continueButton = AddMenuButton(columnRect, look, "Continue", "Continue");
        loadButton = AddMenuButton(columnRect, look, "Load", "Load");
        settingsButton = AddMenuButton(columnRect, look, "Settings", "Settings");
        quitButton = AddMenuButton(columnRect, look, "Quit", "Quit");
        return true;
    }

    static Button AddMenuButton(Transform parent, UIStyle look, string objectName, string label)
    {
        var button = UIBuilder.CreateButton(parent, objectName, label, look.Button);
        button.gameObject.AddComponent<LayoutElement>().preferredHeight = look.Button.height * 1.2f;
        return button;
    }
}
