using UnityEngine;

/// <summary>
/// One compartment of the cash drawer. Interact (empty hands) to take a coin: it costs its value from
/// your coins, and you can drag it to the change tray. Make one per denomination.
/// </summary>
[RequireComponent(typeof(Interactable))]
public class ChangeDrawer : MonoBehaviour
{
    [Tooltip("Coin spawned for this compartment.")]
    [SerializeField]
    Coin coinPrefab;

    [Tooltip("What the coin is worth, and what taking it costs (coins, min 1).")]
    [SerializeField]
    int value = 1;

    [Tooltip("Where the coin appears. Empty = just above this object.")]
    [SerializeField]
    Transform spawnPoint;

    [Tooltip("Seconds before the next coin can be taken.")]
    [SerializeField]
    float cooldown = 0.15f;

    Interactable _interactable;
    float _nextTime;

    void Awake() => _interactable = GetComponent<Interactable>();

    void OnEnable()
    {
        if (_interactable == null || coinPrefab == null)
        {
            Debug.LogError($"{name}: Interactable/coin prefab missing.", this);
            enabled = false;
            return;
        }
        _interactable.OnInteractEvent += HandleInteract;
    }

    void OnDisable()
    {
        if (_interactable != null)
            _interactable.OnInteractEvent -= HandleInteract;
    }

    void HandleInteract(Interactor who)
    {
        if (who != null && Time.time >= _nextTime)
            TrySpawn();
    }

    /// <summary>Take one coin. False (nothing spent) if you can't afford it.</summary>
    public bool TrySpawn()
    {
        var wallet = CurrencyManager.Instance;
        if (wallet == null || !wallet.TrySpendCoins(value))
            return false;

        _nextTime = Time.time + cooldown;
        Vector3 pos =
            spawnPoint != null ? spawnPoint.position : transform.position + Vector3.up * 0.3f;
        Quaternion rot = spawnPoint != null ? spawnPoint.rotation : transform.rotation;
        Coin coin = Instantiate(coinPrefab, pos, rot);
        coin.SetValue(value); // the coin is always worth exactly what it cost
        return true;
    }

    void OnValidate()
    {
        value = Mathf.Max(1, value);
        cooldown = Mathf.Max(0f, cooldown);
    }
}
