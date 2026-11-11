using System.Collections.Generic;
using UnityEngine;

public class CoinUIPool : MonoBehaviour
{
    public static CoinUIPool Instance { get; private set; }

    [SerializeField]
    private CoinUI popupPrefab;

    [SerializeField]
    private RectTransform popupParent; // usually the HUD canvas

    [SerializeField]
    private Camera worldCamera;

    private Canvas _parentCanvas;
    private readonly Queue<CoinUI> _pool = new Queue<CoinUI>();

    Camera GetUICamera() =>
        _parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _parentCanvas.worldCamera;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (popupPrefab == null)
            Debug.LogError("popupPrefab not assigned.", this);
        if (popupParent == null)
            Debug.LogError("popupParent not assigned.", this);
        if (worldCamera == null)
            worldCamera = Camera.main;

        if (popupParent != null)
        {
            _parentCanvas = popupParent.GetComponentInParent<Canvas>();
            if (_parentCanvas == null)
                Debug.LogError("popupParent isn't under a Canvas.", this);
        }
    }

    public void SpawnAtWorldPos(Vector3 worldPos, int amount)
    {
        if (worldCamera == null)
        {
            Debug.LogWarning("CoinUI: worldCamera missing, skipping popup.", this);
            return;
        }
        SpawnAtScreenPos(worldCamera.WorldToScreenPoint(worldPos), amount);
    }

    public void SpawnAtUIElement(RectTransform target, int amount)
    {
        if (target == null || _parentCanvas == null)
        {
            Debug.LogWarning("CoinUI: target/canvas missing, skipping popup.", this);
            return;
        }
        Camera uiCamera = GetUICamera();
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uiCamera, target.position);
        SpawnAtScreenPos(screenPos, amount);
    }

    public void SpawnAtScreenPos(Vector2 screenPos, int amount)
    {
        if (popupPrefab == null || popupParent == null || _parentCanvas == null)
        {
            Debug.LogWarning("CoinUI: missing setup, skipping popup.", this);
            return;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            popupParent,
            screenPos,
            GetUICamera(),
            out Vector2 anchoredPos
        );

        CoinUI popup = Get();
        popup.transform.SetParent(popupParent, false); // false: keep local layout under the canvas
        popup.Play(anchoredPos, amount);
    }

    private CoinUI Get()
    {
        if (_pool.Count > 0)
        {
            CoinUI popup = _pool.Dequeue();
            popup.gameObject.SetActive(true);
            return popup;
        }
        return Instantiate(popupPrefab);
    }

    public void Return(CoinUI popup)
    {
        if (popup == null)
            return;
        popup.gameObject.SetActive(false);
        _pool.Enqueue(popup);
    }
}
