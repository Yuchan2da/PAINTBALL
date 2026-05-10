using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// DecalProjector용 팀 컬러 머티리얼 매핑.
/// 런타임 텍스처 생성 대신, 에디터에서 사전 생성된 팀별 머티리얼을 사용.
///
/// [사전 요구사항]
/// Assets/Materials/ 폴더에 다음 머티리얼이 존재해야 함:
///   PaintDecal_Team0_Red.mat  (빨간 팀)
///   PaintDecal_Team1_Blue.mat (파란 팀)
///
/// 새 팀 추가 시 에디터에서 머티리얼을 추가 생성하고 colorToMaterial 딕셔너리에 등록.
/// </summary>
public static class DecalTintCache
{
    private static Dictionary<Color, Material> cache = new Dictionary<Color, Material>();
    private static bool initialized = false;

    /// <summary>
    /// 팀 컬러에 대응하는 사전 생성 머티리얼을 반환한다.
    /// 런타임에 텍스처를 생성하지 않고, 에디터에서 만든 에셋을 사용.
    /// </summary>
    public static Material GetTintedMaterial(Color teamColor, DecalProjector sourceProjector)
    {
        if (cache.TryGetValue(teamColor, out Material cached))
            return cached;

        if (!initialized)
        {
            Initialize();
            initialized = true;

            // 초기화 후 다시 캐시 체크
            if (cache.TryGetValue(teamColor, out cached))
                return cached;
        }

        // 정확한 색상 매칭 실패 → 가장 가까운 색상 찾기
        Material closest = FindClosest(teamColor);
        if (closest != null)
        {
            cache[teamColor] = closest;
            return closest;
        }

        // 폴백: 런타임 생성 (기존 방식)
        return CreateRuntimeTinted(teamColor, sourceProjector);
    }

    static void Initialize()
    {
        // 모든 색상을 동일한 베이스 텍스처 기반 런타임 생성으로 통일.
        // 사전 생성 머티리얼은 텍스처/모양이 달라 일관성을 해치므로 비활성화.
        // RegisterColor(new Color(1, 0, 0, 1), "PaintDecal_Team0_Red");
        // RegisterColor(new Color(0, 0, 1, 1), "PaintDecal_Team1_Blue");
    }

    static void RegisterColor(Color color, string materialName)
    {
        // Resources 폴더에서 로드 시도
        Material mat = Resources.Load<Material>(materialName);
        
        if (mat == null)
        {
            // 직접 경로로 시도 (에디터 전용)
            #if UNITY_EDITOR
            mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                $"Assets/Materials/{materialName}.mat");
            #endif
        }

        if (mat != null)
        {
            cache[color] = mat;

        }
        else
        {
            Debug.LogWarning($"[DecalTintCache] Material not found: {materialName}");
        }
    }

    static Material FindClosest(Color target)
    {
        Material best = null;
        float bestDist = float.MaxValue;
        foreach (var kvp in cache)
        {
            float dist = ColorDistance(kvp.Key, target);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = kvp.Value;
            }
        }
        // 색상 차이가 0.3 이내면 매칭
        return (bestDist < 0.3f) ? best : null;
    }

    static float ColorDistance(Color a, Color b)
    {
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        return Mathf.Sqrt(dr * dr + dg * dg + db * db);
    }

    /// <summary>
    /// 폴백: 사전 생성 머티리얼이 없으면 런타임에 생성 (기존 방식).
    /// </summary>
    static Material CreateRuntimeTinted(Color teamColor, DecalProjector sourceProjector)
    {
        if (sourceProjector == null || sourceProjector.material == null)
            return null;

        Material baseMat = sourceProjector.material;
        Texture2D baseTex = baseMat.GetTexture("Base_Map") as Texture2D;

        if (baseTex == null || !baseTex.isReadable)
        {
            Debug.LogWarning($"[DecalTintCache] Fallback failed: baseTex null or not readable");
            return null;
        }

        Texture2D tinted = new Texture2D(baseTex.width, baseTex.height, TextureFormat.RGBA32, false);
        tinted.filterMode = baseTex.filterMode;
        tinted.wrapMode = baseTex.wrapMode;

        Color[] px = baseTex.GetPixels();
        for (int i = 0; i < px.Length; i++)
            px[i] = new Color(px[i].r * teamColor.r, px[i].g * teamColor.g, px[i].b * teamColor.b, px[i].a);

        tinted.SetPixels(px);
        tinted.Apply();

        Material mat = new Material(baseMat);
        mat.SetTexture("Base_Map", tinted);
        mat.name = $"DecalTint_{teamColor}";

        cache[teamColor] = mat;
        return mat;
    }
}
