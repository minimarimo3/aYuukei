#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class VRMDummyMaterialGenerator
{
    private const string ExportFolderPath = "Assets/VRMShadersPreload";
    private const string PrefabName = "VRMShaderPreloaderPrefab.prefab";

    [MenuItem("Tools/VRM/Generate Preload Dummy Materials")]
    public static void GenerateMaterialsAndPrefab()
    {
        // 出力先フォルダの確保
        if (!AssetDatabase.IsValidFolder(ExportFolderPath))
        {
            AssetDatabase.CreateFolder("Assets", "VRMShadersPreload");
        }

        List<Material> generatedMaterials = new List<Material>();

        // 1. MToon10 の生成 (URP用パス、見つからなければ標準パスをフォールバック)
        Shader mtoonShader = Shader.Find("VRM10/Universal Render Pipeline/MToon10") ?? Shader.Find("VRM10/MToon10");
        if (mtoonShader != null)
        {
            generatedMaterials.AddRange(GenerateMToon10Variants(mtoonShader));
        }
        else
        {
            Debug.LogError("[VRM Preload] MToon10 shader not found. Ensure VRM 1.0 is installed.");
        }

        // 2. URP Lit の生成
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader != null)
        {
            generatedMaterials.AddRange(GenerateURPLitVariants(litShader));
        }
        else
        {
            Debug.LogError("[VRM Preload] URP Lit shader not found.");
        }

        // 3. UniUnlit の生成
        Shader unlitShader = Shader.Find("UniGLTF/UniUnlit");
        if (unlitShader != null)
        {
            generatedMaterials.AddRange(GenerateUniUnlitVariants(unlitShader));
        }
        else
        {
            Debug.LogError("[VRM Preload] UniUnlit shader not found.");
        }

        // 4. Prefabの生成とセットアップ
        CreatePreloaderPrefab(generatedMaterials.ToArray());

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        Debug.Log($"[VRM Preload] Successfully generated {generatedMaterials.Count} materials and prefab at {ExportFolderPath}");
    }

    private static List<Material> GenerateMToon10Variants(Shader shader)
    {
        List<Material> mats = new List<Material>();
        string[] blendNames = { "Opaque", "Cutout", "Transparent", "TransparentZWrite" };
        string[] outlineNames = { "OutlineNone", "OutlineWorld", "OutlineScreen" };

        for (int blend = 0; blend < 4; blend++)
        {
            for (int outline = 0; outline < 3; outline++)
            {
                Material mat = new Material(shader);
                mat.name = $"MToon10_{blendNames[blend]}_{outlineNames[outline]}";

                // BlendMode Setup
                mat.SetFloat("_BlendMode", blend);
                if (blend == 1) // Cutout
                {
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.renderQueue = 2450;
                }
                else if (blend >= 2) // Transparent
                {
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = 3000;
                    if (blend == 3) mat.SetFloat("_ZWrite", 1); // TransparentWithZWrite
                }

                // Outline Setup
                mat.SetFloat("_OutlineWidthMode", outline);

                SaveMaterialAsset(mat);
                mats.Add(mat);
            }
        }
        return mats;
    }

    private static List<Material> GenerateURPLitVariants(Shader shader)
    {
        List<Material> mats = new List<Material>();
        
        // Opaque
        Material opaque = new Material(shader) { name = "URPLit_Opaque" };
        SaveMaterialAsset(opaque);
        mats.Add(opaque);

        // Cutout
        Material cutout = new Material(shader) { name = "URPLit_Cutout" };
        cutout.SetFloat("_AlphaClip", 1);
        cutout.EnableKeyword("_ALPHATEST_ON");
        cutout.SetOverrideTag("RenderType", "TransparentCutout");
        cutout.renderQueue = 2450;
        SaveMaterialAsset(cutout);
        mats.Add(cutout);

        // Transparent
        Material trans = new Material(shader) { name = "URPLit_Transparent" };
        trans.SetFloat("_Surface", 1); // 1 = Transparent
        trans.SetFloat("_Blend", 0); // 0 = Alpha
        trans.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        trans.SetOverrideTag("RenderType", "Transparent");
        trans.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        trans.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        trans.SetFloat("_ZWrite", 0);
        trans.renderQueue = 3000;
        SaveMaterialAsset(trans);
        mats.Add(trans);

        return mats;
    }

    private static List<Material> GenerateUniUnlitVariants(Shader shader)
    {
        List<Material> mats = new List<Material>();
        string[] modes = { "Opaque", "Cutout", "Transparent" };

        for (int i = 0; i < 3; i++)
        {
            Material mat = new Material(shader);
            mat.name = $"UniUnlit_{modes[i]}";
            mat.SetFloat("_BlendMode", i);
            
            if (i == 1) { mat.SetOverrideTag("RenderType", "TransparentCutout"); mat.renderQueue = 2450; }
            if (i == 2) { mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 3000; }

            SaveMaterialAsset(mat);
            mats.Add(mat);
        }
        return mats;
    }

    private static void SaveMaterialAsset(Material mat)
    {
        string path = Path.Combine(ExportFolderPath, $"{mat.name}.mat");
        AssetDatabase.CreateAsset(mat, path);
    }

    private static void CreatePreloaderPrefab(Material[] materials)
    {
        string prefabPath = Path.Combine(ExportFolderPath, PrefabName);
        
        // 一時的なGameObjectを作成
        GameObject go = new GameObject("VRMShaderPreloader");
        VRMShaderPreloader preloader = go.AddComponent<VRMShaderPreloader>();
        preloader.preloadMaterials = materials;

        // Prefabとして保存し、シーンのGameObjectを破棄
        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
    }
}
#endif