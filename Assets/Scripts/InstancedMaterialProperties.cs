using UnityEngine;

public class InstancedMaterialProperties : MonoBehaviour
{
    static MaterialPropertyBlock propertyBlock;

    static int colorID = Shader.PropertyToID("_Color");
    static int smoothnessId = Shader.PropertyToID("_Smoothness");

    [SerializeField]
    Color color = Color.white;

    [SerializeField, Range(0f, 1f)]
    float smoothness = 0.5f;

    private void Awake()
    {
        OnValidate();
    }

    private void OnValidate()
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
        propertyBlock.SetColor(colorID, color);
        propertyBlock.SetFloat(smoothnessId, smoothness);
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            meshRenderer.SetPropertyBlock(propertyBlock);
    }
}
