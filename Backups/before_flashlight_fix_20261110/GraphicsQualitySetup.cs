using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 그래픽 품질 단계 설정 (메뉴 Tools/VOID LAST/Optimize/Setup Graphics Quality).
///  - PC(높음): 원래 설정 + GPU Resident Drawer·GPU 가림 판정(보이지 않는 물체를 GPU가 걸러 냄).
///  - Laptop(노트북): RAM 16GB·내장 그래픽 노트북용. 텍스처 절반 크기, 화면 80% 해상도(FSR로 키움),
///    그림자 거리·해상도 축소, 화면 주변 그늘(SSAO) 끔, 수직 동기화(화면 주사율에 맞춤)로 발열 감소.
///  - 두 단계 모두 로딩 중 텍스처·모델 올리기 예산을 늘려 로딩 시간을 줄인다.
/// 게임은 시작할 때 GraphicsQuality가 컴퓨터 사양을 보고 자동으로 고르고, 대기실 버튼으로 바꿀 수 있다.
/// 다시 실행해도 같은 결과가 나오도록 만들었다 (Laptop 에셋은 PC 에셋을 복사해 다시 만든다).
/// </summary>
public static class GraphicsQualitySetup
{
    const string PcAsset = "Assets/Settings/PC_RPAsset.asset";
    const string PcRenderer = "Assets/Settings/PC_Renderer.asset";
    const string LaptopAsset = "Assets/Settings/Laptop_RPAsset.asset";
    const string LaptopRenderer = "Assets/Settings/Laptop_Renderer.asset";

    [MenuItem("Tools/VOID LAST/Optimize/Setup Graphics Quality")]
    public static string Setup()
    {
        // ---- PC (높음): GPU Resident Drawer + GPU 가림 판정. BRG 셰이더 변형을 빌드에 남겨야 동작한다
        var gs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        gs.FindProperty("m_BrgStripping").intValue = 2;   // KeepAll
        gs.ApplyModifiedPropertiesWithoutUndo();
        var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcAsset);
        pc.gpuResidentDrawerMode = GPUResidentDrawerMode.InstancedDrawing;
        pc.gpuResidentDrawerEnableOcclusionCullingInCameras = true;
        EditorUtility.SetDirty(pc);

        // ---- Laptop 렌더러: PC 렌더러 복사 후 SSAO 끄기
        AssetDatabase.DeleteAsset(LaptopRenderer);
        AssetDatabase.CopyAsset(PcRenderer, LaptopRenderer);
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(LaptopRenderer);
        foreach (var feature in renderer.rendererFeatures)
            if (feature != null && feature.GetType().Name.Contains("AmbientOcclusion")) feature.SetActive(false);
        EditorUtility.SetDirty(renderer);

        // ---- Laptop 파이프라인: PC 복사 후 가볍게
        AssetDatabase.DeleteAsset(LaptopAsset);
        AssetDatabase.CopyAsset(PcAsset, LaptopAsset);
        var laptop = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(LaptopAsset);
        var so = new SerializedObject(laptop);
        var list = so.FindProperty("m_RendererDataList");
        list.arraySize = 1;
        list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
        so.FindProperty("m_DefaultRendererIndex").intValue = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        laptop.renderScale = 0.8f;
        laptop.upscalingFilter = UpscalingFilterSelection.FSR;
        laptop.shadowDistance = 25f;
        laptop.shadowCascadeCount = 2;
        laptop.mainLightShadowmapResolution = 1024;
        laptop.additionalLightsShadowmapResolution = 512;
        SetBool(laptop, "m_SoftShadowsSupported", false);
        EditorUtility.SetDirty(laptop);

        // ---- 품질 단계: 0 = PC, 1 = Laptop
        var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        var levels = qs.FindProperty("m_QualitySettings");
        while (levels.arraySize > 1) levels.DeleteArrayElementAtIndex(levels.arraySize - 1);
        var high = levels.GetArrayElementAtIndex(0);
        high.FindPropertyRelative("name").stringValue = "PC";
        UploadBudget(high);
        levels.InsertArrayElementAtIndex(0);   // 0번을 복사해 1번에 넣는다
        var low = levels.GetArrayElementAtIndex(1);
        low.FindPropertyRelative("name").stringValue = "Laptop";
        low.FindPropertyRelative("customRenderPipeline").objectReferenceValue = laptop;
        low.FindPropertyRelative("globalTextureMipmapLimit").intValue = 1;   // 텍스처 절반 크기 (메모리 약 1/4)
        low.FindPropertyRelative("vSyncCount").intValue = 1;
        low.FindPropertyRelative("lodBias").floatValue = 1f;
        low.FindPropertyRelative("anisotropicTextures").intValue = 1;
        low.FindPropertyRelative("particleRaycastBudget").intValue = 64;
        low.FindPropertyRelative("shadowDistance").floatValue = 25f;
        UploadBudget(low);
        qs.FindProperty("m_CurrentQuality").intValue = 0;
        qs.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        string result = "quality levels: " + string.Join(", ", QualitySettings.names) + " | laptop rp=" + LaptopAsset;
        Debug.Log("[그래픽 품질] " + result);
        return result;
    }

    /// <summary>로딩 중 텍스처·모델을 GPU로 올리는 예산 (기본 2ms·16MB → 4ms·64MB): 로딩이 빨라진다</summary>
    static void UploadBudget(SerializedProperty level)
    {
        level.FindPropertyRelative("asyncUploadTimeSlice").intValue = 4;
        level.FindPropertyRelative("asyncUploadBufferSize").intValue = 64;
    }

    static void SetBool(Object target, string property, bool value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(property);
        if (p != null) { p.boolValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        else Debug.LogWarning("[그래픽 품질] 설정 항목을 찾지 못함: " + property);
    }
}
