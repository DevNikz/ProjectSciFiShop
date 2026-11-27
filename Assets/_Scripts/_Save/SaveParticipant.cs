using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base for a small component that saves state Easy Save can't see on its own (a shelf's stocked items,
/// a box's contents, the hotbar). Put it next to the object and add it to that object's ES3 Auto Save
/// "Components To Save", so the lists it fills in BeforeSave are written with the world.
/// SaveManager calls BeforeSave before writing, BeforeLoad before clearing the world, and AfterLoad
/// a couple of frames after the world came back, so every object has run its Awake and Start.
/// </summary>
public abstract class SaveParticipant : MonoBehaviour
{
    static readonly List<SaveParticipant> _all = new();

    /// <summary>Every participant that has woken up and not been destroyed.</summary>
    public static IReadOnlyList<SaveParticipant> All => _all;

    protected virtual void Awake() => _all.Add(this);

    protected virtual void OnDestroy() => _all.Remove(this);

    /// <summary>Fill the saved fields from the live state.</summary>
    public abstract void BeforeSave();

    /// <summary>Rebuild the live state from the saved fields.</summary>
    public abstract void AfterLoad();

    /// <summary>Forget live state that is about to be destroyed. Optional.</summary>
    public virtual void BeforeLoad() { }

    // Reset statics for "Enter Play Mode without Domain Reload".
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _all.Clear();
}
