using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>What BuildPanel made. The screen copies these into its own fields.</summary>
public class ShopPanelParts
{
    public RectTransform panel;
    public Transform rowParent; // scroll content: rows are created here
    public Transform templateHolder; // inactive: the row template lives here
    public TMP_Text totalLabel,
        coinsLabel,
        statusLabel;
    public Button buyButton,
        closeButton,
        upgradesButton; // upgradesButton only if asked for
}

/// <summary>What BuildRowLayout made. The row component copies these into its own fields.</summary>
public class RowParts
{
    public Image icon;
    public TMP_Text nameLabel,
        detailLabel,
        qtyLabel,
        unlockLabel;
    public Button plusButton,
        minusButton,
        unlockButton;
}

/// <summary>
/// Builds the inside of a built-in order or furniture window: header row, scroll list, footer. Also the
/// layout of one row, so the header columns line up with the rows. Needs a UIStyle (null = the default look).
/// </summary>
public static class ShopPanelBuilder
{
    // The columns shared by a row and the header: icon, name, detail, -, qty, +, unlock.
    class RowCells
    {
        public RectTransform icon,
            name,
            detail,
            minus,
            qty,
            plus,
            unlock;
    }

    /// <summary>
    /// Panel (header + scroll list + footer) stretched inside parent. Null (and an error) if parent is missing.
    /// </summary>
    public static ShopPanelParts BuildPanel(
        Transform parent,
        UIStyle style,
        string nameTitle,
        string detailTitle,
        string qtyTitle,
        bool withUpgradesButton
    )
    {
        if (parent == null)
        {
            Debug.LogError("ShopPanelBuilder: no parent to build the panel in.");
            return null;
        }
        if (style == null)
            style = UIStyle.Fallback;
        var l = style.List;

        var parts = new ShopPanelParts { panel = UIBuilder.CreateStretched("Panel", parent) };

        var header = BuildHeaderRow(parts.panel, style, nameTitle, detailTitle, qtyTitle);
        header.anchorMin = new Vector2(0f, 1f); // strip hanging from the top
        header.anchorMax = Vector2.one;
        header.pivot = new Vector2(0.5f, 1f);
        header.anchoredPosition = new Vector2(0f, -l.padding);
        header.sizeDelta = new Vector2(-2f * l.padding, l.headerHeight);

        var scroll = BuildScrollList(parts.panel, l, out RectTransform content);
        scroll.offsetMin = new Vector2(l.padding, l.padding + l.footerHeight + l.padding);
        scroll.offsetMax = new Vector2(-l.padding, -(l.padding + l.headerHeight + l.rowSpacing));
        parts.rowParent = content;

        BuildFooter(parts, style, withUpgradesButton);

        var holder = UIBuilder.CreateStretched("Templates", parts.panel);
        holder.gameObject.SetActive(false); // clones made from the template start active
        parts.templateHolder = holder;
        return parts;
    }

