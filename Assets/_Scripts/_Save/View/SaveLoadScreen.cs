using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The save slot list: Save, Load and Delete per slot, and a New Save button. Works as a desktop app, on
/// its own, or in the main menu scene (no SaveManager there: it can only load, by asking the game scene to
/// load the slot). BuiltIn mode makes the window in code; Custom mode uses the scene objects below.
/// </summary>
public class SaveLoadScreen : WindowScreen
{
    [Header("Game scene (used where there is no SaveManager, e.g. the main menu)")]
    [Tooltip("Scene loaded when a slot is picked in the main menu. It must be in Build Settings.")]
    [SerializeField]
    string gameSceneName = "_MainMenu";

    [Header("Custom layout (Custom mode only)")]
    [Tooltip("Rows are created here (e.g. a ScrollRect content).")]
    [SerializeField]
    Transform rowParent;

    [SerializeField]
    SaveSlotRow rowPrefab;

    [Tooltip("Makes a new manual slot. Hidden while the screen can't save or the slots are full.")]
    [SerializeField]
    Button newSaveButton;

    [Tooltip("Optional. Name for a new slot. Empty = Save 1, Save 2...")]
    [SerializeField]
    TMP_InputField nameInput;

    [Tooltip("Optional. Shows the result of the last action.")]
    [SerializeField]
    TMP_Text statusLabel;

    readonly List<SaveSlotInfo> _slots = new();
    readonly List<SaveSlotRow> _rows = new();
    SaveManager _subscribed;

    public override ScreenId Id => ScreenId.SaveLoad;
    protected override string DefaultTitle => "Save / Load";

