using System.Collections;
using UnityEngine;

/// <summary>Prewarms the product pool when the shop scene starts.</summary>
public class ProductPoolBootstrap : MonoBehaviour
{
    [Tooltip("Products to prewarm. Each uses its own Prewarm Count (0 = grow on demand).")]
    [SerializeField]
    private ProductDefinition[] definitions = System.Array.Empty<ProductDefinition>();

    private IEnumerator Start()
    {
        yield return ProductPool.Prewarm(definitions);
    }
}
