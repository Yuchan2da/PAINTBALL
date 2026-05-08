using UnityEngine;

/// <summary>
/// 크로스헤어 설정 데이터 클래스.
///
/// [역할]
/// - 크로스헤어의 모든 커스텀 설정값을 보관
/// - JSON 직렬화로 PlayerPrefs에 저장/로드
/// - CrosshairRenderer와 CrosshairSettingsUI에서 공유
///
/// [설계 원칙]
/// - MonoBehaviour 상속 없음 (순수 데이터)
/// - 기본값이 발로란트 기본 크로스헤어와 유사하게 설정
/// </summary>
[System.Serializable]
public class CrosshairSettings
{
    // ── 색상 ──────────────────────────────────────────────────────
    public float colorR = 0f;
    public float colorG = 1f;
    public float colorB = 0f;
    public float colorA = 1f;

    // ── 선 ────────────────────────────────────────────────────────
    public float lineLength   = 6f;   // 선 길이 (px)  범위: 1~20
    public float lineThickness = 2f;  // 선 두께 (px)  범위: 1~6
    public float gap           = 4f;  // 중심 간격 (px) 범위: 0~20

    // ── 중심 점 ──────────────────────────────────────────────────
    public bool  showDot  = true;
    public float dotSize  = 2f;       // 범위: 1~6

    // ── 외곽선 ───────────────────────────────────────────────────
    public bool  showOutline      = true;
    public float outlineThickness = 1f;  // 범위: 1~3

    // ── 표시 옵션 ─────────────────────────────────────────────────
    public bool showCrosshair = true;  // 크로스헤어 전체 표시
    public bool showLines     = true;  // 십자선(4개 선) 표시 (점/외곽선은 유지)

    // ── T자 (윗선 제거) ──────────────────────────────────────────
    public bool tShape = false;

    // ── 동적 확장 ────────────────────────────────────────────────
    public bool  dynamicOnFire = true;   // 사격 시 벌어짐
    public bool  dynamicOnMove = true;   // 이동 시 벌어짐
    public float dynamicAmount = 6f;     // 확장 크기 (px) 범위: 1~15

    // ── 유틸리티 ─────────────────────────────────────────────────

    /// <summary>설정된 RGBA 값을 Color로 변환.</summary>
    public Color GetColor() => new Color(colorR, colorG, colorB, colorA);

    /// <summary>Color를 개별 RGBA 필드로 분해 저장.</summary>
    public void SetColor(Color c)
    {
        colorR = c.r;
        colorG = c.g;
        colorB = c.b;
        colorA = c.a;
    }

    // ── PlayerPrefs 저장/로드 ────────────────────────────────────

    private const string PREFS_KEY = "crosshair_settings";

    /// <summary>현재 설정을 JSON으로 직렬화하여 PlayerPrefs에 저장.</summary>
    public void Save()
    {
        string json = JsonUtility.ToJson(this);
        PlayerPrefs.SetString(PREFS_KEY, json);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// PlayerPrefs에서 설정을 불러온다.
    /// 저장된 값이 없으면 기본값 인스턴스를 반환.
    /// </summary>
    public static CrosshairSettings Load()
    {
        string json = PlayerPrefs.GetString(PREFS_KEY, "");
        if (string.IsNullOrEmpty(json))
            return new CrosshairSettings();
        return JsonUtility.FromJson<CrosshairSettings>(json);
    }
}
