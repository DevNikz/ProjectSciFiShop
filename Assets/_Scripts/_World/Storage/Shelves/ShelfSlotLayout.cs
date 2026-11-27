using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Supplies the slots of one ShelfInventory.
/// Grid: slots are generated (in the editor) as child Transforms in columns, rows and levels, listed in
/// the order products fill them. SceneReferences: you place the slots yourself and list them.
/// Front is this object's local +Z (the side customers stand on). Left and right are as seen from there.
/// All sizes are in metres. This object's own scale is ignored, so a stretched shelf board is fine.
/// Several layouts can feed one ShelfInventory (one per board).
/// </summary>
[DisallowMultipleComponent]
public class ShelfSlotLayout : MonoBehaviour
{
    public enum SlotMode
    {
        Grid,
        SceneReferences,
    }

    public enum RowOrder
    {
        FrontToBack,
        BackToFront,
    }

    public enum ColumnOrder
    {
        LeftToRight,
        RightToLeft,
        CenterOut,
    }

    [Serializable]
    public struct SlotPadding
    {
        public float left;
        public float right;
        public float front;
        public float back;
    }

    const string ContainerName = "Generated Slots";
    const float GizmoCellHeight = 0.02f;
    const float GizmoActualRadius = 0.015f;

    static readonly Color GizmoFootprintColor = new(1f, 1f, 1f, 0.5f);
    static readonly Color GizmoCellColor = new(0.25f, 0.9f, 1f, 1f);
    static readonly Color GizmoFirstColor = new(0.3f, 1f, 0.4f, 1f);
    static readonly Color GizmoActualColor = new(1f, 0.85f, 0.2f, 1f);

    [Tooltip(
        "Grid = slots are generated from the fields below. SceneReferences = you place the slots and list them."
    )]
    [SerializeField]
    SlotMode mode = SlotMode.Grid;

    [Header("Grid")]
    [Tooltip("Slots across the shelf (min 1).")]
    [SerializeField]
    int columns = 4;

    [Tooltip("Slots front to back (min 1). Each row fills completely before the next one starts.")]
    [SerializeField]
    int rows = 2;

    [Tooltip("Tiers stacked upward (min 1). The bottom level fills first.")]
    [SerializeField]
    int levels = 1;

    [Tooltip("Width (X) and depth (Z) of one slot, in metres. The object's scale is ignored.")]
    [SerializeField]
    Vector2 cellSize = new(0.28f, 0.28f);

    [Tooltip("Gap between neighbouring slots: X between columns, Z between rows.")]
    [SerializeField]
    Vector2 spacing = new(0.02f, 0.02f);

    [Tooltip(
        "Margin between the shelf edge and the first slot. Left and right are seen from the front."
    )]
    [SerializeField]
    SlotPadding padding = new()
    {
        left = 0.03f,
        right = 0.03f,
        front = 0.03f,
        back = 0.03f,
    };

    [Tooltip("Height between levels, in metres.")]
    [SerializeField]
    float levelSpacing = 0.35f;

    [Tooltip("Height of the slot pivots on the first level. Slots sit at product-centre height.")]
    [SerializeField]
    float slotHeightOffset;

    [Tooltip(
        "Moves the whole grid sideways (X) and front-back (Z), in metres. Use it when this object's pivot is not at the middle of the shelf surface."
    )]
    [SerializeField]
    Vector2 centerOffset;

    [Tooltip("Which row fills first. FrontToBack = the row nearest the customer first.")]
    [SerializeField]
    RowOrder rowOrder = RowOrder.FrontToBack;

    [Tooltip("Order inside a row, seen from the front. CenterOut starts in the middle.")]
    [SerializeField]
    ColumnOrder columnOrder = ColumnOrder.LeftToRight;

    [Tooltip("Filled by the 'Generate slots' context menu, in fill order. Do not edit by hand.")]
    [SerializeField]
    Transform[] generatedSlots = Array.Empty<Transform>();

    [SerializeField, HideInInspector]
    Transform generatedContainer;

    [Header("Scene References")]
    [Tooltip("Slots you placed yourself, in fill order. One product fits in each slot.")]
    [SerializeField]
    Transform[] sceneSlots = Array.Empty<Transform>();

    [Header("Debug")]
    [Tooltip("Draw the layout in the Scene and Game view (always, not only when selected).")]
    [SerializeField]
    bool showGizmos = true;

    readonly List<Vector3Int> gizmoCells = new();