    protected override bool HasCustomReferences() => rowParent != null && rowPrefab != null;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!enabled)
            return;
        if (newSaveButton != null)
            newSaveButton.onClick.AddListener(HandleNewSave);
    }

    protected override void OnDisable()
    {
        if (newSaveButton != null)
            newSaveButton.onClick.RemoveListener(HandleNewSave);
        base.OnDisable();
    }

    protected override void OnSessionBegin()
    {
        _subscribed = SaveManager.Instance;
        if (_subscribed != null)
            _subscribed.Saved += HandleSaved; // an autosave can land while the list is open
        SetStatus(string.Empty);
        Refresh();
    }

    protected override void OnSessionEnd()
    {
        if (_subscribed != null)
            _subscribed.Saved -= HandleSaved;
        _subscribed = null;
    }

    void HandleSaved(string slotId) => Refresh();

    // BuiltIn: toolbar (New Save + status) on top, a scroll list under it.
    protected override bool TryBuildContent(RectTransform panelRect, UIStyle look)
    {
        var list = look.List;
        float pad = list.padding;
        float barHeight = look.Button.height;

        var bar = UIBuilder.CreateStretched("Toolbar", panelRect);
        bar.anchorMin = new Vector2(0f, 1f);
        bar.anchorMax = Vector2.one;
        bar.pivot = new Vector2(0.5f, 1f);
        bar.anchoredPosition = new Vector2(0f, -pad);
        bar.sizeDelta = new Vector2(-2f * pad, barHeight);
        var barLayout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        barLayout.spacing = list.cellSpacing;
        barLayout.childAlignment = TextAnchor.MiddleLeft;
        barLayout.childControlWidth = true;
        barLayout.childControlHeight = true;
        barLayout.childForceExpandWidth = false;
        barLayout.childForceExpandHeight = false;

        newSaveButton = UIBuilder.CreateButton(bar, "NewSave", "New Save", look.Button);
        var buttonElement = newSaveButton.gameObject.AddComponent<LayoutElement>();
        buttonElement.preferredWidth = look.Button.width;
        buttonElement.preferredHeight = barHeight;

        statusLabel = UIBuilder.CreateText(bar, "Status", list.footerText);
        statusLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var scroll = UIBuilder.CreateStretched("Scroll", panelRect);
        scroll.offsetMin = new Vector2(pad, pad);
        scroll.offsetMax = new Vector2(-pad, -(2f * pad + barHeight));
        var scrollImage = scroll.gameObject.AddComponent<Image>();
        scrollImage.color = list.backgroundColor;
        scroll.gameObject.AddComponent<RectMask2D>();

        var content = UIBuilder.CreateStretched("Content", scroll);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = list.rowSpacing;
        contentLayout.padding = new RectOffset((int)pad, (int)pad, (int)pad, (int)pad);
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter
            .FitMode
            .PreferredSize;

        var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
        scrollRect.content = content;
        scrollRect.viewport = scroll;
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = list.scrollSensitivity;

        var holder = new GameObject("Templates", typeof(RectTransform));
        holder.transform.SetParent(panelRect, false);
        holder.SetActive(false);
        var template = new GameObject(
            "SlotRowTemplate",
            typeof(RectTransform),
            typeof(SaveSlotRow)
        ).GetComponent<SaveSlotRow>();
        template.transform.SetParent(holder.transform, false);
        template.BuildDefault(look);

        rowParent = content;
        rowPrefab = template;
        return true;
    }

    void Refresh()
    {
        foreach (var row in _rows)
            if (row != null)
                Destroy(row.gameObject);
        _rows.Clear();

        var manager = SaveManager.Instance;
        bool busy = manager != null && manager.IsBusy;
        bool canSave = manager != null && !busy;

        SaveStorage.ListSlots(_slots);
        int manualCount = 0;
        foreach (var slot in _slots)
        {
            if (!slot.IsAutosave)
                manualCount++;
            var info = slot;
            var row = Instantiate(rowPrefab, rowParent);
            row.Bind(
                info,
                !busy,
                () => HandleLoad(info),
                canSave && !info.IsAutosave ? () => HandleSave(info) : null,
                () => HandleDelete(info)
            );
            _rows.Add(row);
        }

        if (newSaveButton != null)
        {
            bool roomLeft = manager != null && manualCount < manager.MaxManualSlots;
            newSaveButton.gameObject.SetActive(canSave && roomLeft);
        }
        if (_slots.Count == 0 && statusLabel != null && string.IsNullOrEmpty(statusLabel.text))
            SetStatus("No saves yet.");
    }

    void HandleNewSave()
    {
        var manager = SaveManager.Instance;
        if (manager == null)
            return;
        string slotName =
            nameInput != null && !string.IsNullOrWhiteSpace(nameInput.text)
                ? nameInput.text.Trim()
                : NextFreeName();
        if (manager.TrySave(slotName))
        {
            if (nameInput != null)
                nameInput.text = string.Empty;
            SetStatus($"Saved '{slotName}'.");
        }
        else
            SetStatus("Save failed. See the console.");
        Refresh();
    }

    void HandleSave(SaveSlotInfo info)
    {
        var manager = SaveManager.Instance;
        if (manager == null)
            return;
        SetStatus(
            manager.TrySave(info.slotId, info.displayName)
                ? $"Saved '{info.displayName}'."
                : "Save failed. See the console."
        );
        Refresh();
    }

    void HandleLoad(SaveSlotInfo info)
    {
        var manager = SaveManager.Instance;
        if (manager != null) // in the game: load into the running scene
        {
            if (manager.TryLoad(info.slotId))
                Close();
            else
                SetStatus("Load failed. See the console.");
            return;
        }
        SaveSession.RequestLoad(info.slotId); // main menu: the game scene loads it when it starts
        SceneManager.LoadScene(gameSceneName);
    }

    void HandleDelete(SaveSlotInfo info)
    {
        SetStatus(
            SaveStorage.DeleteSlot(info.slotId)
                ? $"Deleted '{info.displayName}'."
                : "Could not delete the save."
        );
        Refresh();
    }

    // "Save 1", "Save 2"... the first one no slot uses yet.
    string NextFreeName()
    {
        for (int n = 1; ; n++)
        {
            string candidate = $"Save {n}";
            string id = SaveStorage.ToSlotId(candidate);
            if (!_slots.Exists(slot => slot.slotId == id))
                return candidate;
        }
    }

    void SetStatus(string text)
    {
        if (statusLabel != null)
            statusLabel.text = text;
    }
}
