using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lives on the Interactor's GameObject. Picks what the player should be prompted with:
/// the highest-priority registered <see cref="IPromptContextProvider"/>, else the built-in states
/// in this order: furniture placing, carrying, dragging, scanner out, empty-handed (the Interactable
/// under the crosshair). A held object can add bits to the Holding context with IPromptContextFlag.
/// Publishes the merged lines to <see cref="PromptBus"/> only when the source or state changes.
/// </summary>
[FPSAddon("Prompt UI (Sub-Addon)")]
[RequireComponent(typeof(Interactor))]
[DisallowMultipleComponent]
public class PromptPublisher : MonoBehaviour
{
    // Wraps a bare line list (the furniture placer has no Interactable) as an IPromptSource.
    sealed class LinesSource : IPromptSource
    {
        public IReadOnlyList<PromptEntry> Prompts { get; set; }
    }

    Interactor _interactor;
    Carrier _carrier;
    Dragger _dragger;

    readonly List<PromptEntry> _merged = new();
    readonly LinesSource _placingSource = new();
    IPromptSource _source,
        _extra;
    IReadOnlyList<PromptEntry> _sourceLines,
        _extraLines;
    PromptContext _context;
    bool _hasPublished;

    // Resolved once per held / dragged object, not per frame.
    Carriable _heldObj,
        _dragObj;
    Interactable _heldSource,
        _dragSource;
    IPromptContextFlag[] _heldFlags = Array.Empty<IPromptContextFlag>();

    void Awake()
    {
        _interactor = GetComponent<Interactor>();
        _carrier = GetComponent<Carrier>();
        _dragger = GetComponent<Dragger>();
    }

    void OnDisable()
    {
        // Leave the UI hidden and force a fresh publish on re-enable.
        _hasPublished = false;
        _source = _extra = null;
        _sourceLines = _extraLines = null;
        _heldObj = _dragObj = null;
        _heldSource = _dragSource = null;
        _heldFlags = Array.Empty<IPromptContextFlag>();
        PromptBus.Publish(null, PromptContext.EmptyHands);
    }

    void Update()
    {
        var state = ResolveState();
        var sourceLines = state.source?.Prompts;
        var extraLines = state.extra?.Prompts;

        if (
            _hasPublished
            && state.context == _context
            && ReferenceEquals(state.source, _source)
            && ReferenceEquals(state.extra, _extra)
            && ReferenceEquals(sourceLines, _sourceLines)
            && ReferenceEquals(extraLines, _extraLines)
        )
            return; // nothing changed: no list work

        _hasPublished = true;
        _context = state.context;
        _source = state.source;
        _extra = state.extra;
        _sourceLines = sourceLines;
        _extraLines = extraLines;

        _merged.Clear();
        AddLines(sourceLines);
        AddLines(extraLines);
        PromptBus.Publish(_merged, _context);
    }

    PromptState ResolveState()
    {
        IPromptContextProvider best = null;
        PromptState bestState = default;
        var providers = PromptBus.Providers;
        for (int i = 0; i < providers.Count; i++)
        {
            var p = providers[i];
            if (best != null && p.Priority <= best.Priority)
                continue;
            if (!p.TryGetState(out var s))
                continue;
            best = p;
            bestState = s;
        }
        if (best != null)
            return bestState;

        if (TryGetPlacingState(out var placing))
            return placing;
        if (TryGetHeldState(out var held))
            return held;
        if (TryGetDragState(out var drag))
            return drag;
        // if (ScannerTool.IsEquipped) // ScannerPromptHud shows its lines
        //     return new PromptState { context = PromptContext.HoldingScanner };

        return new PromptState { source = _interactor.Target, context = PromptContext.EmptyHands };
    }

    // The furniture preview has no Interactable: it hands over its own lines.
    bool TryGetPlacingState(out PromptState state)
    {
        state = default;
        // var placer = FurniturePlacer.Active;
        // if (placer == null)
        //     return false;
        // _placingSource.Prompts = placer.Prompts;
        // state = new PromptState { source = _placingSource, context = PromptContext.Placing };
        // return true;
        return false;
    }

    // Carrying: the held object's lines, then the aimed-at Interactable's lines.
    bool TryGetHeldState(out PromptState state)
    {
        state = default;
        var held = _interactor.IsHolding ? _interactor.Carrier.Held : null;
        if (held == null)
        {
            _heldObj = null;
            _heldSource = null;
            _heldFlags = Array.Empty<IPromptContextFlag>();
            return false;
        }
        if (held != _heldObj)
        {
            _heldObj = held;
            _heldSource = SourceOf(held);
            _heldFlags = held.GetComponents<IPromptContextFlag>();
        }

        var context = PromptContext.Holding;
        for (int i = 0; i < _heldFlags.Length; i++)
            context |= _heldFlags[i].Flag; // read each frame: look-at bits change while aiming

        state = new PromptState
        {
            source = _heldSource,
            extra = _interactor.Target,
            context = context,
        };
        return true;
    }

    bool TryGetDragState(out PromptState state)
    {
        state = default;
        if (_carrier == null || !_carrier.AreHandsFull)
        {
            _dragObj = null;
            _dragSource = null;
            return false;
        }
        var dragged = _dragger.Dragged.Body.GetComponent<Carriable>();
        if (dragged != _dragObj)
        {
            _dragObj = dragged;
            _dragSource = SourceOf(dragged);
        }
        state = new PromptState { source = _dragSource, context = PromptContext.Dragging };
        return true;
    }

    static Interactable SourceOf(Component c) =>
        c != null && c.TryGetComponent(out Interactable i) ? i : null;

    void AddLines(IReadOnlyList<PromptEntry> lines)
    {
        if (lines == null)
            return;
        for (int i = 0; i < lines.Count; i++)
            _merged.Add(lines[i]);
    }
}
