using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One line of the slot list: name, details, and Load / Save / Delete buttons. Save and Delete need a second
/// click within a moment (the label asks "Sure?"), so a stray click never overwrites or deletes a save.
/// BuildDefault makes the layout in code; a Custom prefab assigns the fields below (buttons may be empty).
/// </summary>
public class SaveSlotRow : MonoBehaviour
{
    const float ConfirmSeconds = 2.5f;

    [SerializeField]
    TMP_Text nameLabel;

    [SerializeField]
    TMP_Text detailLabel;

    [SerializeField]
    Button loadButton;

    [SerializeField]
    Button saveButton;

    [SerializeField]
    Button deleteButton;

    Action _onLoad,
        _onSave,
        _onDelete;
    float _saveArmedUntil,
        _deleteArmedUntil;
    string _saveText,
        _deleteText;

    /// <summary>Fills the row for one slot. Pass null for an action that must not show its button.</summary>
    public void Bind(SaveSlotInfo info, bool canLoad, Action onLoad, Action onSave, Action onDelete)
    {
        _onLoad = onLoad;
        _onSave = onSave;
        _onDelete = onDelete;
        _saveArmedUntil = _deleteArmedUntil = 0f;

        if (nameLabel != null)
            nameLabel.text = info.IsAutosave ? "Autosave" : info.displayName;
        if (detailLabel != null)
            detailLabel.text = info.IsCompatible
                ? $"Day {info.day}   {info.coins} coins   {info.GetLocalTimeText()}"
                : $"Saved by another version ({info.version})";

        SetUp(loadButton, onLoad != null, canLoad && info.IsCompatible, Load);
        SetUp(saveButton, onSave != null, true, Save);
        SetUp(deleteButton, onDelete != null, true, Delete);
        _saveText = LabelOf(saveButton);
        _deleteText = LabelOf(deleteButton);
    }

    static void SetUp(Button button, bool visible, bool interactable, UnityEngine.Events.UnityAction click)
    {
        if (button == null)
            return;
        button.onClick.RemoveAllListeners();
        button.gameObject.SetActive(visible);
        button.interactable = interactable;
        button.onClick.AddListener(click);
    }

    void Load() => _onLoad?.Invoke();

    void Save()
    {
        if (Time.unscaledTime > _saveArmedUntil)
        {
            _saveArmedUntil = Time.unscaledTime + ConfirmSeconds;
            SetLabel(saveButton, "Sure?");
            return;
        }
        _saveArmedUntil = 0f;
        SetLabel(saveButton, _saveText);
        _onSave?.Invoke();
    }

    void Delete()
    {
        if (Time.unscaledTime > _deleteArmedUntil)
        {
            _deleteArmedUntil = Time.unscaledTime + ConfirmSeconds;
            SetLabel(deleteButton, "Sure?");
            return;
        }
        _deleteArmedUntil = 0f;
        _onDelete?.Invoke();
    }

    void Update()
    {
        // The confirm label goes back when the moment passes.
        if (_saveArmedUntil > 0f && Time.unscaledTime > _saveArmedUntil)
        {
            _saveArmedUntil = 0f;
            SetLabel(saveButton, _saveText);
        }
        if (_deleteArmedUntil > 0f && Time.unscaledTime > _deleteArmedUntil)
        {
            _deleteArmedUntil = 0f;
            SetLabel(deleteButton, _deleteText);
        }
    }

    static string LabelOf(Button button)
    {
        var text = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        return text != null ? text.text : null;
    }

    static void SetLabel(Button button, string text)
    {
        var label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        if (label != null && text != null)
            label.text = text;
    }

    /// <summary>Makes the default layout (BuiltIn mode): name and details on the left, buttons on the right.</summary>
    public void BuildDefault(UIStyle look)
    {
        var list = look.List;
        var button = look.Button;

        var rect = (RectTransform)transform;
        var background = gameObject.GetComponent<Image>();
        if (background == null)
            background = gameObject.AddComponent<Image>();
        background.color = list.rowColor;

        var layout = gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(
            (int)list.rowPadding,
            (int)list.rowPadding,
            4,
            4
        );
        layout.spacing = list.cellSpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        gameObject.AddComponent<LayoutElement>().preferredHeight = list.rowHeight;

        var texts = new GameObject("Texts", typeof(RectTransform), typeof(VerticalLayoutGroup));
        texts.transform.SetParent(rect, false);
        var textLayout = texts.GetComponent<VerticalLayoutGroup>();
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;
        texts.AddComponent<LayoutElement>().flexibleWidth = 1f;

        nameLabel = UIBuilder.CreateText(texts.transform, "Name", list.nameText);
        detailLabel = UIBuilder.CreateText(texts.transform, "Details", list.detailText);

        loadButton = AddButton("Load", "Load", button);
        saveButton = AddButton("Save", "Save", button);
        deleteButton = AddButton("Delete", "Delete", button);
    }

    Button AddButton(string objectName, string label, ButtonStyle style)
    {
        var button = UIBuilder.CreateButton(transform, objectName, label, style);
        var element = button.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = style.width * 0.6f;
        element.preferredHeight = style.height;
        return button;
    }
}
