using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Always-visible store status and an explicit open/close control.</summary>
public class StoreControlUI : MonoBehaviour
{
    DayManager manager;
    TMP_Text label;
    GameObject canvasObject;

    void Start()
    {
        manager = GetComponent<DayManager>();
        if (manager == null)
            manager = DayManager.Instance;
        canvasObject = new GameObject("Store Status", typeof(Canvas), typeof(CanvasScaler));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.GetComponent<Canvas>().sortingOrder = 90;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var textObject = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(canvasObject.transform, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(28, -60);
        rect.sizeDelta = new Vector2(480, 76);
        label = textObject.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = 20;
        label.raycastTarget = false;
        label.color = Color.white;
    }

    void Update()
    {
        if (manager == null || label == null)
            return;
        bool canControl =
            Time.timeScale > 0f
            && (InputManager.Instance == null || InputManager.Instance.GameplayEnabled);
        if (canControl && Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame)
        {
            if (manager.IsOpen)
                manager.CloseStore();
            else if (manager.Phase == StorePhase.Waiting && manager.StartNextDay())
                CustomerNotificationUI.Show("Store open — customers can now enter");
        }
        label.text =
            manager.IsOpen ? "<color=#72E6AE><b>STORE OPEN</b></color>  [F6] Close store"
            : manager.Phase == StorePhase.Closing
                ? "<color=#FFD180><b>STORE CLOSING</b></color>\nServing the customers already inside"
            : "<b>STORE CLOSED</b>  [F6] Open store\n<size=80%>Stock your shelves before opening</size>";
    }

    void OnDestroy()
    {
        if (canvasObject != null)
            Destroy(canvasObject);
    }
}
