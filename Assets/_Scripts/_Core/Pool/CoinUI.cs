using DG.Tweening;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(CanvasGroup))]
public class CoinUI : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI label;

    [SerializeField]
    private float floatDistance = 60f;

    [SerializeField]
    private float duration = 0.6f;

    [Tooltip("Text color when the amount is negative. Gains keep the prefab's label color.")]
    [SerializeField]
    private Color lossColor = new Color(1f, 0.35f, 0.35f, 1f);

    private Color _gainColor;
    private RectTransform _rect;
    private CanvasGroup _canvasGroup;

    void Awake()
    {
        CacheComponents();
    }

    void CacheComponents()
    {
        _rect = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();
        if (label == null)
            Debug.LogError("label not assigned.", this);
        else
            _gainColor = label.color;
        if (_canvasGroup == null)
            Debug.LogError("missing CanvasGroup, fade won't work.", this);
    }

    public void Play(Vector2 anchoredPos, int amount)
    {
        if (label == null)
            return;

        label.text = (amount >= 0 ? "+" : "") + amount;
        label.color = amount < 0 ? lossColor : _gainColor;
        _rect.anchoredPosition = anchoredPos;
        if (_canvasGroup != null)
            _canvasGroup.alpha = 1f;
        _rect.localScale = Vector3.one;

        DOTween.Kill(_rect);

        Sequence seq = DOTween.Sequence();
        seq.Append(
            _rect.DOAnchorPosY(anchoredPos.y + floatDistance, duration).SetEase(Ease.OutCubic)
        );
        if (_canvasGroup != null)
            seq.Join(_canvasGroup.DOFade(0f, duration).SetEase(Ease.InCubic));
        seq.SetTarget(_rect);
        _ = seq.OnComplete(() => CoinUIPool.Instance.Return(this));
    }
}
