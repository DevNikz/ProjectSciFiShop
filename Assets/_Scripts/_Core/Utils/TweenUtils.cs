using DG.Tweening;
using UnityEngine;

public static class TweenUtils
{
    public static void IconPunchScale(RectTransform target, Vector3 baseScale, float punchScale)
    {
        target.DOKill();
        target.localScale = baseScale;
        target.DOPunchScale(Vector3.one * punchScale, 0.3f, 6, 0.5f);
    }

    public static void IconPunchScale(Transform target, Vector3 baseScale, float punchScale)
    {
        target.DOKill();
        target.localScale = baseScale;
        target.DOPunchScale(Vector3.one * punchScale, 0.3f, 6, 0.5f);
    }
}
