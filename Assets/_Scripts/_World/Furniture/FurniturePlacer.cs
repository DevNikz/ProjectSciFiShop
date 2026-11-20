using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Places bought furniture (from a FurnitureCrate) and moves or sells placed furniture. While a
/// preview is up the player's Interactor is switched off, so nothing else reacts to E, and the hotbar
/// is blocked with it. A held item is stowed in a free hotbar slot first and comes back afterwards.
/// Keys: E place, scroll rotate (90 degrees), X cancel, and the Sell key while moving a placed piece.
/// One FurniturePlacer in the scene.
/// </summary>
public class FurniturePlacer : MonoBehaviour
{
    public static FurniturePlacer Instance;

    /// <summary>This placer while a preview is up, else null.</summary>
    public static FurniturePlacer Active => Instance != null && Instance._active ? Instance : null;

    const float RotationStep = 90f;
    const int HitBuffer = 16;

    [Header("Placement")]
    [Tooltip("How far from the view furniture can be placed (m).")]
    [SerializeField]
    float placeRange = 4f;

    [Tooltip("Grid size on the floor (m).")]
    [SerializeField]
    float gridSize = 0.5f;

    [Tooltip("Surfaces furniture can stand on. Expansion floors count once they are active.")]
    [SerializeField]
    LayerMask floorMask = ~0;

    [Tooltip("How level the floor must be: the surface normal's up part is at least this.")]
    [SerializeField, Range(0.5f, 1f)]
    float minFloorUp = 0.9f;

    [Tooltip("What blocks a spot: walls, furniture, boxes, the player. The floor may be in it.")]
    [SerializeField]
    LayerMask blockMask = ~0;

    [Tooltip("The free-space test starts this far above the floor (m), so the floor can't block.")]
    [SerializeField]
    float footprintLift = 0.05f;

    [Tooltip(
        "The free-space box is shrunk by this (m) on the sides and top, so a wall, ceiling or neighbour that only touches the piece doesn't block it."
    )]
    [SerializeField]
    float footprintSkin = 0.01f;

    [Header("Preview")]
    [Tooltip("Preview material while the spot is free. Empty = the furniture's own materials.")]
    [SerializeField]
    Material validMaterial;

    [Tooltip("Preview material while the spot is blocked. Empty = the furniture's own materials.")]
    [SerializeField]
    Material invalidMaterial;

    [Header("Input")]
    [Tooltip("Seconds between rotations while scrolling.")]
    [SerializeField]
    float rotateCooldown = 0.15f;

    [Header("Prompts")]
    [SerializeField]
    string placeText = "Place";

    [SerializeField]
    string rotateText = "Rotate";

    [SerializeField]
    string cancelText = "Cancel";

    [SerializeField]
    string sellText = "Sell";

    [Header("Messages")]
    [Tooltip("Optional. Shows why placing didn't start.")]
    [SerializeField]
    TMP_Text messageLabel;

    [Tooltip("Seconds a message stays up.")]
    [SerializeField]
    float messageTime = 2f;

    /// <summary>Raised after new furniture is placed.</summary>
    public event Action<FurnitureDefinition> Placed;

    /// <summary>Raised after placed furniture is moved.</summary>
    public event Action<FurnitureDefinition> Moved;

    /// <summary>The HUD lines for the preview (same array until it ends).</summary>
    public IReadOnlyList<PromptEntry> Prompts => _prompts;

    bool _active;
    Interactor _who;
    Hotbar _hotbar;
    int _prevSlot;
    bool _reequip; // something was in the hands: bring it back afterwards
    FurnitureDefinition _def;
    FurnitureCrate _crate;
    FurnitureCarriable _moving;
    GameObject _ghost;
    Renderer[] _ghostRenderers;
    readonly List<Renderer> _hidden = new(); // the moved piece's own renderers, off while its preview shows
    PromptEntry[] _prompts;
    float _yaw;
    Vector3 _pos;
    bool _valid;
    int _look; // 0 = none yet, 1 = valid look, 2 = blocked look
    int _startFrame;
    float _nextRotate;
    readonly Collider[] _hits = new Collider[HitBuffer];
    Vector3 _localCenter,
        _localHalf;

    // The Interactor comes back one frame after the preview ends, so the E that confirmed can't
    // also interact with what was just placed.
    Interactor _resumeWho;
    Hotbar _resumeHotbar;
    int _resumeSlot;
    bool _resumeReequip;
    int _resumeFrame;

