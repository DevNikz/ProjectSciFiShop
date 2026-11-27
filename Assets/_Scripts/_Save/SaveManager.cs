using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Saves and loads the whole game with Easy Save 3, one file per slot: the shop numbers (coins, prices,
/// orders, day, progression, furniture orders) under their own keys, and the world (spawned boxes,
/// products, coins, furniture, shelves) through the scene's Easy Save 3 Manager. It autosaves when a day
/// ends into the "autosave" slot. Needs a CurrencyManager, PriceBook, OrderManager and DayManager in the
/// scene. Customers are not saved: a save made mid-day loads with an empty shop floor.
/// Set the Easy Save 3 Manager's Load Event and Save Event to None: this script decides when.
/// </summary>
public class SaveManager : MonoBehaviour
{
    public const int CurrentVersion = 1;
    public const string AutosaveSlot = "autosave";

    const string CurrencyKey = "currency",
        PricesKey = "prices",
        OrdersKey = "orders",
        DayKey = "day",
        ProgressionKey = "progression",
        FurnitureKey = "furniture";

    public static SaveManager Instance;

    [Tooltip("Save automatically when a day ends, into the 'autosave' slot.")]
    [SerializeField]
    private bool autosaveOnDayEnd = true;

    [Tooltip("How many slots the player can make by hand (the autosave slot is extra).")]
    [SerializeField, Min(1)]
    private int maxManualSlots = 10;

    [Tooltip(
        "Load the newest save when the scene starts, if the main menu did not ask for a slot."
    )]
    [SerializeField]
    private bool loadLatestOnStart;

    [Tooltip("The scene's Easy Save 3 Manager. Empty = found in the scene.")]
    [SerializeField]
    private ES3AutoSaveMgr autoSaveManager;

    private DayManager _watched;
    private Coroutine _finishLoad;

    /// <summary>True while a load is finishing. Saves and loads are refused until it ends.</summary>
    public bool IsBusy => _finishLoad != null;

    public int MaxManualSlots => maxManualSlots;

    /// <summary>Slot id of a finished save.</summary>
    public event Action<string> Saved;

    /// <summary>Slot id of a finished load. Raised a couple of frames after TryLoad, once the world is back.</summary>
    public event Action<string> Loaded;

    private struct Managers
    {
        public CurrencyManager currency;
        public PriceBook prices;
        public OrderManager orders;
        public DayManager day;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this; // not DontDestroyOnLoad: it works with scene managers
    }

    private void Start()
    {
        _watched = DayManager.Instance;
        if (_watched != null)
            _watched.DayEnded += HandleDayEnded;
        else
            Debug.LogWarning($"{name}: no DayManager, so autosave is off.", this);

        var world = ResolveWorldManager();
        if (world == null)
            Debug.LogWarning(
                $"{name}: no Easy Save 3 Manager in the scene, so the world is not saved.",
                this
            );
        else if (
            world.loadEvent != ES3AutoSaveMgr.LoadEvent.None
            || world.saveEvent != ES3AutoSaveMgr.SaveEvent.None
        )
            Debug.LogWarning(
                $"{name}: set the Easy Save 3 Manager's Load Event and Save Event to None, or it will load and save its own file.",
                world
            );

        StartCoroutine(LoadRequestedSlot());
    }

    private void OnDestroy()
    {
        if (_watched != null)
            _watched.DayEnded -= HandleDayEnded;
        if (Instance == this)
            Instance = null;
    }

    // One frame late, so every other Start has run before the world is replaced.
    private IEnumerator LoadRequestedSlot()
    {
        yield return null;
        if (SaveSession.TryConsume(out string slotId))
            TryLoad(slotId);
        else if (loadLatestOnStart)
            TryLoadLatest();
    }

    private void HandleDayEnded(DaySummary summary)
    {
        if (autosaveOnDayEnd)
            TrySave(AutosaveSlot, "Autosave");
    }

    /// <summary>True if there is any readable save.</summary>
    public bool HasSave()
    {
        var slots = new List<SaveSlotInfo>();
        SaveStorage.ListSlots(slots);
        return slots.Count > 0;
    }

    /// <summary>Loads the newest compatible save. False if there is none.</summary>
    public bool TryLoadLatest()
    {
        var slots = new List<SaveSlotInfo>();
        SaveStorage.ListSlots(slots);
        foreach (var slot in slots)
            if (slot.IsCompatible)
                return TryLoad(slot.slotId);
        return false;
    }

    /// <summary>
    /// Writes the slot. False (the old file is put back) if a manager is missing or the write fails.
    /// displayName is what the slot list shows. Empty = the slot name.
    /// </summary>
    public bool TrySave(string slotName, string displayName = null)
    {
        string slotId = SaveStorage.ToSlotId(slotName);
        if (string.IsNullOrEmpty(slotId))
        {
            Debug.LogWarning($"{name}: '{slotName}' is not a usable slot name.", this);
            return false;
        }
        if (IsBusy)
        {
            Debug.LogWarning($"{name}: busy loading, so the save was skipped.", this);
            return false;
        }
        if (!TryGetManagers("save", out Managers m))
            return false;

        var settings = SaveStorage.SlotSettings(slotId);
        bool hadFile = ES3.FileExists(settings);
        try
        {
            if (hadFile)
                ES3.CreateBackup(settings);

            foreach (var participant in GetActiveParticipants())
                participant.BeforeSave();

            ES3.Save(CurrencyKey, m.currency.CaptureState(), settings);
            ES3.Save(PricesKey, m.prices.CaptureState(), settings);
            ES3.Save(OrdersKey, m.orders.CaptureState(), settings);
            ES3.Save(DayKey, m.day.CaptureState(), settings);
            if (ProgressionManager.Instance != null)
                ES3.Save(ProgressionKey, ProgressionManager.Instance.CaptureState(), settings);
            if (FurnitureManager.Instance != null)
                ES3.Save(FurnitureKey, FurnitureManager.Instance.CaptureState(), settings);
            SaveWorld(settings);

            // Last, so a save that failed half way never looks complete.
            ES3.Save(
                SaveStorage.MetaKey,
                new SaveSlotInfo
                {
                    slotId = slotId,
                    displayName = string.IsNullOrWhiteSpace(displayName)
                        ? slotName.Trim()
                        : displayName.Trim(),
                    savedAtUtc = DateTime.UtcNow.ToString("o"),
                    day = m.day.Day,
                    coins = (int)m.currency.GetCoins(),
                    version = CurrentVersion,
                },
                settings
            );
        }
        catch (Exception e)
        {
            Debug.LogError($"{name}: saving slot '{slotId}' failed ({e.Message}).", this);
            if (hadFile)
                ES3.RestoreBackup(settings);
            return false;
        }

        Saved?.Invoke(slotId);
        return true;
    }

    /// <summary>
    /// Loads the slot. The file is read and checked first, so a bad slot changes nothing. The world
    /// comes back over the next frames, then Loaded is raised. False if there is no readable, compatible
    /// save or a manager is missing.
    /// </summary>
    public bool TryLoad(string slotId)
    {
        if (string.IsNullOrEmpty(slotId))
            return false;
        if (IsBusy)
        {
            Debug.LogWarning($"{name}: busy loading, so the load was skipped.", this);
            return false;
        }
        if (!TryGetManagers("load", out Managers m))
            return false;
        if (!SaveStorage.TryReadMeta(slotId, out SaveSlotInfo info))
        {
            Debug.LogWarning($"{name}: no readable save in slot '{slotId}'.", this);
            return false;
        }
        if (!info.IsCompatible)
        {
            Debug.LogError(
                $"{name}: save version {info.version} is not supported (expected {CurrentVersion}). The file was left untouched.",
                this
            );
            return false;
        }

        var settings = SaveStorage.SlotSettings(slotId);
        try
        {
            foreach (var participant in GetActiveParticipants())
                participant.BeforeLoad();
            WorldClearer.ClearSpawned();

            m.currency.RestoreState(Read<CurrencyData>(CurrencyKey, settings));
            m.prices.RestoreState(Read<PriceBookData>(PricesKey, settings));
            m.orders.RestoreState(Read<OrderManagerData>(OrdersKey, settings));
            m.day.RestoreState(Read<DayData>(DayKey, settings));
            // Before the world: bought expansions must be active again so their shelves can take their saved state.
            // A save with no progression in it resets progression to the start.
            if (ProgressionManager.Instance != null)
                ProgressionManager.Instance.RestoreState(
                    Read<ProgressionData>(ProgressionKey, settings)
                );
            if (FurnitureManager.Instance != null)
                FurnitureManager.Instance.RestoreState(
                    Read<FurnitureManagerData>(FurnitureKey, settings)
                );
            LoadWorld(settings);
        }
        catch (Exception e)
        {
            Debug.LogError($"{name}: loading slot '{slotId}' failed ({e.Message}).", this);
            return false;
        }

        _finishLoad = StartCoroutine(FinishLoad(slotId, m.day));
        return true;
    }

    public bool DeleteSlot(string slotId) => SaveStorage.DeleteSlot(slotId);

    // Objects made by the load have woken up, but not started: wait before the participants rebuild.
    private IEnumerator FinishLoad(string slotId, DayManager day)
    {
        yield return null;
        yield return new WaitForFixedUpdate();

        foreach (var participant in GetActiveParticipants())
        {
            try
            {
                participant.AfterLoad();
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"{participant.name}: restoring after load failed ({e.Message}).",
                    participant
                );
            }
        }
        if (day != null)
            day.Resume();

        _finishLoad = null;
        Loaded?.Invoke(slotId);
    }

    private void SaveWorld(ES3Settings settings)
    {
        var world = ResolveWorldManager();
        if (world == null)
            return;
        SaveStorage.ApplyTo(world.settings, settings.path);

        // Unused pool units are not part of the world: skip them for this write only.
        var idle = new List<ES3AutoSave>();
        foreach (var autoSave in world.autoSaves)
        {
            if (autoSave == null || !autoSave.enabled)
                continue;
            var carriable = autoSave.GetComponent<Carriable>();
            if (carriable != null && CarriablePool.IsPooledIdle(carriable))
                idle.Add(autoSave);
        }
        foreach (var autoSave in idle)
            autoSave.enabled = false;
        try
        {
            world.Save();
        }
        finally
        {
            foreach (var autoSave in idle)
                if (autoSave != null)
                    autoSave.enabled = true;
        }
    }

    private void LoadWorld(ES3Settings settings)
    {
        var world = ResolveWorldManager();
        if (world == null)
            return;
        SaveStorage.ApplyTo(world.settings, settings.path);
        world.Load();
    }

    private ES3AutoSaveMgr ResolveWorldManager() =>
        autoSaveManager != null ? autoSaveManager : ES3AutoSaveMgr.Current;

    private bool TryGetManagers(string action, out Managers managers)
    {
        managers = new Managers
        {
            currency = CurrencyManager.Instance,
            prices = PriceBook.Instance,
            orders = OrderManager.Instance,
            day = DayManager.Instance,
        };
        if (
            managers.currency != null
            && managers.prices != null
            && managers.orders != null
            && managers.day != null
        )
            return true;
        Debug.LogError(
            $"{name}: can't {action}, a manager is missing (currency, prices, orders or day).",
            this
        );
        return false;
    }

    // A copy of the list, so a participant can be destroyed while it runs. Inactive ones are leftovers
    // of objects the clearer just removed.
    private static List<SaveParticipant> GetActiveParticipants()
    {
        var list = new List<SaveParticipant>(SaveParticipant.All.Count);
        foreach (var participant in SaveParticipant.All)
            if (participant != null && participant.gameObject.activeInHierarchy)
                list.Add(participant);
        return list;
    }

    private static T Read<T>(string key, ES3Settings settings)
        where T : class => ES3.KeyExists(key, settings) ? ES3.Load<T>(key, settings) : null;

    [ContextMenu("Debug: save to 'debug'")]
    private void DebugSave() => Debug.Log($"{name}: save = {TrySave("debug")}", this);

    [ContextMenu("Debug: load 'debug'")]
    private void DebugLoad() => Debug.Log($"{name}: load = {TryLoad("debug")}", this);

    [ContextMenu("Debug: delete 'debug'")]
    private void DebugDelete() =>
        Debug.Log($"{name}: delete = {SaveStorage.DeleteSlot("debug")}", this);
}
