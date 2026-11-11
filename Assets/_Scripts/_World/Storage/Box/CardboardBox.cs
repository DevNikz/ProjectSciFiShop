using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[CarriableAddon("As Storage Box")]
[RequireComponent(typeof(Carriable), typeof(Rigidbody))]
public class CardboardBox : MonoBehaviour, IHeldInteract, ICarrierStorage, IPromptContextFlag
{
    [Header("Cavity")]
    [Tooltip("Inner volume (BoxCollider, made a trigger). Empty = auto from renderers.")]
    [SerializeField]
    private BoxCollider cavity;

    [Tooltip("Auto cavity: wall thickness (m).")]
    [SerializeField]
    private float autoWallThickness = 0.04f;

    [Tooltip("Optional. Animator with a bool that is true while the box is held.")]
    [SerializeField]
    private Animator animator;

    [SerializeField]
    private string openParam = "Open";

    [Header("Layout")]
    [Tooltip("Where items gather on the box floor (like a layout group). Upper = +Z, Right = +X.")]
    [SerializeField]
    private LayoutAlign alignment = LayoutAlign.MiddleCenter;

    [Tooltip("Stack position in the box. Bottom = rest on the floor.")]
    [SerializeField]
    private VerticalAlign verticalAlignment = VerticalAlign.Bottom;

    [Tooltip("Gap between items per axis (m). Also the wobble room.")]
    [SerializeField]
    private Vector3 spacing = new(0.03f, 0.03f, 0.03f);

    [Header("Store")]
    [Tooltip("Reach for storing and placing (m).")]
    [SerializeField]
    private float reach = 3f;

    [Tooltip("Aim tolerance when storing (m radius). 0 = exact ray only.")]
    [SerializeField]
    private float aimRadius = 0.12f;

    [Tooltip("Heaviest item accepted (kg).")]
    [SerializeField]
    private float maxItemMass = 10f;

    [SerializeField]
    private float enterTime = 0.35f;

    [SerializeField]
    private float arcHeight = 0.2f;

    [Tooltip("Box mass grows with its contents.")]
    [SerializeField]
    private bool shouldAddContentMass;

    public enum LayoutAlign
    {
        UpperLeft,
        UpperCenter,
        UpperRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        LowerLeft,
        LowerCenter,
        LowerRight,
    }

    public enum VerticalAlign
    {
        Bottom,
        Middle,
        Top,
    }

    // Released items ignore these colliders until 'until'.
    private sealed class Batch
    {
        public float until;
        public readonly List<Collider> items = new();
        public readonly List<Collider> box = new();
    }

    private sealed class Pop
    {
        public Slot s;
        public float t;
    }

    private struct GhostPart
    {
        public Mesh mesh;
        public Matrix4x4 localMatrix; // item-local space
        public int subMeshCount;
    }

    public enum SizeMode
    {
        Varied, // shrink only as needed; full box shrinks new items more
        Constant, // every item gets the same size; full box rejects new items
    }

    [Header("Sizing")]
    [Tooltip("Resize stored items. They regain full size when placed.")]
    [SerializeField]
    private bool shouldShrinkItems = true;

    [Tooltip("Varied = shrink to fit. Constant = fixed size, box rejects when full.")]
    [SerializeField]
    private SizeMode sizeMode = SizeMode.Varied;

    [Tooltip("Constant only: also enlarge items smaller than Stored Size.")]
    [SerializeField]
    private bool shouldScaleUpInConstantMode;

    [Tooltip("Stored size: largest side (m). Varied never enlarges small items.")]
    [SerializeField]
    private float storedSize = 0.2f;

    [Tooltip("Smallest allowed scale (fraction of original). Smaller = rejected.")]
    [SerializeField, Range(0.05f, 1f)]
    private float minScale = 0.1f;

    [Header("Throw")]
    [Tooltip("Throwing the box releases its contents with their original physics.")]
    [SerializeField]
    private bool shouldEjectOnThrow = true;

    [Tooltip("Random extra speed for released items (m/s).")]
    [SerializeField]
    private float ejectSpread = 1.5f;

    [Tooltip("Time released items take to regain full size (s).")]
    [SerializeField]
    private float ejectGrowTime = 0.2f;

    [Tooltip("Time released items ignore the box and each other (s).")]
    [SerializeField]
    private float ejectIgnoreTime = 0.5f;

    [Header("Drop")]
    [Tooltip("On = when dropped (not thrown), ease the box's world X rotation to 0.")]
    [SerializeField]
    private bool levelOnRelease = true;

    [Tooltip("Time to ease to level after release (s). 0 = snap.")]
    [SerializeField]
    private float levelBlendTime = 0.2f;

    [Header("Wobble")]
    [Tooltip("Items jiggle when the box moves. Off = rigid.")]
    [SerializeField]
    private bool isWobbleEnabled = true;

    [SerializeField]
    private float wobbleFrequency = 4.5f;

    [SerializeField, Range(0.05f, 1f)]
    private float wobbleDamping = 0.3f;

    [SerializeField]
    private float wobbleStrength = 1f;

    [Tooltip("Degrees of tilt per metre of offset.")]
    [SerializeField]
    private float tiltPerMeter = 250f;

    [SerializeField]
    private float maxTilt = 12f;

    [Header("Place")]
    [SerializeField]
    private LayerMask placeMask = ~0;

    [Tooltip("Surfaces steeper than this are invalid (deg).")]
    [SerializeField]
    private float maxSlope = 50f;

    [Tooltip("Tilt the item to lie flat on sloped surfaces.")]
    [SerializeField]
    private bool shouldAlignToSurface = true;

    [SerializeField]
    private float placeTime = 0.3f;

    [SerializeField]
    private float rotateStep = 15f;

    [Header("Shelf")]
    [Tooltip("Seconds between items while left click is held aimed at a shelf.")]
    [SerializeField]
    private float shelfStockInterval = 0.25f;

    private const float SETTLE_TIME = 0.15f;
    private const float MAX_ACCEL = 50f;

    private sealed class Slot
    {
        public Carriable item;
        public Transform tr;
        public Rigidbody rb;
        public Collider[] cols;
        public bool[] colOn;
        public bool kin,
            grav,
            detect;
        public RigidbodyInterpolation interp;
        public Transform parent;
        public float mass;
        public Vector3 bCenter,
            bExt,
            full, // world scale at store time
            half;
        public float shrink = 1f;
        public bool shrunk;
        public Vector3 startScale,
            inScale,
            fromScale,
            endScale;
        public Quaternion insideRot,
            restRot;
        public Vector3 target,
            basePos,
            baseVel,
            off,
            offVel,
            lastV;
        public bool entering;
        public float t;
        public Vector3 startPos;
        public Quaternion startRot;
        public Vector3 fromPos,
            endPos;
        public Quaternion fromRot,
            endRot;
        public GhostPart[] ghostParts; // placement outline, built on first preview
        public ShelfInventory shelf; // set while flying to a shelf
        public Transform shelfSlot; // the slot reserved on that shelf
    }