    public SlotMode Mode => mode;
    public int Columns => columns;
    public int Rows => rows;
    public int Levels => levels;
    public Vector2 CellSize => cellSize;
    public Vector2 Spacing => spacing;
    public SlotPadding Padding => padding;
    public float LevelSpacing => levelSpacing;
    public float SlotHeightOffset => slotHeightOffset;
    public Vector2 CenterOffset => centerOffset;
    public RowOrder RowFillOrder => rowOrder;
    public ColumnOrder ColumnFillOrder => columnOrder;

    /// <summary>Slots in fill order for the current mode. Never null; entries can be null if a slot was deleted.</summary>
    public IReadOnlyList<Transform> Slots =>
        (mode == SlotMode.Grid ? generatedSlots : sceneSlots) ?? Array.Empty<Transform>();

    /// <summary>Width (X) and depth (Z) of the grid including padding, in metres.</summary>
    public Vector2 FootprintSize =>
        new(
            columns * cellSize.x + (columns - 1) * spacing.x + padding.left + padding.right,
            rows * cellSize.y + (rows - 1) * spacing.y + padding.front + padding.back
        );

    /// <summary>cell = (column from the left, row from the front, level). Local position of its slot pivot.</summary>
    Vector3 GetCellLocalCenter(Vector3Int cell)
    {
        Vector2 footprint = FootprintSize;
        // Seen from the front, left is local +X and front is local +Z.
        float x =
            centerOffset.x
            + footprint.x * 0.5f
            - padding.left
            - cellSize.x * 0.5f
            - cell.x * (cellSize.x + spacing.x);
        float z =
            centerOffset.y
            + footprint.y * 0.5f
            - padding.front
            - cellSize.y * 0.5f
            - cell.y * (cellSize.y + spacing.y);
        float y = slotHeightOffset + cell.z * levelSpacing;
        return new Vector3(x, y, z);
    }

    /// <summary>Lists every grid cell in the order products fill them: level, then row, then column.</summary>
    void BuildFillOrder(List<Vector3Int> cells)
    {
        cells.Clear();
        for (int level = 0; level < levels; level++)
        for (int rowStep = 0; rowStep < rows; rowStep++)
        {
            int row = rowOrder == RowOrder.FrontToBack ? rowStep : rows - 1 - rowStep;
            for (int columnStep = 0; columnStep < columns; columnStep++)
                cells.Add(new Vector3Int(ResolveColumn(columnStep), row, level));
        }
    }

    int ResolveColumn(int step)
    {
        switch (columnOrder)
        {
            case ColumnOrder.RightToLeft:
                return columns - 1 - step;
            case ColumnOrder.CenterOut:
                // 0, +1, -1, +2, -2 ... around the middle column (left-middle when even).
                int offset = step == 0 ? 0 : (step % 2 == 1 ? (step + 1) / 2 : -(step / 2));
                return (columns - 1) / 2 + offset;
            default:
                return step;
        }
    }

    [ContextMenu("Generate slots")]
    void GenerateSlots()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Debug.LogWarning($"{name}: generate slots in Edit Mode so they are saved.", this);
            return;
        }
        if (mode != SlotMode.Grid)
        {
            Debug.LogWarning($"{name}: switch Mode to Grid to generate slots.", this);
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Generate shelf slots");
        int undoGroup = Undo.GetCurrentGroup();

        DestroyGenerated();

        var container = new GameObject(ContainerName).transform;
        Undo.RegisterCreatedObjectUndo(container.gameObject, "Generate shelf slots");
        container.SetParent(transform, false);
        // Cancel this object's scale so slots (and the products parented to them) are unscaled.
        Vector3 scale = transform.lossyScale;
        container.localScale = new Vector3(
            scale.x != 0f ? 1f / scale.x : 1f,
            scale.y != 0f ? 1f / scale.y : 1f,
            scale.z != 0f ? 1f / scale.z : 1f
        );

        var cells = new List<Vector3Int>();
        BuildFillOrder(cells);
        var created = new Transform[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var slot = new GameObject($"Slot_L{cell.z}_R{cell.y}_C{cell.x}").transform;
            slot.SetParent(container, false);
            slot.localPosition = GetCellLocalCenter(cell);
            created[i] = slot;
        }

        Undo.RecordObject(this, "Generate shelf slots");
        generatedContainer = container;
        generatedSlots = created;
        EditorUtility.SetDirty(this);
        PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"{name}: generated {created.Length} slot(s).", this);
#endif
    }

    [ContextMenu("Clear generated slots")]
    void ClearGeneratedSlots()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Debug.LogWarning($"{name}: clear slots in Edit Mode.", this);
            return;
        }
        DestroyGenerated();
#endif
    }

