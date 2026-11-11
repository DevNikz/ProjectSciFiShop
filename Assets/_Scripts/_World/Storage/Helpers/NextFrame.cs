using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs an action on the next frame. For work that is illegal while a parent is activating or
/// deactivating (SetParent in OnDisable) and that an inactive object cannot schedule itself.
/// The hidden runner object is created on first use.
/// </summary>
public class NextFrame : MonoBehaviour
{
    static NextFrame _instance;
    static bool _quitting;

    readonly List<Action> _queue = new();
    readonly List<Action> _running = new();

    // Reset statics for "Enter Play Mode without Domain Reload".
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _instance = null;
        _quitting = false;
    }

    /// <summary>Queues the action for the next frame. Ignored while the application quits.</summary>
    public static void Run(Action action)
    {
        if (action == null || _quitting)
            return;
        if (_instance == null)
        {
            var go = new GameObject("[NextFrame]") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<NextFrame>();
        }
        _instance._queue.Add(action);
    }

    void Update()
    {
        if (_queue.Count == 0)
            return;
        _running.AddRange(_queue);
        _queue.Clear();
        foreach (var action in _running)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        _running.Clear();
    }

    void OnApplicationQuit() => _quitting = true;

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}
