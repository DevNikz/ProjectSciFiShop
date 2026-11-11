using System;
using UnityEngine;

/// <summary>Which input action a prompt line shows the key for.</summary>
/// This displays what Key / Control Input for that specific action
public enum PromptAction
{
    Interact,
    Grab,
    Scan,
    BoxStore,
    Rotate, // furniture preview
    Cancel, // furniture preview
    Sell, // furniture preview, while moving a placed piece
    ScannerToggle, // equip/unequip the scanner (keep last: serialized as an int)
    BoxReturn, // BoxCycleNext OR Take Items from Shelf to Box (must match the action name)

    // Add your Action Prompts Here.
    // HoldObject
    // TakeObject
    // Etc.

    // Shop Keeper Action Prompts
    // Grab,
    // Scan,
    // BoxStore,
    // Rotate, // furniture preview
    // Cancel, // furniture preview
    // Sell, // furniture preview, while moving a placed piece
}

/// <summary>Player state a prompt line is valid in. Add flags here when new states are needed.</summary>
[Flags]
public enum PromptContext
{
    EmptyHands = 1,
    Holding = 2, // carrying anything (E pickup)
    Dragging = 4, // Grabber is dragging an object
    HoldingBox = 8, // carrying a CardboardBox (also counts as Holding)
    HoldingScanner = 16, // scanner equipped (not Holding)
    Placing = 32, // a furniture preview is up (not part of Any: only the placer's own lines use it)
    LookAtShelf = 64, // holding a box, aimed at a shelf
    LookAtPickupItem = 128, // holding a box, aimed at a storable PickupObject that is not a ProductPickup
    LookAtProductPickup = 256, // holding a box, aimed at a storable ProductPickup
    LookAtShelfStocked = 512, // LookAtShelf, and the shelf holds at least one item
    LookAtShelfEmpty = 1024, // LookAtShelf, and the shelf holds nothing
    Any =
        EmptyHands
        | Holding
        | Dragging
        | HoldingBox
        | HoldingScanner
        | Placing
        | LookAtShelf
        | LookAtPickupItem
        | LookAtProductPickup,
}

/// <summary>One HUD line: "[key of action] text", shown only when the player state matches.</summary>
[Serializable]
public struct PromptEntry
{
    public PromptAction action;
    public string text;

    [Tooltip("States this line is shown in. Nothing selected = Any.")]
    public PromptContext when;

    /// <summary>
    /// False if this line is empty or not valid in ctx. Else key = the bound key text
    /// (fallbackKey if InputManager has none).
    /// </summary>
    public bool TryGetKey(PromptContext ctx, string fallbackKey, out string key)
    {
        key = null;
        if (string.IsNullOrEmpty(text))
            return false;
        var state = when == 0 ? PromptContext.Any : when; // unset = any state
        if ((state & ctx) == 0)
            return false;

        var input = InputManager.Instance;
        key = input != null ? input.GetLabel(action) : null;
        if (string.IsNullOrEmpty(key))
            key = fallbackKey;
        return true;
    }
}