    /// <summary>
    /// Fills a row object (needs a RectTransform) with the default row layout: icon, name, detail,
    /// - qty +, unlock. Null (and an error) if root is missing.
    /// </summary>
    public static RowParts BuildRowLayout(RectTransform root, UIStyle style)
    {
        if (root == null)
        {
            Debug.LogError("ShopPanelBuilder: no row object to build the layout on.");
            return null;
        }
        if (style == null)
            style = UIStyle.Fallback;
        var l = style.List;
        var b = style.Button;

        root.gameObject.AddComponent<Image>().color = l.rowColor;
        AddRowLayoutGroup(root.gameObject, l, 0f);
        var element = root.gameObject.AddComponent<LayoutElement>();
        element.minHeight = l.rowHeight;
        element.preferredHeight = l.rowHeight;

        var cells = CreateCells(root, style);
        var parts = new RowParts();

        var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconObject.transform.SetParent(cells.icon, false);
        CenterFixed((RectTransform)iconObject.transform, new Vector2(l.iconSize, l.iconSize));
        parts.icon = iconObject.GetComponent<Image>();
        parts.icon.preserveAspect = true;
        parts.icon.raycastTarget = false;

        parts.nameLabel = AddLabel(cells.name, "Name", l.nameText, null);
        parts.nameLabel.overflowMode = TextOverflowModes.Ellipsis;
        parts.detailLabel = AddLabel(cells.detail, "Detail", l.detailText, null);
        parts.detailLabel.overflowMode = TextOverflowModes.Ellipsis;
        parts.qtyLabel = AddLabel(cells.qty, "Qty", l.qtyText, "0");

        parts.minusButton = AddCellButton(cells.minus, "Minus", "-", b, b.stepSize, b.height);
        parts.plusButton = AddCellButton(cells.plus, "Plus", "+", b, b.stepSize, b.height);
        parts.unlockButton = AddCellButton(
            cells.unlock,
            "Unlock",
            "Unlock",
            b,
            l.unlockWidth,
            b.height
        );
        parts.unlockLabel = parts.unlockButton.GetComponentInChildren<TMP_Text>(true);
        return parts;
    }

    // Column titles over the row columns. Extra right padding matches the scrollbar next to the rows.
    static RectTransform BuildHeaderRow(
        Transform parent,
        UIStyle style,
        string nameTitle,
        string detailTitle,
        string qtyTitle
    )
    {
        var l = style.List;
        var rect = UIBuilder.CreateStretched("Header", parent);
        rect.gameObject.AddComponent<Image>().color = l.headerColor;
        AddRowLayoutGroup(rect.gameObject, l, l.scrollbarWidth);

        var cells = CreateCells(rect, style);
        AddLabel(cells.name, "Title", l.headerText, nameTitle);
        AddLabel(cells.detail, "Title", l.headerText, detailTitle);
        AddLabel(cells.qty, "Title", l.headerText, qtyTitle).alignment =
            TextAlignmentOptions.Center;
        return rect;
    }

