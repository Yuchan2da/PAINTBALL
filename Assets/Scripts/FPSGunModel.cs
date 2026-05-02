using UnityEngine;
using Photon.Pun;

/// <summary>
/// 1인칭 건 모델 비주얼 컨트롤러.
///
/// [역할]
/// - 카메라 하단 우측에 페인트볼 건 모델을 표시 (로컬 플레이어 전용)
/// - 발사 시 뒤로 밀리는 반동(kickback) 연출
/// - 재장전 시 아래로 기울어지는 연출
/// - 팀 색상에 맞춰 MarkerBody 스킨 자동 전환
///
/// [배치 구조]
/// Player / Main Camera / GunModel (이 스크립트)
///   └── Paintball_Maker (중첩 프리팹)
///       ├── Fire_Mouths    (CustomizableGroup)
///       ├── MarkerBody     (CustomizableGroup) ← 팀색 자동 매칭
///       ├── Grips          (CustomizableGroup)
///       ├── Flahs_Lights   (CustomizableGroup, optional)
///       ├── Loaders        (CustomizableGroup)
///       └── Gas_Loaders    (CustomizableGroup)
///
/// [설계 원칙]
/// - 이 스크립트는 순수 비주얼만 담당. 게임 로직(사격/탄약)은 PlayerShooter가 관리.
/// - PlayerShooter에서 Fire/Reload 시 이 스크립트의 메서드를 호출.
/// </summary>
public class FPSGunModel : MonoBehaviour
{
    [Header("반동 설정")]
    [Tooltip("발사 시 뒤로 밀리는 거리 (localPosition.z 기준)")]
    public float recoilKickback = 0.03f;

    [Tooltip("반동 복귀 속도")]
    public float recoilRecovery = 10f;

    [Header("재장전 연출")]
    [Tooltip("재장전 시 아래로 기울어지는 각도 (음수 = 아래)")]
    public float reloadTiltAngle = -30f;

    [Tooltip("기울기 전환 속도")]
    public float reloadTiltSpeed = 5f;

    [Header("파츠 CustomizableGroup (Inspector에서 연결)")]
    [Tooltip("MarkerBody — 팀색 자동 매칭")]
    public CustomizableGroup markerBodyGroup;

    [Tooltip("Fire_Mouths — 총구")]
    public CustomizableGroup muzzleGroup;

    [Tooltip("Grips — 그립")]
    public CustomizableGroup gripGroup;

    [Tooltip("Flahs_Lights — 플래시라이트")]
    public CustomizableGroup flashGroup;

    // ── 내부 상태 ─────────────────────────────────────────────────
    private Vector3 originLocalPos;
    private Quaternion originLocalRot;
    private float currentKickback;
    private bool isReloading;
    private float currentTiltAngle;

    // PlayerShooter의 PlayerColors 인덱스 → MarkerBody 스킨 인덱스 매핑
    // PlayerColors: [0]=빨강, [1]=파랑, [2]=초록, [3]=노랑
    // MarkerBody:   [0]=Skin_1, [1]=Blue, [2]=Green, [3]=Green2, [4]=Navy,
    //               [5]=Orange, [6]=Pink, [7]=Purple, [8]=Red, [9]=White, [10]=Gold
    private static readonly int[] TeamToSkinIndex = { 8, 1, 2, 5 };
    // 빨강→Red(8), 파랑→Blue(1), 초록→Green(2), 노랑→Orange(5)

    void Start()
    {
        // 원격 플레이어에게는 1인칭 건 모델이 불필요 → 즉시 비활성화
        var pv = GetComponentInParent<Photon.Pun.PhotonView>();
        if (pv != null && !pv.IsMine)
        {
            gameObject.SetActive(false);
            return;
        }

        // 원래 위치/회전 저장 (반동/재장전 복귀 기준점)
        originLocalPos = transform.localPosition;
        originLocalRot = transform.localRotation;

        // 데모씬용 컴포넌트 비활성화 (RotateWithMouse, SaveCustomizables, LoadCustomizables)
        DisableDemoComponents();

        // CustomizableGroup의 Update() 비활성화 (E/Q 키 입력 차단)
        DisableGroupUpdates();

        // PlayerPrefs에서 저장된 커스터마이징 적용 (로컬 전용)
        ApplyCustomization();
    }

    // ── 공개 메서드 (PlayerShooter에서 호출) ──────────────────────

    /// <summary>발사 반동 시작: 뒤로 밀기</summary>
    public void PlayRecoil()
    {
        currentKickback = recoilKickback;
    }

    /// <summary>재장전 상태 설정: true면 건이 아래로 기울어짐</summary>
    public void SetReloading(bool reloading)
    {
        isReloading = reloading;
    }

