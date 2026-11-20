using System;
using UnityEngine;

/// <summary>
/// A coin worth some wallet coins. Sits next to a Carriable (grab, throw, drag come from it).
/// The player collects it by walking into its trigger collider.
/// </summary>
[RequireComponent(typeof(Carriable))]
public class Coin : MonoBehaviour, ICarriableInteractHandler, ICarriablePoolable, IHasCarriable
{
    [Header("Currency")]
    [Tooltip("Coins added on collect (min 1).")]
    [SerializeField]
    private int value = 1;
    private Carriable _carriable;
    private bool _collected;
    private int _defaultValue; // prefab value, captured on the first pool spawn

    public int Value => value;
    public Carriable Carriable => _carriable;

    public event Action<Coin> Collected;

    private void Awake() => _carriable = GetComponent<Carriable>();

    public void SetValue(int coins) => value = Math.Max(1, coins);

    public bool TryHandleInteract(Carriable carriable, Interactor who)
    {
        if (who == null)
            return false;
        TryCollect();
        return true;
    }

    public bool TryCollect()
    {
        var wallet = CurrencyManager.Instance;
        if (_collected || wallet == null)
            return false;

        _collected = true;
        wallet.AddCoins(value);
        Collected?.Invoke(this);
        _carriable.Despawn();
        return true;
    }

    public void OnPoolSpawn()
    {
        if (_defaultValue < 1)
            _defaultValue = value; // first use: still the prefab value
        value = _defaultValue; // undo a SetValue from the last use
        _collected = false;
    }

    public void OnPoolRelease() => Collected = null;

    private void OnValidate() => value = Math.Max(1, value);
}
