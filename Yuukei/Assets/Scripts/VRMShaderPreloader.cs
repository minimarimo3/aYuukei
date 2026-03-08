using UnityEngine;

// ビルド時にダミーマテリアルへの参照を保持するためのコンポーネント
public class VRMShaderPreloader : MonoBehaviour
{
    [Tooltip("自動生成されたダミーマテリアル群が登録されます")]
    public Material[] preloadMaterials;
}