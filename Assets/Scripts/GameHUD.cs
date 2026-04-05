using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 인게임 HUD 표시 스크립트.
/// 플레이어의 탄창 수와 체력을 화면에 출력한다.
///
/// [설계 원칙]
/// - HUD는 '표시만' 담당. 게임 로직 변경은 PlayerShooter/MonkeyHealth가 한다. (SRP)
/// - 매 프레임 갱신하지 않고 값이 바뀔 때만 갱신하는 방식(isDirty).
///   → Update()에서 매 프레임 string 생성으로 인한 GC 낭비를 방지.
/// - PlayerShooter/MonkeyHealth가 null이면 해당 UI를 조용히 숨김.
///   → 씬 구성 도중 에러 없이 테스트 가능.
/// </summary>
public class GameHUD : MonoBehaviour
{
    [Header("탄창 HUD")]
    [Tooltip("탄창 수를 표시할 TextMeshPro 컴포넌트")]
    public TMP_Text ammoText;

    [Header("체력 HUD")]
    [Tooltip("체력 수치를 표시할 TextMeshPro 컴포넌트")]
    public TMP_Text hpText;
    [Tooltip("체력 슬라이더 (선택). 비워두면 텍스트만 표시")]
    public Slider hpSlider;

    [Header("연결 대상")]
    [Tooltip("플레이어 오브젝트의 PlayerShooter 컴포넌트")]
    public PlayerShooter playerShooter;
    [Tooltip("플레이어 오브젝트의 MonkeyHealth 컴포넌트")]
    public MonkeyHealth playerHealth;

    // isDirty 패턴: 이전 값과 현재 값이 다를 때만 UI 텍스트를 갱신
    private int lastAmmo   = -1;
    private int lastHp     = -1;

    void Start()
    {
        // 컴포넌트가 없으면 해당 UI 오브젝트를 비활성화
        if (playerShooter == null && ammoText != null)
            ammoText.gameObject.SetActive(false);

        if (playerHealth == null && hpText != null)
            hpText.gameObject.SetActive(false);

        // HP 슬라이더 초기 범위 설정
        if (hpSlider != null && playerHealth != null)
        {
            hpSlider.minValue = 0;
            hpSlider.maxValue = playerHealth.maxHp;
            hpSlider.value    = playerHealth.maxHp;
        }

        // 시작 시 즉시 한 번 갱신
        ForceRefresh();
    }

    void Update()
    {
        RefreshAmmo();
        RefreshHp();
    }

    // ── 탄창 갱신 ─────────────────────────────────────────────────

    void RefreshAmmo()
    {
        if (playerShooter == null || ammoText == null) return;

        int current = playerShooter.CurrentAmmo;
        if (current == lastAmmo) return; // 변화 없으면 스킵

        lastAmmo = current;
        ammoText.text = $"{current} / {playerShooter.MaxAmmo}";

        // 탄창이 5발 이하면 빨간색으로 경고
        ammoText.color = current <= 5 ? Color.red : Color.white;
    }

    // ── 체력 갱신 ─────────────────────────────────────────────────

    void RefreshHp()
    {
        if (playerHealth == null) return;

        int current = playerHealth.CurrentHp;
        if (current == lastHp) return; // 변화 없으면 스킵

        lastHp = current;

        if (hpText != null)
        {
            hpText.text = $"HP  {current} / {playerHealth.maxHp}";

            // HP 비율에 따라 색상 변화: 초록 → 노랑 → 빨강
            float ratio = (float)current / playerHealth.maxHp;
            hpText.color = ratio > 0.5f ? Color.green
                         : ratio > 0.25f ? Color.yellow
                         : Color.red;
        }

        if (hpSlider != null)
            hpSlider.value = current;
    }

    // ── 강제 갱신 (Start, 리스폰 등에서 호출용) ──────────────────

    /// <summary>
    /// isDirty 캐시를 초기화해서 다음 Update에서 즉시 UI를 갱신하도록 한다.
    /// </summary>
    public void ForceRefresh()
    {
        lastAmmo = -1;
        lastHp   = -1;
    }
}