    private Carriable _pickup;
    private Rigidbody _rb;
    private Transform _root;
    private bool _hasCavity,
        _quit;
    private Vector3 _cavityMin,
        _cavityMax;
    private float _baseMass;
    private bool _wasHeld; // held last frame (to catch the release)
    private bool _thrown; // the last release was a throw: leave its rotation alone
    private bool _leveling;
    private float _levelT;
    private Quaternion _levelFrom,
        _levelTo;
    private readonly List<Slot> _slots = new();
    private readonly List<Slot> _leaving = new();
    private readonly List<Vector3> _packCenters = new();
    private readonly List<Vector3> _packSizes = new();
    private readonly BoxLayoutPacker _packer = new();
    private readonly Collider[] _overlapBuffer = new Collider[16];
    private readonly RaycastHit[] _sphereHits = new RaycastHit[16];
    private readonly List<Batch> _batches = new();
    private readonly List<Pop> _pops = new();
    private readonly List<Collider> _colTmp = new();
    private readonly List<Carriable> _itemTmp = new();
    private readonly List<GhostPart> _ghostPartBuffer = new();

    private Slot _previewSlot;
    private float _previewYaw;
    private bool _isPreviewValid;
    private Vector3 _previewPosition;
    private Quaternion _previewRotation;

    // Shelf stocking: the aimed shelf (refreshed each frame while held) and the hold-repeat state.
    private ShelfInventory _aimedShelf;
    private bool _isShelfStocking;
    private float _nextStockTime;

    // Store highlight: the aimed item that would actually store.
    [SerializeField]
    private Carriable _highlightedItem;
    private bool _isAimedItemProduct;
    private Carriable _cachedCandidate;
    private int _cachedSlotCount = -1;
    private bool _canStoreCachedCandidate;

    private bool _animOk,
        _open;
    private int _openHash;

    public int Count => _slots.Count;

    /// <summary>Copies the stored items into results (cleared first). Does not allocate.</summary>
    public void GetItems(List<Carriable> results)
    {
        if (results == null)
            return;
        results.Clear();
        foreach (var s in _slots)
            if (s != null && s.item != null)
                results.Add(s.item);
    }

    /// <summary>Stores an item as if it was put in by hand. False if it can't be stored.</summary>
    public bool TryStoreItem(Carriable item) => TryStore(item);

    /// <summary>The shelf under the crosshair while this box is held, else null. The HUD reads it for the LookAtShelf prompt state.</summary>
    public ShelfInventory AimedShelf => _aimedShelf;

    /// <summary>The aimed item this held box could store right now (outlined), else null. The HUD reads it for the look-at prompt states.</summary>
    public Carriable AimedItem => _highlightedItem;

    /// <summary>Prompt bits this held box adds to the Holding context: HoldingBox plus what it is aimed at.</summary>
    public PromptContext Flag => PromptContext.HoldingBox | LookContext;

    // What the held box is aimed at: a shelf (stocked or empty), a storable product, or a storable plain item.
    private PromptContext LookContext
    {
        get
        {
            PromptContext look = 0;
            if (_aimedShelf != null)
                look |=
                    PromptContext.LookAtShelf
                    | (
                        _aimedShelf.Count > 0
                            ? PromptContext.LookAtShelfStocked
                            : PromptContext.LookAtShelfEmpty
                    );
            if (_highlightedItem != null)
                look |= _isAimedItemProduct
                    ? PromptContext.LookAtProductPickup
                    : PromptContext.LookAtPickupItem;
            return look;
        }
    }

    /// <summary>Item currently selected for placing, else null.</summary>
    public Carriable SelectedItem => _previewSlot != null ? _previewSlot.item : null;

    /// <summary>
    /// Sends the selected product (else the last stored one) from this held box to a shelf, with the
    /// place arc. True if it started flying. Fires Rejected when the shelf refuses it.
    /// </summary>
    public bool TryPlaceSelectedOnShelf(ShelfInventory shelf)
    {
        var slot = PickShelfSourceSlot();
        if (shelf == null || slot == null)
            return false;

        Transform shelfSlot = shelf.TryReserveSlot(slot.item);
        if (shelfSlot == null)
        {
            Rejected?.Invoke(slot.item);
            return false;
        }

        var product = slot.item.GetComponent<ShelfProduct>();
        slot.shelf = shelf;
        slot.shelfSlot = shelfSlot;
        StartLeaving(
            slot,
            shelfSlot.TransformPoint(product.PlacementOffset),
            shelfSlot.rotation * product.PlacementRotation
        );
        return true;
    }

    // Selected item first, else the newest stored item that is not still entering.
    private Slot PickShelfSourceSlot()
    {
        if (_previewSlot != null && _previewSlot.item != null && _previewSlot.tr != null)
            return _previewSlot;
        for (int i = _slots.Count - 1; i >= 0; i--)
        {
            var s = _slots[i];
            if (s != null && !s.entering && s.item != null && s.tr != null)
                return s;
        }
        return null;
    }

    public event System.Action<Carriable> Stored;
    public event System.Action<Carriable> Removed;
    public event System.Action<Carriable> Rejected;

    private void Awake()
    {
        _pickup = GetComponent<Carriable>();
        _rb = GetComponent<Rigidbody>();
        _baseMass = _rb != null ? _rb.mass : 0f;

        var go = new GameObject("Contents");
        _root = go.transform;
        _root.SetParent(transform, false);
        Vector3 s = transform.lossyScale;
        _root.localScale = new Vector3(Inv(s.x), Inv(s.y), Inv(s.z));

        ComputeCavity();
        if (!_hasCavity)
            Debug.LogWarning(
                $"{name}: no cavity found. Assign a Cavity or add mesh renderers.",
                this
            );

        if (
            animator != null
            && animator.runtimeAnimatorController != null
            && !string.IsNullOrEmpty(openParam)
        )
        {
            _openHash = Animator.StringToHash(openParam);
            foreach (var p in animator.parameters)
                if (p.nameHash == _openHash && p.type == AnimatorControllerParameterType.Bool)
                    _animOk = true;
        }
    }

    private void OnEnable()
    {
        if (_pickup != null)
            _pickup.Thrown += HandleThrown;
    }