#if UNITY_EDITOR
    void DestroyGenerated()
    {
        Undo.RecordObject(this, "Clear generated slots");
        if (generatedContainer != null)
            Undo.DestroyObjectImmediate(generatedContainer.gameObject);
        generatedContainer = null;
        generatedSlots = Array.Empty<Transform>();
        EditorUtility.SetDirty(this);
    }
#endif

    static bool HasNullEntry(Transform[] list)
    {
        if (list == null)
            return false;
        foreach (var entry in list)
            if (entry == null)
                return true;
        return false;
    }

    void OnValidate()
    {
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);
        levels = Mathf.Max(1, levels);
        cellSize = Vector2.Max(cellSize, new Vector2(0.01f, 0.01f));
        spacing = Vector2.Max(spacing, Vector2.zero);
        padding.left = Mathf.Max(0f, padding.left);
        padding.right = Mathf.Max(0f, padding.right);
        padding.front = Mathf.Max(0f, padding.front);
        padding.back = Mathf.Max(0f, padding.back);
        levelSpacing = Mathf.Max(0f, levelSpacing);

        if (mode == SlotMode.Grid)
        {
            int expected = columns * rows * levels;
            int actual = generatedSlots != null ? generatedSlots.Length : 0;
            if (actual != expected || HasNullEntry(generatedSlots))
                Debug.LogWarning(
                    $"{name}: grid has {actual} generated slot(s) but {expected} are expected. Run 'Generate slots'.",
                    this
                );
        }
        else if (sceneSlots == null || sceneSlots.Length == 0)
            Debug.LogWarning($"{name}: no scene slots, so this shelf holds nothing.", this);
        else if (HasNullEntry(sceneSlots))
            Debug.LogWarning($"{name}: a scene slot is missing (null entry).", this);
    }

    void OnDrawGizmos()
    {
        if (!showGizmos)
            return;
        if (mode == SlotMode.Grid)
            DrawGridGizmos();
        else
            DrawSceneSlotGizmos();
    }

    void DrawGridGizmos()
    {
        BuildFillOrder(gizmoCells);
        Vector2 footprint = FootprintSize;
        var footprintBox = new Vector3(footprint.x, GizmoCellHeight * 0.25f, footprint.y);
        var cellBox = new Vector3(cellSize.x, GizmoCellHeight, cellSize.y);

        Matrix4x4 matrix = GetUnscaledMatrix(transform);
        Gizmos.matrix = matrix;
        Gizmos.color = GizmoFootprintColor;
        for (int level = 0; level < levels; level++)
            Gizmos.DrawWireCube(
                new Vector3(
                    centerOffset.x,
                    slotHeightOffset + level * levelSpacing,
                    centerOffset.y
                ),
                footprintBox
            );

        Vector3 previous = default;
        for (int i = 0; i < gizmoCells.Count; i++)
        {
            Vector3 center = GetCellLocalCenter(gizmoCells[i]);
            Gizmos.color = i == 0 ? GizmoFirstColor : GizmoCellColor;
            Gizmos.DrawWireCube(center, cellBox);
            if (i > 0)
                Gizmos.DrawLine(previous, center);
            previous = center;
            DrawIndexLabel(matrix.MultiplyPoint3x4(center), i);
        }

        // What is actually in the scene. A yellow dot off its cell means the layout is stale.
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = GizmoActualColor;
        if (generatedSlots != null)
            foreach (var slot in generatedSlots)
                if (slot != null)
                    Gizmos.DrawWireSphere(slot.position, GizmoActualRadius);
    }

    void DrawSceneSlotGizmos()
    {
        if (sceneSlots == null)
            return;
        var cellBox = new Vector3(cellSize.x, GizmoCellHeight, cellSize.y);
        Transform previous = null;
        for (int i = 0; i < sceneSlots.Length; i++)
        {
            var slot = sceneSlots[i];
            if (slot == null)
                continue;
            Gizmos.matrix = GetUnscaledMatrix(slot);
            Gizmos.color = previous == null ? GizmoFirstColor : GizmoCellColor;
            Gizmos.DrawWireCube(Vector3.zero, cellBox);
            Gizmos.matrix = Matrix4x4.identity;
            if (previous != null)
                Gizmos.DrawLine(previous.position, slot.position);
            previous = slot;
            DrawIndexLabel(slot.position, i);
        }
    }

    static Matrix4x4 GetUnscaledMatrix(Transform target) =>
        Matrix4x4.TRS(target.position, target.rotation, Vector3.one);

    static void DrawIndexLabel(Vector3 worldPosition, int index)
    {
#if UNITY_EDITOR
        Handles.Label(worldPosition, index.ToString());
#endif
    }
}