    string _message,
        _lastMessage;
    float _messageUntil;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this; // not DontDestroyOnLoad: it works with scene managers
    }

    void OnEnable()
    {
        if (validMaterial == null || invalidMaterial == null)
            Debug.LogWarning(
                $"{name}: no preview materials, so the preview shows the furniture's own materials.",
                this
            );
    }

    void OnDisable()
    {
        End(true); // never leave the Interactor switched off
        if (_resumeWho != null)
            Resume();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Ends any preview right now and gives the player's Interactor back (used before a load).</summary>
    public static void CancelActive()
    {
        if (Instance != null)
            Instance.End(true);
    }

    /// <summary>Start placing the crate's furniture. Does nothing if a preview is already up.</summary>
    public void BeginPlace(FurnitureCrate crate, Interactor who)
    {
        if (crate == null || crate.Definition == null)
            return;
        Begin(crate.Definition, crate, null, who);
    }

    /// <summary>Start moving a placed piece. Does nothing if a preview is already up.</summary>
    public void BeginMove(FurnitureCarriable placed, Interactor who)
    {
        if (placed == null || placed.Definition == null)
            return;
        Begin(placed.Definition, null, placed, who);
    }

    void Begin(
        FurnitureDefinition def,
        FurnitureCrate crate,
        FurnitureCarriable moving,
        Interactor who
    )
    {
        if (_active || def == null || who == null || !who.isActiveAndEnabled || !isActiveAndEnabled)
            return;
        if (def.Prefab == null)
        {
            Debug.LogError($"{name}: '{def.name}' has no prefab.", this);
            return;
        }

        var manager = FurnitureManager.Instance;
        string lockReason = manager != null ? manager.PlacementLockReason : null;
        if (lockReason != null)
        {
            Say(lockReason);
            return;
        }

        var ghost = BuildGhost(def.Prefab);
        bool wasHolding = who.IsHolding;
        if (!TryFreeHands(who, out var hotbar, out int previous))
        {
            Destroy(ghost);
            Say("Your hands are full.");
            return;
        }

        _who = who;
        _def = def;
        _crate = crate;
        _moving = moving;
        _ghost = ghost;
        _ghostRenderers = ghost.GetComponentsInChildren<Renderer>();
        foreach (var r in _ghostRenderers)
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _hotbar = hotbar;
        _prevSlot = previous;
        _reequip = wasHolding;
        _hidden.Clear();
        if (_ghostRenderers.Length > 0)
        {
            var b = _ghostRenderers[0].bounds;
            for (int i = 1; i < _ghostRenderers.Length; i++)
                b.Encapsulate(_ghostRenderers[i].bounds);
            _localCenter = b.center;
            _localHalf = b.extents;
        }
        else
        {
            _localHalf = _def.Footprint; // fallback, treated as half extents
            _localCenter = new Vector3(0f, _localHalf.y, 0f);
        }
        if (moving != null)
            foreach (var r in moving.GetComponentsInChildren<Renderer>())
                if (r.enabled)
                {
                    r.enabled = false;
                    _hidden.Add(r);
                }

        _yaw =
            moving != null
                ? moving.transform.eulerAngles.y
                : Mathf.Round(who.Origin.eulerAngles.y / RotationStep) * RotationStep;
        _prompts = BuildPrompts(def, moving != null);
        _look = 0;
        _valid = false;
        _nextRotate = 0f;
        _startFrame = Time.frameCount;
        _active = true;
        who.enabled = false; // nothing else reacts to E now, and the hotbar can't change
    }

    // Empty hands: end any drag, stow a carried item in a free hotbar slot. False if that isn't possible.
    static bool TryFreeHands(Interactor who, out Hotbar hotbar, out int previous)
    {
        hotbar = who.GetComponent<Hotbar>();
        previous = hotbar != null ? hotbar.Current : 0;

        var grabber = who.GetComponent<Carrier>();
        if (grabber != null && grabber.AreHandsFull)
            grabber.Held.Detach();
        //grabber.Dragged.EndDrag();

        if (!who.IsHolding)
            return true;
        if (hotbar != null)
            for (int i = 0; i < hotbar.Size; i++)
                if (i != hotbar.Current && hotbar.IsFree(i))
                {
                    hotbar.Select(i); // stows the held item in its slot
                    break;
                }
        return !who.IsHolding; // still holding: no free slot, or an item that skips the hotbar
    }

    void Update()
    {
        UpdateMessage();
        if (_resumeWho != null && Time.frameCount > _resumeFrame)
            Resume();
        if (!_active)
            return;

        var input = InputManager.Instance;
        var ignore =
            _moving != null ? _moving.transform
            : _crate != null ? _crate.transform
            : null;
        // Esc, a UI opening, or the piece/crate being destroyed (a load) ends the preview.
        if (
            input == null
            || _who == null
            || ignore == null
            || !input.CursorLocked
            || !input.GameplayEnabled
        )
        {
            End(false);
            return;
        }

        bool inputLive = Time.frameCount != _startFrame; // the E that started it must not also place
        if (inputLive && IInput.BoxCancelPressed)
        {
            End(false);
            return;
        }

        float scroll = IInput.BoxRotate;
        if (inputLive && scroll != 0f && Time.unscaledTime >= _nextRotate)
        {
            _yaw = Mathf.Repeat(_yaw + Mathf.Sign(scroll) * RotationStep, 360f);
            _nextRotate = Time.unscaledTime + rotateCooldown;
        }

        UpdateGhost(ignore);
        if (!inputLive)
            return;

        if (_moving != null && IInput.FurnitureSellPressed)
        {
            Sell();
            return;
        }
        if (input.InteractPressed && _valid)
            Confirm();
    }

    void UpdateGhost(Transform ignore)
    {
        var origin = _who.Origin;
        Vector3 pos;
        bool onFloor = false;
        // Debug.Log($"Origin Pos: {origin.position} | Forward: {origin.forward}");
        if (_who.Cast(origin.position, origin.forward, placeRange, ignore, out var hit))
        {
            pos = hit.point;
            // Debug.Log($"Hit Normal Y: {hit.normal.y} >? MinFloorUp: {minFloorUp}");
            onFloor =
                (floorMask.value & (1 << hit.collider.gameObject.layer)) != 0
                && hit.normal.y >= minFloorUp;
        }
        else
            pos = origin.position + origin.forward * placeRange;

        pos.x = Snap(pos.x);
        pos.z = Snap(pos.z);
        Quaternion rot = Quaternion.Euler(0f, _yaw, 0f);
        _pos = pos;
        _valid = onFloor && IsFootprintClear(pos, rot, ignore);
        _ghost.transform.SetPositionAndRotation(pos, rot);
        SetGhostLook(_valid);
    }

    float Snap(float v) => Mathf.Round(v / gridSize) * gridSize;

    // Same idea as OrderManager's pad test: a box above the floor, ignoring the piece being moved.
    bool IsFootprintClear(Vector3 pos, Quaternion rot, Transform ignore)
    {
        // Vector3 full = _def.Footprint; // half extents
        // float height = full.y * 2f;
        // float bottom = footprintLift;
        // float top = height - footprintSkin; // flush with the real top, minus skin
        // if (top <= bottom + 0.01f)
        //     top = bottom + 0.01f; // very flat pieces still get a box

        // Vector3 half = new Vector3(
        //     Mathf.Max(0.01f, full.x - footprintSkin),
        //     (top - bottom) * 0.5f,
        //     Mathf.Max(0.01f, full.z - footprintSkin)
        // );
        // Vector3 center = pos + rot * new Vector3(0f, bottom + half.y, 0f);

        float minY = _localCenter.y - _localHalf.y;
        float lo = minY + footprintLift;
        float hi = _localCenter.y + _localHalf.y - footprintSkin;
        if (hi <= lo + 0.01f)
            hi = lo + 0.01f;

        Vector3 half = new Vector3(
            Mathf.Max(0.01f, _localHalf.x - footprintSkin),
            (hi - lo) * 0.5f,
            Mathf.Max(0.01f, _localHalf.z - footprintSkin)
        );
        Vector3 center = pos + rot * new Vector3(_localCenter.x, (lo + hi) * 0.5f, _localCenter.z);

        int n = Physics.OverlapBoxNonAlloc(
            center,
            half,
            _hits,
            rot,
            blockMask,
            QueryTriggerInteraction.Ignore
        );
        for (int i = 0; i < n; i++)
        {
            var c = _hits[i];
            if (c == null || (ignore != null && c.transform.IsChildOf(ignore)))
                continue;
            return false;
        }
        return true;
    }

    void SetGhostLook(bool valid)
    {
        int look = valid ? 1 : 2;
        if (look == _look)
            return;
        _look = look;
        var material = valid ? validMaterial : invalidMaterial;
        if (material == null || _ghostRenderers == null)
            return;
        foreach (var r in _ghostRenderers)
        {
            if (r == null)
                continue;
            var materials = new Material[r.sharedMaterials.Length];
            Array.Fill(materials, material);
            r.sharedMaterials = materials;
        }
    }

    void Confirm()
    {
        Quaternion rot = Quaternion.Euler(0f, _yaw, 0f);
        var def = _def;
        if (_moving != null)
        {
            _moving.transform.SetPositionAndRotation(_pos, rot);
            End(false);
            Moved?.Invoke(def);
            return;
        }

        Instantiate(def.Prefab, _pos, rot);
        _crate.gameObject.SetActive(false); // leaves the saved world right away
        Destroy(_crate.gameObject);
        End(false);
        Placed?.Invoke(def);
    }

    void Sell()
    {
        var manager = FurnitureManager.Instance;
        if (manager == null || !manager.TrySell(_moving))
        {
            Say("Couldn't sell it.");
            return;
        }
        End(false);
    }

    void End(bool immediate)
    {
        if (!_active)
            return;
        _active = false;

        if (_ghost != null)
            Destroy(_ghost);
        _ghost = null;
        _ghostRenderers = null;
        foreach (var r in _hidden)
            if (r != null)
                r.enabled = true;
        _hidden.Clear();

        _resumeWho = _who;
        _resumeHotbar = _hotbar;
        _resumeSlot = _prevSlot;
        _resumeReequip = _reequip;
        _resumeFrame = Time.frameCount;
        _who = null;
        _hotbar = null;
        _crate = null;
        _moving = null;
        _def = null;
        _prompts = null;

        if (immediate)
            Resume();
    }

    // Gives the player back the Interactor and the item that was in the hands.
    void Resume()
    {
        var who = _resumeWho;
        var hotbar = _resumeHotbar;
        _resumeWho = null;
        _resumeHotbar = null;
        if (who == null)
            return;

        who.enabled = true;
        if (_resumeReequip && hotbar != null && !hotbar.IsFree(_resumeSlot))
            hotbar.Select(_resumeSlot);
    }

    PromptEntry[] BuildPrompts(FurnitureDefinition def, bool moving)
    {
        var list = new List<PromptEntry>(4)
        {
            new PromptEntry
            {
                action = PromptAction.Interact,
                text = placeText,
                when = PromptContext.Placing,
            },
            new PromptEntry
            {
                action = PromptAction.Rotate,
                text = rotateText,
                when = PromptContext.Placing,
            },
            new PromptEntry
            {
                action = PromptAction.Cancel,
                text = cancelText,
                when = PromptContext.Placing,
            },
        };
        if (moving)
        {
            var manager = FurnitureManager.Instance;
            int refund = manager != null ? manager.GetRefund(def) : 0;
            list.Add(
                new PromptEntry
                {
                    action = PromptAction.Sell,
                    text = refund > 0 ? $"{sellText} (+{refund})" : sellText,
                    when = PromptContext.Placing,
                }
            );
        }
        return list.ToArray();
    }

    // A look-only copy of the prefab. It is built under an inactive holder, so none of its scripts
    // run, and every script and physics component is removed: it can't register as saveable,
    // block anything, or react.
    static GameObject BuildGhost(FurnitureCarriable prefab)
    {
        var ghost = new GameObject($"[Ghost] {prefab.name}");
        Transform root = prefab.transform;

        // Static meshes
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mf.sharedMesh == null)
                continue;

            var part = new GameObject(mf.name);
            part.transform.SetParent(ghost.transform, false);
            CopyLocalPose(root, mf.transform, part.transform);

            part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = part.AddComponent<MeshRenderer>();
            r.sharedMaterials = mr.sharedMaterials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Skinned meshes (rare for furniture; baked to a static snapshot)
        foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null)
                continue;
            var part = new GameObject(smr.name);
            part.transform.SetParent(ghost.transform, false);
            CopyLocalPose(root, smr.transform, part.transform);

            part.AddComponent<MeshFilter>().sharedMesh = smr.sharedMesh;
            var r = part.AddComponent<MeshRenderer>();
            r.sharedMaterials = smr.sharedMaterials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        return ghost;
    }

    // Pose of `child` relative to the prefab root, applied to `target` (parented under the ghost root).
    static void CopyLocalPose(Transform root, Transform child, Transform target)
    {
        target.localPosition = root.InverseTransformPoint(child.position);
        target.localRotation = Quaternion.Inverse(root.rotation) * child.rotation;
        target.localScale = child.lossyScale; // fine unless the prefab uses non-uniform scale on rotated parents
    }

    void Say(string message)
    {
        _message = message;
        _messageUntil = Time.unscaledTime + messageTime;
    }

    void UpdateMessage()
    {
        if (messageLabel == null)
            return;
        string text = Time.unscaledTime < _messageUntil ? _message : "";
        if (text == _lastMessage)
            return;
        _lastMessage = text;
        messageLabel.text = text;
    }

    void OnValidate()
    {
        placeRange = Mathf.Max(0.5f, placeRange);
        gridSize = Mathf.Max(0.05f, gridSize);
        footprintLift = Mathf.Max(0f, footprintLift);
        footprintSkin = Mathf.Clamp(footprintSkin, 0f, 0.05f);
        rotateCooldown = Mathf.Max(0.05f, rotateCooldown);
        messageTime = Mathf.Max(0.5f, messageTime);
    }
}