    private void OnDisable()
    {
        if (_pickup != null)
            _pickup.Thrown -= HandleThrown;
        _wasHeld = false;
        _leveling = false;
        _thrown = false;
        foreach (var p in _pops) // finish growth
            if (p.s.tr != null)
                p.s.tr.localScale = p.s.endScale;
        _pops.Clear();
        foreach (var b in _batches) // never leave collisions off
            IgnorePairs(b, false);
        _batches.Clear();
        ClearPreview();
        SetHighlightedItem(null);
        for (int i = _leaving.Count - 1; i >= 0; i--)
            Finish(_leaving[i]);
        _leaving.Clear();
    }

    private void OnApplicationQuit()
    {
        _quit = true;
    }

    private void OnDestroy()
    {
        bool release = !_quit && gameObject.scene.isLoaded;
        foreach (var s in _slots)
        {
            if (release && s.tr != null)
            {
                s.endPos = s.tr.position;
                s.endRot = s.tr.rotation;
                s.tr.SetParent(s.parent != null ? s.parent : null, true);
                Restore(s);
            }
        }
        _slots.Clear();
        foreach (var s in _leaving)
            Finish(s);
        _leaving.Clear();
    }

    public bool OnHeldInteract(Interactor who)
    {
        if (!CanHandleHeldInteract(who))
            return false;

        if (_previewSlot != null)
        {
            if (_isPreviewValid)
                ConfirmPlace();
            return true;
        }

        return false; // storing is on left click (UpdateInput): E falls through to drop/throw
    }

    /// <summary>Carrier hook: while this box is held, picking up another item stores it here, never in the hotbar.</summary>
    public bool TryStore(Carrier from, Carriable p)
    {
        if (!isActiveAndEnabled || _pickup == null || from != _pickup.Holder || p == null)
            return false;
        if (TryStore(p))
            return true;
        Rejected?.Invoke(p);
        return false;
    }

    private bool CanHandleHeldInteract(Interactor who) =>
        isActiveAndEnabled && who != null && _pickup != null && _pickup.HolderInteractor == who;

    // False = nothing to store.
    private bool TryStoreAimedItem(Interactor who)
    {
        Transform origin = who.Origin;
        if (!_hasCavity || origin == null)
            return false;

        Carriable item = FindAimedItem(who, origin);
        if (item == null)
            return false;

        if (!TryStore(item))
            Rejected?.Invoke(item);
        return true;
    }

    // Exact ray first, then a thin spherecast so near-misses still store.
    private Carriable FindAimedItem(Interactor who, Transform origin)
    {
        bool hasExactHit =
            who.Cast(origin.position, origin.forward, reach, transform, out var hit)
            && hit.collider != null;
        if (hasExactHit)
        {
            var exactItem = hit.collider.GetComponentInParent<Carriable>();
            if (exactItem != null)
                return exactItem;
        }
        if (aimRadius <= 0f)
            return null;

        float maxDistance = hasExactHit ? hit.distance + aimRadius : reach; // don't reach through walls
        return SphereCastForItem(who, origin, maxDistance);
    }

