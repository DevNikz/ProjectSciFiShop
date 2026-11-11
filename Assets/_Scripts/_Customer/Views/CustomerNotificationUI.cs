using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Small runtime-created toast used by the customer prototype.</summary>
public class CustomerNotificationUI : MonoBehaviour
{
    static CustomerNotificationUI instance;

    readonly Queue<string> messages = new();
    TMP_Text label;
    CanvasGroup canvasGroup;
    Coroutine displayRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateOnSceneLoad() => EnsureExists();

    public static void Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var notifications = EnsureExists();
        notifications.messages.Enqueue(message);
        if (notifications.displayRoutine == null)
            notifications.displayRoutine = notifications.StartCoroutine(
                notifications.DisplayMessages()
            );
    }

    static CustomerNotificationUI EnsureExists()
    {
        if (instance != null)
            return instance;

        var root = new GameObject("Customer Notifications");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<CustomerNotificationUI>();
        instance.BuildUI();
        return instance;
    }

    void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        var panel = new GameObject(
            "Toast",
            typeof(RectTransform),
            typeof(Image),
            typeof(CanvasGroup)
        );
        panel.transform.SetParent(transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -42f);
        panelRect.sizeDelta = new Vector2(420f, 48f);
        panel.GetComponent<Image>().color = new Color(0.025f, 0.06f, 0.11f, 0.9f);
        canvasGroup = panel.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;

        var textObject = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(panel.transform, false);
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 5f);
        textRect.offsetMax = new Vector2(-12f, -5f);

        label = textObject.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = 19f;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.color = new Color(0.4f, 0.9f, 1f, 1f);
    }

    IEnumerator DisplayMessages()
    {
        while (messages.Count > 0)
        {
            label.text = messages.Dequeue();
            yield return Fade(0f, 1f, 0.16f);
            yield return new WaitForSecondsRealtime(2.4f);
            yield return Fade(1f, 0f, 0.2f);
            yield return new WaitForSecondsRealtime(0.08f);
        }

        displayRoutine = null;
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        canvasGroup.alpha = to;
    }
}
