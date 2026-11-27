using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Where and how saves are written: one ES3 file per slot in Slots/, one global settings file.
/// Plain JSON in the editor, AES-encrypted in builds. Easy Save's own backup (a .bac copy made before
/// each save) is used when a file can't be read.
/// </summary>
public static class SaveStorage
{
    public const string SlotDirectory = "Slots";
    public const string SlotExtension = ".es3";
    public const string MetaKey = "meta";
    public const string GlobalSettingsPath = "settings.es3";

    const string BackupSuffix = ".bac"; // Easy Save's own suffix (internal to the asset)
    const int MaxSlotIdLength = 40;

    // Spec hole: change this before release. Anything in the build can read it.
    const string EncryptionPassword = "ScifiShop-change-this-password";

    /// <summary>Points settings at path in the persistent data folder, with the editor/build encryption.</summary>
    public static void ApplyTo(ES3Settings settings, string path)
    {
        settings.path = path;
        settings.location = ES3.Location.File;
        settings.directory = ES3.Directory.PersistentDataPath;
#if UNITY_EDITOR
        settings.encryptionType = ES3.EncryptionType.None;
#else
        settings.encryptionType = ES3.EncryptionType.AES;
        settings.encryptionPassword = EncryptionPassword;
#endif
    }

    public static ES3Settings CreateSettings(string path)
    {
        var settings = new ES3Settings(path, (ES3Settings)null);
        ApplyTo(settings, path);
        return settings;
    }

    public static ES3Settings SlotSettings(string slotId) =>
        CreateSettings($"{SlotDirectory}/{slotId}{SlotExtension}");

    public static ES3Settings GlobalSettings() => CreateSettings(GlobalSettingsPath);

    /// <summary>A safe file name for a slot name. Empty if nothing usable is left.</summary>
    public static string ToSlotId(string slotName)
    {
        if (string.IsNullOrWhiteSpace(slotName))
            return string.Empty;
        var id = new System.Text.StringBuilder();
        foreach (char c in slotName.Trim().ToLowerInvariant())
        {
            if (id.Length >= MaxSlotIdLength)
                break;
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                id.Append(c);
            else if (c == ' ')
                id.Append('_');
        }
        return id.ToString();
    }

    /// <summary>
    /// Reads a slot's info. If the file is unreadable, its backup is restored once and read again.
    /// False if there is no readable save in the slot.
    /// </summary>
    public static bool TryReadMeta(string slotId, out SaveSlotInfo info)
    {
        info = null;
        var settings = SlotSettings(slotId);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (ES3.FileExists(settings) && ES3.KeyExists(MetaKey, settings))
                {
                    info = ES3.Load<SaveSlotInfo>(MetaKey, settings);
                    if (info != null)
                    {
                        info.slotId = slotId; // the file name is the truth
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SaveStorage: slot '{slotId}' is unreadable ({e.Message}).");
            }
            if (attempt == 0 && !ES3.RestoreBackup(settings))
                break;
        }
        return false;
    }

    /// <summary>All readable slots, newest first.</summary>
    public static void ListSlots(List<SaveSlotInfo> results)
    {
        results.Clear();
        try
        {
            // The trailing slash matters: without it Easy Save lists the folder above "Slots".
            string directory = SlotDirectory + "/";
            var settings = CreateSettings(directory);
            if (!ES3.DirectoryExists(directory, settings))
                return;
            foreach (string file in ES3.GetFiles(directory, settings))
            {
                if (!file.EndsWith(SlotExtension, StringComparison.OrdinalIgnoreCase))
                    continue; // skips the .bac backups
                if (TryReadMeta(Path.GetFileNameWithoutExtension(file), out var info))
                    results.Add(info);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveStorage: could not list the save slots ({e.Message}).");
        }
        results.Sort((a, b) => string.CompareOrdinal(b.savedAtUtc, a.savedAtUtc));
    }

    /// <summary>Deletes the slot file and its backup. False if something went wrong.</summary>
    public static bool DeleteSlot(string slotId)
    {
        try
        {
            var settings = SlotSettings(slotId);
            ES3.DeleteFile(settings);
            ES3.DeleteFile(CreateSettings(settings.path + BackupSuffix));
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"SaveStorage: could not delete slot '{slotId}' ({e.Message}).");
            return false;
        }
    }
}