    /// <summary>
    /// 팀 색상에 맞춰 MarkerBody 스킨을 전환한다.
    /// teamIndex: PlayerShooter.PlayerColors 배열 인덱스 (0~3)
    /// </summary>
    public void SetTeamSkin(int teamIndex)
    {
        if (markerBodyGroup == null) return;

        int skinIndex = 0;
        if (teamIndex >= 0 && teamIndex < TeamToSkinIndex.Length)
            skinIndex = TeamToSkinIndex[teamIndex];

        markerBodyGroup.ItemID = skinIndex;
        markerBodyGroup.UpdateVisibility();
    }

    /// <summary>건 모델 표시/숨김 (사망/리스폰)</summary>
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    /// <summary>
    /// PlayerPrefs / Photon Custom Properties에 저장된 커스터마이징을 적용한다.
    /// Photon CP를 우선으로 읽어 같은 PC 멀티 인스턴스에서도 독립 적용.
    /// </summary>
    public void ApplyCustomization()
    {
        // 로컬 플레이어의 Photon Player 참조 확보
        Photon.Realtime.Player owner = null;
        var pv = GetComponentInParent<PhotonView>();
        if (pv != null && pv.Owner != null)
            owner = pv.Owner;

        ApplyGroupFromPlayer(muzzleGroup, GunCustomizeUI.KEY_MUZZLE, 0, owner);
        ApplyGroupFromPlayer(gripGroup,   GunCustomizeUI.KEY_GRIP,   0, owner);

        // 플래시라이트: 기본값 -1 → "없음" (= Childs.Count)
        int flashDefault = (flashGroup != null) ? flashGroup.Childs.Count : 0;
        ApplyGroupFromPlayer(flashGroup, GunCustomizeUI.KEY_FLASH, flashDefault, owner);
    }

    /// <summary>Photon CP 우선, PlayerPrefs 폴백으로 ID를 읽어 CustomizableGroup에 적용.</summary>
    private void ApplyGroupFromPlayer(CustomizableGroup group, string key, int defaultID,
                                       Photon.Realtime.Player owner)
    {
        if (group == null) return;

        // Childs 미초기화 방어
        if (group.Childs.Count == 0)
        {
            for (int i = 0; i < group.transform.childCount; i++)
                group.Childs.Add(group.transform.GetChild(i).gameObject);
        }

        // Photon CP 우선 → PlayerPrefs 폴백
        int savedID = GunCustomizeUI.GetSavedIDForPlayer(owner, key, defaultID);

        // -1 (미저장 플래시) → "없음" 상태
        if (savedID < 0 && group.OppitionalItem)
            savedID = group.Childs.Count;

        int maxID = group.OppitionalItem ? group.Childs.Count : group.Childs.Count - 1;
        group.ItemID = Mathf.Clamp(savedID, 0, maxID);
        group.UpdateVisibility();
    }

    // ── 애니메이션 업데이트 ───────────────────────────────────────

    void LateUpdate()
    {
        // 반동: 발사 후 뒤로 밀렸다가 부드럽게 복귀
        currentKickback = Mathf.Lerp(currentKickback, 0f, Time.deltaTime * recoilRecovery);
        Vector3 kickOffset = Vector3.back * currentKickback;

        // 재장전 기울기: 부드럽게 전환
        float targetTilt = isReloading ? reloadTiltAngle : 0f;
        currentTiltAngle = Mathf.Lerp(currentTiltAngle, targetTilt, Time.deltaTime * reloadTiltSpeed);

        // 적용
        transform.localPosition = originLocalPos + kickOffset;
        transform.localRotation = originLocalRot * Quaternion.Euler(currentTiltAngle, 0f, 0f);
    }

    // ── 내부 유틸 ────────────────────────────────────────────────

    /// <summary>
    /// 데모씬 전용 컴포넌트 비활성화.
    /// Paintball_Maker 프리팹에 붙어있는 RotateWithMouse, Save/Load를 끈다.
    /// </summary>
    void DisableDemoComponents()
    {
        var rotMouse = GetComponentInChildren<RotateWithMouse>(true);
        if (rotMouse != null) rotMouse.enabled = false;

        var save = GetComponentInChildren<SaveCustomizables>(true);
        if (save != null) save.enabled = false;

        var load = GetComponentInChildren<LoadCustomizables>(true);
        if (load != null) load.enabled = false;
    }

    /// <summary>
    /// 모든 CustomizableGroup의 Update() 실행을 차단.
    /// E/Q 키로 게임 중 파츠가 바뀌는 것을 방지한다.
    /// </summary>
    void DisableGroupUpdates()
    {
        var groups = GetComponentsInChildren<CustomizableGroup>(true);
        foreach (var group in groups)
            group.enabled = false;
    }
}