    private Carriable SphereCastForItem(Interactor who, Transform origin, float maxDistance)
    {
        int hitCount = Physics.SphereCastNonAlloc(
            origin.position,
            aimRadius,
            origin.forward,
            _sphereHits,
            maxDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );
        Carriable nearest = null;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            var hitCollider = _sphereHits[i].collider;
            if (
                hitCollider == null
                || hitCollider.transform.IsChildOf(transform)
                || hitCollider.transform.IsChildOf(who.transform)
            )
                continue;
            var item = hitCollider.GetComponentInParent<Carriable>();
            if (item != null && _sphereHits[i].distance < nearestDistance)
            {
                nearestDistance = _sphereHits[i].distance;
                nearest = item;
            }
        }
        return nearest;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        TrackRelease();
        PurgeSlots();
        UpdateSlots(dt);
        UpdateLeaving(dt);
        UpdateEject(dt);
        UpdateInput();
        UpdatePreview();
        UpdateStoreHighlight();
        UpdateAnimator();
    }

    // Checked every frame, not every physics step, so a release is never missed.
    // On a drop (or a drop by distance) it starts easing world X rotation to 0. Yaw and roll are
    // kept. Throws are left alone: Throw raises Thrown right after letting go, before this runs.
    private void TrackRelease()
    {
        bool held = _pickup != null && _pickup.IsHeld;
        bool released = _wasHeld && !held;
        _wasHeld = held;
        if (held)
        {
            _leveling = false; // grabbed again
            _thrown = false;
            return;
        }
        if (!released)
            return;
        bool wasThrown = _thrown;
        _thrown = false;
        if (wasThrown || !levelOnRelease || _rb == null || _rb.isKinematic)
            return;

        Quaternion q = transform.rotation; // current pose; the physics copy can be a step old
        Vector3 e = q.eulerAngles;
        _levelFrom = q;
        _levelTo = Quaternion.Euler(0f, e.y, e.z);
        _levelT = 0f;
        _leveling = true;
    }

    // Drives the ease. Collisions can't push X off target meanwhile; it ends at exactly 0.
    private void UpdateLevelBlend()
    {
        if (!_leveling)
            return;
        if (_pickup == null || _pickup.IsHeld || _rb == null || _rb.isKinematic)
        {
            _leveling = false;
            return;
        }

        _levelT += Time.fixedDeltaTime;
        float t = levelBlendTime > 0f ? Mathf.Clamp01(_levelT / levelBlendTime) : 1f;
        t = t * t * (3f - 2f * t); // smoothstep
        _rb.rotation = Quaternion.Slerp(_levelFrom, _levelTo, t);

        Vector3 w = _rb.angularVelocity; // a throw's random spin must not re-pitch it
        w.x = 0f;
        _rb.angularVelocity = w;

        if (_levelT >= levelBlendTime)
            _leveling = false;
    }

    private void FixedUpdate()
    {
        UpdateLevelBlend();
        if (_slots.Count == 0 || _root == null)
            return;
        float dt = Time.fixedDeltaTime;
        if (dt <= 0f)
            return;

        float w = 2f * Mathf.PI * wobbleFrequency;
        float k = w * w;
        float c = 2f * wobbleDamping * w;
        Vector3 range = spacing * 0.5f;
        bool sim = isWobbleEnabled && (range.x > 0f || range.y > 0f || range.z > 0f);

        for (int i = 0; i < _slots.Count; i++)
        {
            var s = _slots[i];
            if (s.tr == null)
                continue;
            Vector3 v = _rb != null ? _rb.GetPointVelocity(s.tr.position) : Vector3.zero;
            Vector3 a = (v - s.lastV) / dt;
            s.lastV = v;
            if (s.entering)
                continue;
            if (!sim)
            {
                s.off = Vector3.zero;
                s.offVel = Vector3.zero;
                continue;
            }

            Vector3 al = Vector3.ClampMagnitude(
                _root.InverseTransformDirection(a) * wobbleStrength,
                MAX_ACCEL
            );
            s.offVel += (-al - k * s.off - c * s.offVel) * dt;
            s.off += s.offVel * dt;

            Vector3 lo = Vector3.Min(_cavityMin + s.half - s.target, Vector3.zero);
            Vector3 hi = Vector3.Max(_cavityMax - s.half - s.target, Vector3.zero);
            Clamp1(
                ref s.off.x,
                ref s.offVel.x,
                Mathf.Max(lo.x, -range.x),
                Mathf.Min(hi.x, range.x)
            );
            Clamp1(
                ref s.off.y,
                ref s.offVel.y,
                Mathf.Max(lo.y, -range.y),
                Mathf.Min(hi.y, range.y)
            );
            Clamp1(
                ref s.off.z,
                ref s.offVel.z,
                Mathf.Max(lo.z, -range.z),
                Mathf.Min(hi.z, range.z)
            );
        }
    }

    // ---------- store ----------

    private bool TryStore(Carriable item)
    {
        Slot slot = CreateSlot(item);
        if (slot == null)
            return false;

        item.Detach();
        DisableColliders(slot);
        FreezeBody(slot);
        ParentToContents(slot);

        _slots.Add(slot);
        ApplyTargets();
        UpdateMass();
        Stored?.Invoke(item);
        return true;
    }

    // Null if the item can't be stored. Never touches the item.
    private Slot CreateSlot(Carriable item)
    {
        if (!IsStorable(item))
            return null;

        Transform itemTransform = item.transform;
        if (!MeasureItemBounds(itemTransform, out var boundsCenter, out var boundsExtents))
            return null;

        Rigidbody body = item.Body;
        var slot = new Slot
        {
            item = item,
            tr = itemTransform,
            rb = body,
            bCenter = boundsCenter,
            bExt = boundsExtents,
            full = itemTransform.lossyScale,
            mass = body.mass,
            restRot = itemTransform.rotation,
            parent = itemTransform.parent,
            insideRot = Snap(Quaternion.Inverse(transform.rotation) * itemTransform.rotation),
        };
        return ChooseStoredScale(slot) ? slot : null; // sets shrink + half
    }

    private bool IsStorable(Carriable item)
    {
        if (
            !_hasCavity
            || item == null
            || item == _pickup
            || !item.isActiveAndEnabled
            || item.IsHeld
            || IsPopping(item)
        )
            return false;
        Rigidbody body = item.Body;
        if (body == null || body.isKinematic || body.mass > maxItemMass)
            return false;
        return item.GetComponentInChildren<CardboardBox>() == null;
    }

    private static void DisableColliders(Slot slot)
    {
        slot.cols = slot.item.GetComponentsInChildren<Collider>(true);
        slot.colOn = new bool[slot.cols.Length];
        for (int i = 0; i < slot.cols.Length; i++)
        {
            slot.colOn[i] = slot.cols[i] != null && slot.cols[i].enabled;
            if (slot.cols[i] != null)
                slot.cols[i].enabled = false;
        }
    }

    // Saves the physics state (Restore puts it back), then parks the body.
    private static void FreezeBody(Slot slot)
    {
        Rigidbody body = slot.rb;
        slot.kin = body.isKinematic;
        slot.grav = body.useGravity;
        slot.detect = body.detectCollisions;
        slot.interp = body.interpolation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        slot.item.enabled = false;
    }

    private void ParentToContents(Slot slot)
    {
        Transform itemTransform = slot.tr;
        itemTransform.SetParent(_root, true);
        slot.startScale = itemTransform.localScale;
        slot.inScale = slot.startScale * slot.shrink;
        slot.shrunk = Mathf.Abs(slot.shrink - 1f) > 0.001f;
        slot.startPos = itemTransform.localPosition;
        slot.startRot = itemTransform.localRotation;
        slot.lastV = _rb != null ? _rb.GetPointVelocity(itemTransform.position) : Vector3.zero;
        slot.entering = true;
    }

    private void AdvanceEnter(Slot s, float dt)
    {
        s.t += dt / Mathf.Max(enterTime, 0.01f);
        float u = Mathf.Clamp01(s.t);
        float e = u * u * (3f - 2f * u);

        Vector3 p0 = s.startPos;
        Vector3 p2 = Pivot(s, s.target, s.insideRot, s.shrink);
        Vector3 p1 = (p0 + p2) * 0.5f;
        p1.y = Mathf.Max(Mathf.Max(p0.y, p2.y), _cavityMax.y) + arcHeight * 2f;

        s.tr.localScale = Vector3.Lerp(s.startScale, s.inScale, e);
        s.tr.SetLocalPositionAndRotation(
            Bezier(p0, p1, p2, e),
            Quaternion.Slerp(s.startRot, s.insideRot, e)
        );

        if (u < 1f)
            return;
        s.entering = false;
        s.basePos = s.target;
        s.baseVel = Vector3.zero;
        s.off = Vector3.zero;
        s.offVel = isWobbleEnabled
            ? new Vector3(Random.Range(-0.3f, 0.3f), -0.4f, Random.Range(-0.3f, 0.3f))
            : Vector3.zero;
    }

    private void UpdateSlots(float dt)
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            var s = _slots[i];
            if (s.tr == null)
                continue;
            if (s.entering)
            {
                AdvanceEnter(s, dt);
                continue;
            }
            s.basePos = Vector3.SmoothDamp(
                s.basePos,
                s.target,
                ref s.baseVel,
                SETTLE_TIME,
                Mathf.Infinity,
                dt
            );
            Quaternion rot = Tilt(s.off) * s.insideRot;
            s.tr.SetLocalPositionAndRotation(Pivot(s, s.basePos + s.off, rot, s.shrink), rot);
        }
    }

    private void PurgeSlots()
    {
        bool changed = false;
        for (int i = _slots.Count - 1; i >= 0; i--)
        {
            var s = _slots[i];
            if (s.tr != null && s.tr.parent == _root)
                continue;
            if (s == _previewSlot)
                ClearPreview();

            Debug.LogWarning(
                $"[Box] purge {s.item?.name} parent={s.tr?.parent?.name} held={_pickup.IsHeld} frame={Time.frameCount}",
                this
            );
            _slots.RemoveAt(i);
            if (s.tr != null)
            {
                s.endPos = s.tr.position;
                s.endRot = s.tr.rotation;
                Restore(s);
                Debug.Log(
                    $"[Box] placed {s.item.name} parent={s.tr.parent?.name} kin={s.rb.isKinematic} grav={s.rb.useGravity} pos={s.tr.position} boxPos={transform.position}",
                    this
                );
            }
            changed = true;
        }
        if (changed)
        {
            ApplyTargets();
            UpdateMass();
        }
    }

    // ---------- eject (throw) ----------

    private void HandleThrown(Carriable box, Vector3 v)
    {
        _thrown = true;
        if (shouldEjectOnThrow)
            EjectAll(_rb != null ? _rb.linearVelocity : v);
    }

    // Unstores everything: original parent, scale and physics; inherits box velocity.
    private void EjectAll(Vector3 vel)
    {
        if (_slots.Count == 0)
            return;
        ClearPreview();

        var batch = new Batch { until = Time.time + ejectIgnoreTime };
        GetComponentsInChildren(true, _colTmp);
        foreach (var c in _colTmp)
            if (c != null && !c.transform.IsChildOf(_root))
                batch.box.Add(c);

        foreach (var s in _slots)
        {
            if (s.tr == null)
                continue;
            // Current shrink factor (also right mid-entry).
            float k =
                s.startScale.x > 1e-6f ? Mathf.Max(s.tr.localScale.x / s.startScale.x, 1e-3f) : 1f;
            s.tr.SetParent(s.parent != null ? s.parent : null, true);
            s.fromScale = s.tr.localScale;
            s.endScale = s.fromScale / k;
            s.shrunk = false; // growth handled by UpdateEject
            s.endPos = s.tr.position;
            s.endRot = s.tr.rotation;
            Restore(s);
            if (s.rb != null && !s.kin)
            {
                s.rb.linearVelocity = vel + Random.insideUnitSphere * ejectSpread;
                s.rb.angularVelocity = Random.insideUnitSphere * 2f;
            }
            if (s.cols != null)
                foreach (var c in s.cols)
                    if (c != null && c.enabled)
                        batch.items.Add(c);
            _pops.Add(new Pop { s = s });
            Removed?.Invoke(s.item);
        }
        _slots.Clear();
        UpdateMass();
        IgnorePairs(batch, true);
        _batches.Add(batch);
    }

    private void UpdateEject(float dt)
    {
        for (int i = _pops.Count - 1; i >= 0; i--)
        {
            var p = _pops[i];
            if (p.s.tr == null)
            {
                _pops.RemoveAt(i);
                continue;
            }
            p.t += dt / Mathf.Max(ejectGrowTime, 0.01f);
            float u = Mathf.Clamp01(p.t);
            p.s.tr.localScale = Vector3.Lerp(p.s.fromScale, p.s.endScale, u * u * (3f - 2f * u));
            if (u >= 1f)
                _pops.RemoveAt(i);
        }
        for (int i = _batches.Count - 1; i >= 0; i--)
        {
            if (Time.time < _batches[i].until)
                continue;
            IgnorePairs(_batches[i], false);
            _batches.RemoveAt(i);
        }
    }

    private bool IsPopping(Carriable p)
    {
        for (int i = 0; i < _pops.Count; i++)
            if (_pops[i].s.item == p)
                return true;
        return false;
    }

    private static void IgnorePairs(Batch b, bool on)
    {
        for (int i = 0; i < b.items.Count; i++)
        {
            var a = b.items[i];
            if (a == null)
                continue;
            foreach (var c in b.box)
                if (c != null)
                    Physics.IgnoreCollision(a, c, on);
            for (int j = i + 1; j < b.items.Count; j++)
                if (b.items[j] != null)
                    Physics.IgnoreCollision(a, b.items[j], on);
        }
    }

    // ---------- place ----------

    private void ConfirmPlace()
    {
        var s = _previewSlot;
        if (s == null || s.tr == null)
        {
            ClearPreview();
            return;
        }
        StartLeaving(s, _previewPosition, _previewRotation);
    }

    // Takes the slot out of the box and arcs it to the pose. Finish then frees it in the world,
    // or hands it to its shelf.
    private void StartLeaving(Slot s, Vector3 endPos, Quaternion endRot)
    {
        _slots.Remove(s);
        s.fromPos = s.tr.position;
        s.fromRot = s.tr.rotation;
        s.endPos = endPos;
        s.endRot = endRot;
        s.t = 0f;
        s.tr.SetParent(s.parent != null ? s.parent : null, true);
        s.fromScale = s.tr.localScale;
        s.endScale = s.shrunk ? s.fromScale / s.shrink : s.fromScale;
        s.shrunk = false; // scale now animated by UpdateLeaving
        _leaving.Add(s);

        ClearPreview();
        ApplyTargets();
        UpdateMass();
    }

    private void UpdateLeaving(float dt)
    {
        for (int i = _leaving.Count - 1; i >= 0; i--)
        {
            var s = _leaving[i];
            if (s.tr == null)
            {
                FinishShelfStore(s); // gives the reserved slot back
                _leaving.RemoveAt(i);
                continue;
            }
            s.t += dt / Mathf.Max(placeTime, 0.01f);
            float u = Mathf.Clamp01(s.t);
            float e = u * u * (3f - 2f * u);
            s.tr.localScale = Vector3.Lerp(s.fromScale, s.endScale, e);
            Vector3 mid = (s.fromPos + s.endPos) * 0.5f + Vector3.up * arcHeight * 2f;
            s.tr.SetPositionAndRotation(
                Bezier(s.fromPos, mid, s.endPos, e),
                Quaternion.Slerp(s.fromRot, s.endRot, e)
            );
            if (u < 1f)
                continue;
            _leaving.RemoveAt(i);
            Finish(s);
        }
    }

    private void Finish(Slot s)
    {
        if (s == null)
            return;
        if (s.tr == null)
        {
            FinishShelfStore(s);
            return;
        }
        s.tr.localScale = s.endScale; // leaving items only
        Restore(s);
        FinishShelfStore(s);
        Removed?.Invoke(s.item);
    }

    // Hands an arrived item to its reserved shelf slot. If that fails the item stays where it landed.
    private void FinishShelfStore(Slot s)
    {
        var shelf = s.shelf;
        var shelfSlot = s.shelfSlot;
        s.shelf = null;
        s.shelfSlot = null;
        if (shelf == null)
            return;
        if (!shelf.TryStoreFromBox(s.item, shelfSlot))
            shelf.CancelReservation(shelfSlot);
    }

    private void Restore(Slot s)
    {
        if (s == null || s.tr == null)
            return;
        if (s.shrunk) // released without the place animation
        {
            s.tr.localScale = s.tr.localScale / s.shrink;
            s.shrunk = false;
        }
        s.tr.SetPositionAndRotation(s.endPos, s.endRot);
        if (s.cols != null)
            for (int i = 0; i < s.cols.Length; i++)
                if (s.cols[i] != null)
                    s.cols[i].enabled = s.colOn[i];
        if (s.rb != null)
        {
            s.rb.detectCollisions = s.detect;
            s.rb.isKinematic = s.kin;
            s.rb.useGravity = s.grav;
            if (!s.kin)
            {
                s.rb.position = s.endPos;
                s.rb.rotation = s.endRot;
                s.rb.linearVelocity = Vector3.zero;
                s.rb.angularVelocity = Vector3.zero;
                s.rb.WakeUp();
            }
            s.rb.interpolation = s.interp;
        }
        if (s.item != null)
            s.item.enabled = true;
    }

    // ---------- preview ----------

    private void UpdateInput()
    {
        if (_pickup == null || !_pickup.IsHeld)
        {
            ClearPreview();
            _aimedShelf = null;
            _isShelfStocking = false;
            return;
        }
        _aimedShelf = FindAimedShelf(_pickup.HolderInteractor);
        if (IInput.BoxStorePressed)
        {
            if (_aimedShelf != null)
            {
                _isShelfStocking = TryPlaceSelectedOnShelf(_aimedShelf);
                _nextStockTime = Time.time + shelfStockInterval;
            }
            else if (_previewSlot == null)
                TryStoreAimedItem(_pickup.HolderInteractor);
        }
        else
            UpdateShelfRepeat();

        if (IInput.BoxReturnPressed)
        {
            if (_aimedShelf != null && _aimedShelf.Count > 0)
                TryTakeFromShelf(_aimedShelf); // Q on a stocked shelf takes back instead of cycling
        }
        if (IInput.BoxCycleNextPressed)
            CyclePreviewItem(1);
        // else if (IInput.BoxCyclePrevPressed)
        //     CyclePreviewItem(-1);
        if (_previewSlot == null)
            return;
        if (IInput.BoxCancelPressed)
        {
            ClearPreview();
            return;
        }
        float rotateInput = IInput.BoxRotate;
        if (Mathf.Abs(rotateInput) > 0.01f)
            _previewYaw += Mathf.Sign(rotateInput) * rotateStep;
    }

    // Moves the shelf's newest item back into this box. Only the product already in the box is
    // accepted (an empty box takes any). A refused item goes back on the shelf.
    private void TryTakeFromShelf(ShelfInventory shelf)
    {
        Carriable item = shelf.FindLastStored(GetStoredProductId());
        if (item == null)
            return;
        shelf.Release(item);
        if (TryStore(item))
            return;
        shelf.TryStoreFromBox(item);
        Rejected?.Invoke(item);
    }

    // Product id of what the box holds now (first stored item), null when empty.
    private string GetStoredProductId()
    {
        GetItems(_itemTmp);
        foreach (var stored in _itemTmp)
        {
            var product = stored.GetComponent<ShelfProduct>();
            if (product != null)
                return product.ProductId;
        }
        return null;
    }

    // Holding left click on a shelf keeps stocking, one item per interval. Stops on any failure.
    private void UpdateShelfRepeat()
    {
        if (!_isShelfStocking)
            return;
        if (!IsStoreHeld() || _aimedShelf == null)
        {
            _isShelfStocking = false;
            return;
        }
        if (Time.time < _nextStockTime)
            return;
        _isShelfStocking = TryPlaceSelectedOnShelf(_aimedShelf);
        _nextStockTime = Time.time + shelfStockInterval;
    }

    // Left click still down. Reads the same "BoxStore" extra action as IInput.BoxStorePressed.
    private static bool IsStoreHeld()
    {
        var input = InputManager.Instance;
        if (input == null || !input.CursorLocked || !input.GameplayEnabled)
            return false;
        var action = input.GetExtraAction(
            "BoxStore",
            "<Mouse>/leftButton",
            "<Gamepad>/rightTrigger"
        );
        return action != null && action.IsPressed();
    }

    // The shelf under the crosshair (a stocked item counts as its shelf), else null.
    private ShelfInventory FindAimedShelf(Interactor who)
    {
        Transform origin = who != null ? who.Origin : null;
        if (origin == null)
            return null;
        if (!who.Cast(origin.position, origin.forward, reach, transform, out var hit))
            return null;
        return hit.collider != null ? hit.collider.GetComponentInParent<ShelfInventory>() : null;
    }

    // Select next (+1) / previous (-1) stored item for placing.
    private void CyclePreviewItem(int direction)
    {
        int count = _slots.Count;
        if (count == 0)
            return;
        int current = _previewSlot != null ? _slots.IndexOf(_previewSlot) : -1;
        if (current < 0)
            current = direction > 0 ? -1 : count;
        for (int step = 1; step <= count; step++)
        {
            int index = (((current + direction * step) % count) + count) % count;
            var slot = _slots[index];
            if (slot.entering || slot.tr == null)
                continue;
            if (slot.ghostParts == null)
                slot.ghostParts = BuildGhostParts(slot);
            if (slot != _previewSlot)
                _previewYaw = 0f;
            _previewSlot = slot;
            return;
        }
    }

    private void ClearPreview()
    {
        _previewSlot = null;
        _isPreviewValid = false;
    }

    private void UpdatePreview()
    {
        if (_previewSlot == null)
            return;
        var slot = _previewSlot;
        var holder = _pickup != null ? _pickup.HolderInteractor : null;
        Transform origin = holder != null ? holder.Origin : null;
        if (origin == null || slot.tr == null)
        {
            ClearPreview();
            return;
        }

        ComputePlacementPose(slot, holder, origin);
        SubmitPreviewGhost(slot);
    }

    // Sets the preview pose and whether placing there is valid.
    private void ComputePlacementPose(Slot slot, Interactor holder, Transform origin)
    {
        Quaternion yaw = Quaternion.AngleAxis(_previewYaw, Vector3.up);
        Vector3 worldExtents = Vector3.Scale(slot.bExt, Abs(slot.full)); // placed at full size
        Vector3 centerOffset = Vector3.Scale(slot.bCenter, slot.full);
        Quaternion rotation;
        Vector3 center;
        bool isValid;

        if (holder.Cast(origin.position, origin.forward, reach, transform, out var hit))
        {
            Vector3 normal = hit.normal;
            bool isSlopeOk = Vector3.Angle(normal, Vector3.up) <= maxSlope;
            rotation =
                (
                    shouldAlignToSurface && isSlopeOk
                        ? Quaternion.FromToRotation(Vector3.up, normal)
                        : Quaternion.identity
                )
                * yaw
                * slot.restRot;
            float restHeight =
                Mathf.Abs(Vector3.Dot(normal, rotation * Vector3.right)) * worldExtents.x
                + Mathf.Abs(Vector3.Dot(normal, rotation * Vector3.up)) * worldExtents.y
                + Mathf.Abs(Vector3.Dot(normal, rotation * Vector3.forward)) * worldExtents.z;
            center = hit.point + normal * (restHeight + 0.01f);
            isValid = isSlopeOk && !IsPlacementBlocked(center, worldExtents, rotation);
        }
        else
        {
            rotation = yaw * slot.restRot;
            center = origin.position + origin.forward * reach;
            isValid = false;
        }

        _previewPosition = center - rotation * centerOffset;
        _previewRotation = rotation;
        _isPreviewValid = isValid;
    }

    // Outlines the item mesh at the preview pose (green = valid, red = invalid).
    private void SubmitPreviewGhost(Slot slot)
    {
        if (slot.ghostParts == null)
            return;
        Matrix4x4 itemToWorld = Matrix4x4.TRS(_previewPosition, _previewRotation, slot.full);
        var style = _isPreviewValid ? PreviewValidity.Valid : PreviewValidity.Invalid;
        foreach (var part in slot.ghostParts)
            PickupOutlineRegistry.DrawGhost(
                part.mesh,
                itemToWorld * part.localMatrix,
                part.subMeshCount,
                style
            );
    }

    // Mesh parts of the item in item-local space. Falls back to its bounds box.
    private GhostPart[] BuildGhostParts(Slot slot)
    {
        _ghostPartBuffer.Clear();
        foreach (var filter in slot.tr.GetComponentsInChildren<MeshFilter>(false))
        {
            var mesh = filter != null ? filter.sharedMesh : null;
            var meshRenderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
            if (mesh == null || meshRenderer == null || !meshRenderer.enabled)
                continue;
            _ghostPartBuffer.Add(
                new GhostPart
                {
                    mesh = mesh,
                    localMatrix = slot.tr.worldToLocalMatrix * filter.transform.localToWorldMatrix,
                    subMeshCount = Mathf.Max(1, mesh.subMeshCount),
                }
            );
        }

        if (_ghostPartBuffer.Count == 0)
            _ghostPartBuffer.Add(
                new GhostPart
                {
                    mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx"),
                    localMatrix = Matrix4x4.TRS(slot.bCenter, Quaternion.identity, slot.bExt * 2f),
                    subMeshCount = 1,
                }
            );
        return _ghostPartBuffer.ToArray();
    }

    private bool IsPlacementBlocked(Vector3 center, Vector3 extents, Quaternion rotation)
    {
        int count = Physics.OverlapBoxNonAlloc(
            center,
            extents * 0.95f,
            _overlapBuffer,
            rotation,
            placeMask,
            QueryTriggerInteraction.Ignore
        );
        for (int i = 0; i < count; i++)
        {
            var overlap = _overlapBuffer[i];
            if (overlap != null && !overlap.transform.IsChildOf(transform))
                return true;
        }
        return false;
    }

    // ---------- store highlight ----------

    // Outlines the aimed item while it could be stored; clears when there is nothing to store.
    private void UpdateStoreHighlight() => SetHighlightedItem(FindStoreHighlightTarget());

    private Carriable FindStoreHighlightTarget()
    {
        // While placing, the interact key places, so don't suggest storing.
        if (
            _previewSlot != null
            || _aimedShelf != null
            || _pickup == null
            || !_pickup.IsHeld
            || !_hasCavity
        )
            return null;
        var holder = _pickup.HolderInteractor;
        Transform origin = holder != null ? holder.Origin : null;
        if (origin == null)
            return null;

        Carriable candidate = FindAimedItem(holder, origin);
        return candidate != null && CanStoreCached(candidate) ? candidate : null;
    }

    // CreateSlot allocates, so re-check only when the candidate or box contents change.
    private bool CanStoreCached(Carriable candidate)
    {
        if (candidate != _cachedCandidate || _slots.Count != _cachedSlotCount)
        {
            _cachedCandidate = candidate;
            _cachedSlotCount = _slots.Count;
            _canStoreCachedCandidate = CreateSlot(candidate) != null;
        }
        return _canStoreCachedCandidate;
    }

    private void SetHighlightedItem(Carriable next)
    {
        if (next == _highlightedItem)
            return;
        PickupOutlineRegistry.Unhighlight(_highlightedItem);
        _highlightedItem = next;
        _isAimedItemProduct = next != null && next.TryGetComponent<ProductCarriable>(out _);
        PickupOutlineRegistry.Highlight(_highlightedItem);
    }

    // ---------- layout ----------

    private void ApplyTargets()
    {
        if (_slots.Count == 0 || !TryPackSlots(null))
            return;
        for (int i = 0; i < _slots.Count; i++)
            _slots[i].target = _packCenters[i];
    }

    // Packs all slots (+ an optional candidate) into _packCenters.
    private bool TryPackSlots(Slot extra)
    {
        _packSizes.Clear();
        foreach (var slot in _slots)
            _packSizes.Add(slot.half * 2f);
        if (extra != null)
            _packSizes.Add(extra.half * 2f);
        return _packer.TryPack(
            _packSizes,
            _cavityMin,
            _cavityMax,
            spacing,
            GetAlignFactors(),
            _packCenters
        );
    }

    // 0..1 position along each axis (X: left..right, Y: bottom..top, Z: lower..upper).
    private Vector3 GetAlignFactors()
    {
        int a = (int)alignment;
        float x = (a % 3) * 0.5f;
        int row = a / 3; // 0 upper, 1 middle, 2 lower
        float z = row == 0 ? 1f : (row == 1 ? 0.5f : 0f);
        float y = (int)verticalAlignment * 0.5f;
        return new Vector3(x, y, z);
    }

    private void ComputeCavity()
    {
        var b = new Bounds();
        bool has = false;

        if (cavity != null)
        {
            cavity.isTrigger = true;
            Vector3 c = cavity.center,
                e = cavity.size * 0.5f;
            Matrix4x4 m = cavity.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
                AddPoint(m.MultiplyPoint3x4(c + Corner(e, i)), ref b, ref has);
        }
        else
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer))
                    continue;
                Vector3 c = r.localBounds.center,
                    e = r.localBounds.extents;
                Matrix4x4 m = r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                    AddPoint(m.MultiplyPoint3x4(c + Corner(e, i)), ref b, ref has);
            }
            if (has)
                b.Expand(-2f * Mathf.Max(0f, autoWallThickness));
        }

        _hasCavity = has && b.size.x > 0.01f && b.size.y > 0.01f && b.size.z > 0.01f;
        _cavityMin = b.min;
        _cavityMax = b.max;
    }

    private void AddPoint(Vector3 world, ref Bounds b, ref bool has)
    {
        Vector3 p = _root.InverseTransformPoint(world);
        if (!has)
        {
            b = new Bounds(p, Vector3.zero);
            has = true;
        }
        else
            b.Encapsulate(p);
    }

    // ---------- misc ----------

    private void UpdateMass()
    {
        if (!shouldAddContentMass || _rb == null)
            return;
        float m = _baseMass;
        foreach (var s in _slots)
            m += s.mass;
        _rb.mass = m;
    }

    private void UpdateAnimator()
    {
        if (!_animOk || animator == null || _pickup == null)
            return;
        bool open = _pickup.IsHeld;
        if (open == _open)
            return;
        _open = open;
        animator.SetBool(_openHash, open);
    }

    private static bool MeasureItemBounds(Transform root, out Vector3 center, out Vector3 ext)
    {
        var b = new Bounds();
        bool has = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer))
                continue;
            Encapsulate(root, r.transform.localToWorldMatrix, r.localBounds, ref b, ref has);
        }
        if (!has)
            foreach (var c in root.GetComponentsInChildren<Collider>(false))
            {
                if (c == null || !c.enabled || c.isTrigger)
                    continue;
                Encapsulate(root, Matrix4x4.identity, c.bounds, ref b, ref has);
            }
        center = b.center;
        ext = b.extents;
        return has && ext.x > 1e-4f && ext.y > 1e-4f && ext.z > 1e-4f;
    }

    private static void Encapsulate(
        Transform root,
        Matrix4x4 m,
        Bounds lb,
        ref Bounds b,
        ref bool has
    )
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = root.InverseTransformPoint(
                m.MultiplyPoint3x4(lb.center + Corner(lb.extents, i))
            );
            if (!has)
            {
                b = new Bounds(p, Vector3.zero);
                has = true;
            }
            else
                b.Encapsulate(p);
        }
    }

    private static Vector3 Corner(Vector3 e, int i) =>
        new((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);

    // k = current shrink factor.
    private Vector3 Pivot(Slot s, Vector3 center, Quaternion rot, float k) =>
        center - rot * Vector3.Scale(s.bCenter, s.full * k);

    // Picks the item's stored scale. Sets shrink + half.
    // Varied: largest scale <= 1 that packs. Constant: one fixed scale, no retry.
    private bool ChooseStoredScale(Slot s)
    {
        if (!shouldShrinkItems)
            return TryApplyScale(s, 1f);

        Vector3 d = Vector3.Scale(s.bExt, Abs(s.full)) * 2f;
        float f = storedSize / Mathf.Max(d.x, Mathf.Max(d.y, Mathf.Max(d.z, 1e-4f)));
        bool constant = sizeMode == SizeMode.Constant;
        if (!(constant && shouldScaleUpInConstantMode))
            f = Mathf.Min(1f, f);
        if (f < minScale - 1e-4f)
            return false;
        if (constant)
            return TryApplyScale(s, f);

        while (true)
        {
            if (TryApplyScale(s, f))
                return true;
            if (f <= minScale + 1e-4f)
                return false;
            f = Mathf.Max(minScale, f * 0.85f);
        }
    }

    private bool TryApplyScale(Slot s, float k)
    {
        s.shrink = k;
        s.half = HalfSize(s, k);
        return TryPackSlots(s);
    }

    // Axis-aligned half size inside the box at scale factor k.
    private Vector3 HalfSize(Slot s, float k)
    {
        Vector3 ex = Vector3.Scale(s.bExt, Abs(s.full)) * k;
        return Abs(s.insideRot * Vector3.right) * ex.x
            + Abs(s.insideRot * Vector3.up) * ex.y
            + Abs(s.insideRot * Vector3.forward) * ex.z;
    }

    private Quaternion Tilt(Vector3 off)
    {
        Vector3 h = new(off.x, 0f, off.z);
        float m = h.magnitude;
        if (m < 1e-5f)
            return Quaternion.identity;
        float ang = Mathf.Min(m * tiltPerMeter, maxTilt);
        return Quaternion.AngleAxis(ang, Vector3.Cross(Vector3.up, h / m));
    }

    private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    private static void Clamp1(ref float x, ref float v, float lo, float hi)
    {
        if (x < lo)
        {
            x = lo;
            if (v < 0f)
                v = -v * 0.3f;
        }
        else if (x > hi)
        {
            x = hi;
            if (v > 0f)
                v = -v * 0.3f;
        }
    }

    private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    private static Quaternion Snap(Quaternion q)
    {
        Vector3 e = q.eulerAngles;
        return Quaternion.Euler(
            Mathf.Round(e.x / 90f) * 90f,
            Mathf.Round(e.y / 90f) * 90f,
            Mathf.Round(e.z / 90f) * 90f
        );
    }

    private static float Inv(float v) => Mathf.Abs(v) > 1e-5f ? 1f / v : 1f;

    private void OnValidate()
    {
        autoWallThickness = Mathf.Max(0f, autoWallThickness);
        spacing = Vector3.Max(spacing, Vector3.zero);
        if (Application.isPlaying)
            ApplyTargets(); // live-tweak layout
        reach = Mathf.Max(0.5f, reach);
        aimRadius = Mathf.Clamp(aimRadius, 0f, 0.5f);
        maxItemMass = Mathf.Max(0f, maxItemMass);
        storedSize = Mathf.Max(0.01f, storedSize);
        ejectSpread = Mathf.Max(0f, ejectSpread);
        ejectGrowTime = Mathf.Max(0.01f, ejectGrowTime);
        ejectIgnoreTime = Mathf.Max(0f, ejectIgnoreTime);
        levelBlendTime = Mathf.Max(0f, levelBlendTime);
        enterTime = Mathf.Max(0.05f, enterTime);
        arcHeight = Mathf.Max(0f, arcHeight);
        wobbleFrequency = Mathf.Max(0.5f, wobbleFrequency);
        wobbleStrength = Mathf.Max(0f, wobbleStrength);
        tiltPerMeter = Mathf.Max(0f, tiltPerMeter);
        maxTilt = Mathf.Clamp(maxTilt, 0f, 45f);
        maxSlope = Mathf.Clamp(maxSlope, 0f, 89f);
        placeTime = Mathf.Max(0.05f, placeTime);
        rotateStep = Mathf.Max(1f, rotateStep);
        shelfStockInterval = Mathf.Max(0.05f, shelfStockInterval);
    }
}
