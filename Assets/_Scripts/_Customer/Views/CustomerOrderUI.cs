using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Displays the current customer's requested products while they are at checkout.</summary>
public class CustomerOrderUI : MonoBehaviour
{
    static CustomerOrderUI instance;

    CustomerAI customer;
    TMP_Text titleLabel;
    TMP_Text instructionLabel;
    TMP_Text itemsLabel;
    GameObject panel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateOnSceneLoad() => EnsureExists();

    public static void Show(CustomerAI nextCustomer)
    {
        var ui = EnsureExists();
        ui.customer = nextCustomer;
        ui.panel.SetActive(nextCustomer != null);
        ui.Refresh();
    }

    public static void Hide(CustomerAI currentCustomer)
    {
        if (instance == null || instance.customer != currentCustomer)
            return;
        instance.customer = null;
        instance.panel.SetActive(false);
    }

    static CustomerOrderUI EnsureExists()
    {
        if (instance != null)
            return instance;

        var root = new GameObject("Customer Order UI");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<CustomerOrderUI>();
        instance.BuildUI();
        return instance;
    }

    void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 99;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        gameObject.AddComponent<GraphicRaycaster>();

        panel = new GameObject("Order Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-32f, -24f);
        rect.sizeDelta = new Vector2(390f, 210f);
        panel.GetComponent<Image>().color = new Color(0.018f, 0.045f, 0.09f, 0.94f);

        var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
        accent.transform.SetParent(panel.transform, false);
        var accentRect = accent.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.offsetMin = Vector2.zero;
        accentRect.offsetMax = new Vector2(6f, 0f);
        accent.GetComponent<Image>().color = new Color(0.28f, 0.87f, 1f, 1f);

        titleLabel = CreateLabel("Title", new Vector2(22f, -18f), new Vector2(-18f, -52f), 27f);
        titleLabel.fontStyle = FontStyles.Bold;
        titleLabel.color = new Color(0.32f, 0.89f, 1f, 1f);

        instructionLabel = CreateLabel(
            "Instruction",
            new Vector2(22f, -55f),
            new Vector2(-18f, -82f),
            16f
        );
        instructionLabel.color = new Color(0.67f, 0.75f, 0.83f, 1f);

        var divider = new GameObject("Divider", typeof(RectTransform), typeof(Image));
        divider.transform.SetParent(panel.transform, false);
        var dividerRect = divider.GetComponent<RectTransform>();
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = new Vector2(1f, 1f);
        dividerRect.offsetMin = new Vector2(22f, -96f);
        dividerRect.offsetMax = new Vector2(-18f, -94f);
        divider.GetComponent<Image>().color = new Color(0.28f, 0.46f, 0.59f, 0.65f);

        itemsLabel = CreateLabel("Items", new Vector2(22f, -110f), new Vector2(-18f, -190f), 23f);
        itemsLabel.color = Color.white;
        panel.SetActive(false);
    }

    TMP_Text CreateLabel(string name, Vector2 topLeft, Vector2 bottomRight, float fontSize)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(panel.transform, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = new Vector2(topLeft.x, bottomRight.y);
        rect.offsetMax = new Vector2(bottomRight.x, topLeft.y);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    void LateUpdate()
    {
        if (customer == null)
        {
            if (panel.activeSelf)
                panel.SetActive(false);
            return;
        }
        Refresh();
    }

    void Refresh()
    {
        if (itemsLabel == null || customer == null)
            return;
        var register = Register.Instance;
        if (customer.IsCheckingOut && register != null && register.State == RegisterState.AwaitingChange)
        {
            titleLabel.text = "RETURN CHANGE";
            instructionLabel.text = "Drag drawer coins onto the change tray";
            itemsLabel.fontSize = 22f;
            itemsLabel.text = $"Total: <b>{register.Total}</b>    Paid: <b>{register.CashPaid}</b>\n"
                + $"Change due: <color=#8CEBFF><b>{register.ChangeDue}</b></color>\n"
                + $"In tray: {register.TrayTotal}    Remaining: <b>{Mathf.Max(0, register.ChangeDue - register.TrayTotal)}</b>";
            return;
        }
        titleLabel.text = "CUSTOMER ORDER";
        instructionLabel.text = "Scan every requested item to finish checkout";
        itemsLabel.fontSize = 23f;
        itemsLabel.text = BuildItemsText();
    }

    string BuildItemsText()
    {
        if (!customer.HasOrder)
            return "<color=#AAB7C4>No order available</color>";

        var text = new System.Text.StringBuilder();
        for (int i = 0; i < customer.Order.Count; i++)
        {
            var line = customer.Order[i];
            if (i > 0)
                text.Append('\n');
            string name = line.Product.DisplayName.Replace('_', ' ').Replace('-', ' ');
            text.Append("<color=#55E5FF>•</color> <b>")
                .Append(name)
                .Append("</b>  <color=#8CEBFF>× ")
                .Append(line.Quantity)
                .Append("</color>");
        }
        return text.ToString();
    }
}
