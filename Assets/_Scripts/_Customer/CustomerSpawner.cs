using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Spawns customers only during opening hours. Existing shoppers remain when the store closes.</summary>
[DisallowMultipleComponent]
public class CustomerSpawner : MonoBehaviour
{
    [SerializeField] CustomerAI customerPrefab;
    [SerializeField] CustomerQueue checkoutQueue;
    [SerializeField] Transform[] spawnPoints = System.Array.Empty<Transform>();
    [SerializeField, Min(1f)] float secondsBetweenCustomers = 12f;
    [SerializeField, Min(1)] int maximumActiveCustomers = 4;
    [SerializeField] bool beginSpawningOnStart = true;

    readonly List<CustomerAI> activeCustomers = new();
    Coroutine spawnRoutine;
    DayManager store;
    bool spawningRequested;

    void Start()
    {
        // Older prototype scenes may not yet contain the day-manager prefab.
        if (DayManager.Instance == null)
            new GameObject("DayManager").AddComponent<DayManager>();
        store = DayManager.Instance;
        store.StoreOpened += RefreshSpawning;
        store.StoreClosed += RefreshSpawning;
        store.StateRestored += RefreshSpawning;
        if (beginSpawningOnStart)
            BeginSpawning();
    }

    public void BeginSpawning()
    {
        spawningRequested = true;
        RefreshSpawning();
    }

    public void StopSpawning()
    {
        spawningRequested = false;
        PauseSpawning();
    }

    void PauseSpawning()
    {
        if (spawnRoutine == null)
            return;
        StopCoroutine(spawnRoutine);
        spawnRoutine = null;
    }

    void RefreshSpawning()
    {
        if (spawningRequested && isActiveAndEnabled && store != null && store.IsOpen)
        {
            if (spawnRoutine == null) spawnRoutine = StartCoroutine(SpawnLoop());
        }
        else PauseSpawning();
    }

    public CustomerAI SpawnOne()
    {
        RemoveMissingCustomers();
        if (DayManager.Instance == null || !DayManager.Instance.IsOpen
            || customerPrefab == null || spawnPoints == null || spawnPoints.Length == 0
            || activeCustomers.Count >= maximumActiveCustomers)
            return null;

        var point = spawnPoints[Random.Range(0, spawnPoints.Length)];
        if (point == null)
            return null;

        var customer = Instantiate(customerPrefab, point.position, point.rotation);
        customer.SetQueue(checkoutQueue);
        if (checkoutQueue != null)
            customer.SetExitPoint(checkoutQueue.ExitPoint);
        customer.LeftStore += HandleCustomerLeft;
        activeCustomers.Add(customer);
        CustomerNotificationUI.Show("Customer arrived at the store");
        return customer;
    }

    IEnumerator SpawnLoop()
    {
        while (true)
        {
            SpawnOne();
            yield return new WaitForSeconds(secondsBetweenCustomers);
        }
    }

    void HandleCustomerLeft(CustomerAI customer)
    {
        if (customer != null)
            customer.LeftStore -= HandleCustomerLeft;
        activeCustomers.Remove(customer);
    }

    void RemoveMissingCustomers() => activeCustomers.RemoveAll(customer => customer == null);

    void OnEnable()
    {
        if (store == null) return;
        store.StoreOpened += RefreshSpawning;
        store.StoreClosed += RefreshSpawning;
        store.StateRestored += RefreshSpawning;
        if (beginSpawningOnStart) BeginSpawning();
    }

    void OnDisable()
    {
        if (store != null)
        {
            store.StoreOpened -= RefreshSpawning;
            store.StoreClosed -= RefreshSpawning;
            store.StateRestored -= RefreshSpawning;
        }
        StopSpawning();
    }
}