    // Viewport (masked) + content (rows stack here) + a permanent scrollbar, so the row width never changes.
    static RectTransform BuildScrollList(Transform parent, ListStyle l, out RectTransform content)
    {
        var root = UIBuilder.CreateStretched("Scroll", parent);
        root.gameObject.AddComponent<Image>().color = l.backgroundColor; // also catches drags between rows
        var scroll = root.gameObject.AddComponent<ScrollRect>();

        var viewport = UIBuilder.CreateStretched("Viewport", root);
        viewport.offsetMax = new Vector2(-l.scrollbarWidth, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();

        content = UIBuilder.CreateStretched("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f); // top-anchored, grows downward
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        int gap = Mathf.RoundToInt(l.rowSpacing);
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = l.rowSpacing;
        layout.padding = new RectOffset(0, 0, gap, gap);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter
            .FitMode
            .PreferredSize;

        var barRect = UIBuilder.CreateStretched("Scrollbar", root);
        barRect.anchorMin = new Vector2(1f, 0f); // strip on the right edge
        barRect.anchorMax = Vector2.one;
        barRect.pivot = new Vector2(1f, 0.5f);
        barRect.anchoredPosition = Vector2.zero;
        barRect.sizeDelta = new Vector2(l.scrollbarWidth, 0f);
        barRect.gameObject.AddComponent<Image>().color = l.scrollbarTrackColor;
        var handle = UIBuilder.CreateStretched("Handle", barRect);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = l.scrollbarHandleColor;
        var bar = barRect.gameObject.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = handle;
        bar.targetGraphic = handleImage;

        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = l.scrollSensitivity;
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        return root;
    }

    // Total, coins, status (takes the free space), then the buttons.
    static void BuildFooter(ShopPanelParts parts, UIStyle style, bool withUpgradesButton)
    {
        var l = style.List;
        var footer = UIBuilder.CreateStretched("Footer", parts.panel);
        footer.anchorMin = Vector2.zero; // strip standing on the bottom
        footer.anchorMax = new Vector2(1f, 0f);
        footer.pivot = new Vector2(0.5f, 0f);
        footer.anchoredPosition = new Vector2(0f, l.padding);
        footer.sizeDelta = new Vector2(-2f * l.padding, l.footerHeight);
        footer.gameObject.AddComponent<Image>().color = l.footerColor;

        int side = Mathf.RoundToInt(l.rowPadding);
        var layout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(side, side, 0, 0);
        layout.spacing = l.cellSpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        parts.totalLabel = AddFooterLabel(footer, "Total", l.footerText, 200f, 0f);
        parts.coinsLabel = AddFooterLabel(footer, "Coins", l.footerText, 200f, 0f);
        parts.statusLabel = AddFooterLabel(footer, "Status", l.footerText, 0f, 1f);
        if (withUpgradesButton)
            parts.upgradesButton = AddFooterButton(footer, "Upgrades", style.Button);
        parts.buyButton = AddFooterButton(footer, "Buy", style.Button);
        parts.closeButton = AddFooterButton(footer, "Close", style.Button);
    }

    static TMP_Text AddFooterLabel(
        Transform parent,
        string objectName,
        UITextStyle textStyle,
        float width,
        float flexible
    )
    {
        var text = UIBuilder.CreateText(parent, objectName, textStyle);
        var element = text.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.flexibleWidth = flexible;
        return text;
    }

    // The label doubles as the button text (Buy, Close, Upgrades).
    static Button AddFooterButton(Transform parent, string label, ButtonStyle b)
    {
        var button = UIBuilder.CreateButton(parent, label, label, b);
        var element = button.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = b.width;
        element.preferredHeight = b.height;
        return button;
    }

    static RowCells CreateCells(Transform parent, UIStyle style)
    {
        var l = style.List;
        var b = style.Button;
        return new RowCells
        {
            icon = AddCell(parent, "IconCell", l.iconColumnWidth, 0f),
            name = AddCell(parent, "NameCell", l.nameMinWidth, 1f),
            detail = AddCell(parent, "DetailCell", l.detailMinWidth, 1.4f),
            minus = AddCell(parent, "MinusCell", b.stepSize, 0f),
            qty = AddCell(parent, "QtyCell", l.qtyWidth, 0f),
            plus = AddCell(parent, "PlusCell", b.stepSize, 0f),
            unlock = AddCell(parent, "UnlockCell", l.unlockWidth, 0f), // fixed: a hidden unlock button keeps its space
        };
    }

    static RectTransform AddCell(Transform parent, string objectName, float width, float flexible)
    {
        var cell = new GameObject(objectName, typeof(RectTransform), typeof(LayoutElement));
        cell.transform.SetParent(parent, false);
        var element = cell.GetComponent<LayoutElement>();
        element.minWidth = width;
        element.preferredWidth = width;
        element.flexibleWidth = flexible;
        return (RectTransform)cell.transform;
    }

    // Same padding and spacing for rows and the header. rightExtra = the scrollbar width, header only.
    static void AddRowLayoutGroup(GameObject target, ListStyle l, float rightExtra)
    {
        int side = Mathf.RoundToInt(l.rowPadding);
        var layout = target.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(side, side + Mathf.RoundToInt(rightExtra), 0, 0);
        layout.spacing = l.cellSpacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
    }

    static TMP_Text AddLabel(RectTransform cell, string objectName, UITextStyle style, string text)
    {
        var label = UIBuilder.CreateText(cell, objectName, style);
        UIBuilder.Stretch(label.rectTransform);
        if (text != null)
            label.text = text;
        return label;
    }

    static Button AddCellButton(
        RectTransform cell,
        string objectName,
        string label,
        ButtonStyle style,
        float width,
        float height
    )
    {
        var button = UIBuilder.CreateButton(cell, objectName, label, style);
        CenterFixed((RectTransform)button.transform, new Vector2(width, height));
        return button;
    }

    static void CenterFixed(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }
}
