using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

/// <summary>
/// 로비 대기실에서 총기 부착물(총구, 그립, 플래시라이트)을 편집하는 UI 컨트롤러.
///
/// [역할]
/// - Room Panel 위에 커스터마이징 패널을 오버레이
/// - 각 파츠를 ◀ N/Total ▶ 스위처로 순환 선택
/// - 선택 값을 PlayerPrefs에 저장 → 게임 씬에서 FPSGunModel이 읽어 적용
/// - 3D 프리뷰 모델의 CustomizableGroup을 직접 제어하여 실시간 미리보기
///
/// [배치 구조 (로비 씬)]
/// Canvas
///   └── CustomizePanel (이 스크립트)
///       ├── RawImage (RenderTexture로 프리뷰 표시)
///       ├── MuzzleSwitcher (◀ 라벨 ▶)
///       ├── GripSwitcher   (◀ 라벨 ▶)
///       ├── FlashSwitcher  (◀ 라벨 ▶)
///       └── CloseButton
///
/// [설계 원칙]
/// - 이 스크립트는 로비 UI 전용. 게임 씬에서는 사용하지 않음.
/// - PlayerPrefs 키 상수를 static으로 공개하여 FPSGunModel에서 동일 키로 읽기 가능.
/// - CustomizableGroup 초기화 타이밍 문제를 EnsureInitialized()로 방어.
/// </summary>
public class GunCustomizeUI : MonoBehaviour
{
    // ───── PlayerPrefs 키 (FPSGunModel에서도 사용) ─────────────────
    public const string KEY_MUZZLE = "gun_muzzle";
    public const string KEY_GRIP   = "gun_grip";
    public const string KEY_FLASH  = "gun_flash";

    // ───── 패널 ───────────────────────────────────────────────────
    [Header("패널")]
    [SerializeField] private GameObject customizePanel;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;

    // ───── 프리뷰 건 모델의 CustomizableGroup 참조 ──────────────────
    [Header("프리뷰 건 — CustomizableGroup")]
    [Tooltip("Fire_Mouths (총구 20종)")]
    [SerializeField] private CustomizableGroup muzzleGroup;

    [Tooltip("Grips (그립 18종)")]
    [SerializeField] private CustomizableGroup gripGroup;

    [Tooltip("Flahs_Lights (플래시라이트 9종 + 없음)")]
    [SerializeField] private CustomizableGroup flashGroup;

    // ───── 프리뷰 루트 (패널 열 때만 활성화) ─────────────────────────
    [Header("프리뷰")]
    [Tooltip("프리뷰 건 모델 루트 오브젝트 (Camera + Paintball_Maker)")]
    [SerializeField] private GameObject previewRoot;

    // ───── 총구 스위처 UI ──────────────────────────────────────────
    [Header("총구 스위처")]
    [SerializeField] private Button muzzlePrevBtn;
    [SerializeField] private Button muzzleNextBtn;
    [SerializeField] private TMP_Text muzzleLabel;

    // ───── 그립 스위처 UI ──────────────────────────────────────────
    [Header("그립 스위처")]
    [SerializeField] private Button gripPrevBtn;
    [SerializeField] private Button gripNextBtn;
    [SerializeField] private TMP_Text gripLabel;

    // ───── 플래시라이트 스위처 UI ──────────────────────────────────
    [Header("플래시라이트 스위처")]
    [SerializeField] private Button flashPrevBtn;
    [SerializeField] private Button flashNextBtn;
    [SerializeField] private TMP_Text flashLabel;

    // ===================================================================
    //  초기화
    // ===================================================================

    void Start()
    {
        InitializeGroups();
        LoadSavedSelection();
        BindButtons();
        RefreshAllLabels();
        SetPanelVisible(false);
    }

    /// <summary>버튼 이벤트 일괄 바인딩.</summary>
    private void BindButtons()
    {
        Bind(openButton,  Open);
        Bind(closeButton, Close);

        Bind(muzzlePrevBtn, () => CyclePart(muzzleGroup, -1, muzzleLabel));
        Bind(muzzleNextBtn, () => CyclePart(muzzleGroup,  1, muzzleLabel));

        Bind(gripPrevBtn,   () => CyclePart(gripGroup,   -1, gripLabel));
        Bind(gripNextBtn,   () => CyclePart(gripGroup,    1, gripLabel));

        Bind(flashPrevBtn,  () => CyclePart(flashGroup,  -1, flashLabel));
        Bind(flashNextBtn,  () => CyclePart(flashGroup,   1, flashLabel));
    }

    // ===================================================================
    //  패널 열기 / 닫기
    // ===================================================================

    /// <summary>커스터마이징 패널 열기.</summary>
    public void Open()
    {
        DisablePreviewDemoComponents();
        SetPanelVisible(true);
    }

    /// <summary>커스터마이징 패널 닫기 + 저장.</summary>
    public void Close()
    {
        SaveSelection();
        SetPanelVisible(false);
    }

    private void SetPanelVisible(bool visible)
    {
        if (customizePanel != null)
            customizePanel.SetActive(visible);

        if (previewRoot != null)
            previewRoot.SetActive(visible);
    }

    // ===================================================================
    //  파츠 순환 (◀ ▶ 버튼)
    // ===================================================================

    /// <summary>
    /// CustomizableGroup의 아이템을 direction 방향으로 순환하고 라벨을 갱신한다.
    /// </summary>
    /// <param name="group">대상 파츠 그룹.</param>
    /// <param name="direction">+1 = 다음, -1 = 이전.</param>
    /// <param name="label">현재 선택을 표시할 텍스트.</param>
    private void CyclePart(CustomizableGroup group, int direction, TMP_Text label)
    {
        if (group == null) return;

        if (direction > 0) group.NextItem();
        else               group.PreviousItem();

        RefreshLabel(group, label);
    }

    // ===================================================================
    //  라벨 갱신
    // ===================================================================

    /// <summary>모든 파츠 라벨을 현재 상태로 갱신.</summary>
    private void RefreshAllLabels()
    {
        RefreshLabel(muzzleGroup, muzzleLabel);
        RefreshLabel(gripGroup,   gripLabel);
        RefreshLabel(flashGroup,  flashLabel);
    }

    /// <summary>
    /// 개별 파츠 라벨 갱신.
    /// OppitionalItem이면 "없음" 상태를 별도 표시.
    /// </summary>
    private void RefreshLabel(CustomizableGroup group, TMP_Text label)
    {
        if (label == null || group == null) return;

        bool isNone = group.OppitionalItem && group.ItemID >= group.Childs.Count;
        if (isNone)
        {
            int total = group.Childs.Count + 1;
            label.text = $"없음 ({total}/{total})";
            return;
        }

        int displayTotal = group.OppitionalItem
            ? group.Childs.Count + 1
            : group.Childs.Count;

        label.text = $"{group.ItemID + 1} / {displayTotal}";
    }

    // ===================================================================
    //  저장 / 불러오기 (PlayerPrefs)
    // ===================================================================

    /// <summary>현재 선택 값을 PlayerPrefs + Photon Custom Properties에 저장.</summary>
    private void SaveSelection()
    {
        SaveGroupID(KEY_MUZZLE, muzzleGroup);
        SaveGroupID(KEY_GRIP,   gripGroup);
        SaveGroupID(KEY_FLASH,  flashGroup);
        PlayerPrefs.Save();

        // Photon Custom Properties에도 저장 (같은 PC 멀티 인스턴스 테스트 대응)
        if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
        {
            var props = new Hashtable
            {
                { KEY_MUZZLE, muzzleGroup != null ? muzzleGroup.ItemID : 0 },
                { KEY_GRIP,   gripGroup   != null ? gripGroup.ItemID   : 0 },
                { KEY_FLASH,  flashGroup  != null ? flashGroup.ItemID  : -1 }
            };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }
    }

    /// <summary>PlayerPrefs에서 저장된 선택 값을 읽어 프리뷰에 적용.</summary>
    private void LoadSavedSelection()
    {
        LoadGroupID(KEY_MUZZLE, muzzleGroup, defaultID: 0);
        LoadGroupID(KEY_GRIP,   gripGroup,   defaultID: 0);

        // 플래시라이트 기본값: "없음" = Childs.Count
        int flashDefault = (flashGroup != null) ? flashGroup.Childs.Count : 0;
        LoadGroupID(KEY_FLASH, flashGroup, flashDefault);
    }

    private void SaveGroupID(string key, CustomizableGroup group)
    {
        if (group != null)
            PlayerPrefs.SetInt(key, group.ItemID);
    }

    private void LoadGroupID(string key, CustomizableGroup group, int defaultID)
    {
        if (group == null) return;

        int savedID = PlayerPrefs.GetInt(key, defaultID);
        int maxID = group.OppitionalItem ? group.Childs.Count : group.Childs.Count - 1;
        group.ItemID = Mathf.Clamp(savedID, 0, maxID);
        group.UpdateVisibility();
    }

    // ===================================================================
    //  정적 헬퍼 — FPSGunModel에서 저장값 읽기용
    // ===================================================================

    /// <summary>저장된 총구 ID. Photon CP 우선, PlayerPrefs 폴백.</summary>
    public static int SavedMuzzleID => GetSavedID(KEY_MUZZLE, 0);

    /// <summary>저장된 그립 ID. Photon CP 우선, PlayerPrefs 폴백.</summary>
    public static int SavedGripID => GetSavedID(KEY_GRIP, 0);

    /// <summary>저장된 플래시라이트 ID. Photon CP 우선, PlayerPrefs 폴백.</summary>
    public static int SavedFlashID => GetSavedID(KEY_FLASH, -1);

    /// <summary>
    /// 특정 플레이어의 Photon Custom Properties에서 커스터마이징 값을 읽는다.
    /// FPSGunModel에서 로컬 플레이어의 값을 읽을 때 사용.
    /// </summary>
    public static int GetSavedIDForPlayer(Player player, string key, int fallback)
    {
        if (player != null && player.CustomProperties.ContainsKey(key))
            return (int)player.CustomProperties[key];
        return PlayerPrefs.GetInt(key, fallback);
    }

    /// <summary>Photon LocalPlayer CP 우선, PlayerPrefs 폴백.</summary>
    private static int GetSavedID(string key, int fallback)
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
            return GetSavedIDForPlayer(PhotonNetwork.LocalPlayer, key, fallback);
        return PlayerPrefs.GetInt(key, fallback);
    }

    // ===================================================================
    //  내부 유틸
    // ===================================================================

    /// <summary>
    /// CustomizableGroup의 Childs 리스트가 비어 있으면 자식을 수집한다.
    /// Start() 실행 순서 차이로 Childs가 초기화되지 않은 케이스를 방어.
    /// </summary>
    private void InitializeGroups()
    {
        EnsureGroupReady(muzzleGroup);
        EnsureGroupReady(gripGroup);
        EnsureGroupReady(flashGroup);
    }

    private void EnsureGroupReady(CustomizableGroup group)
    {
        if (group == null || group.Childs.Count > 0) return;

        for (int i = 0; i < group.transform.childCount; i++)
            group.Childs.Add(group.transform.GetChild(i).gameObject);

        // OppitionalItem 기본값: "없음" 상태
        if (group.OppitionalItem)
            group.ItemID = group.Childs.Count;

        group.UpdateVisibility();
    }

    /// <summary>
    /// 프리뷰 건의 데모용 컴포넌트를 비활성화.
    /// RotateWithMouse, Save/LoadCustomizables, CustomizableGroup.Update() 차단.
    /// </summary>
    private void DisablePreviewDemoComponents()
    {
        if (previewRoot == null) return;

        var rotMouse = previewRoot.GetComponentInChildren<RotateWithMouse>(true);
        if (rotMouse != null) rotMouse.enabled = false;

        var save = previewRoot.GetComponentInChildren<SaveCustomizables>(true);
        if (save != null) save.enabled = false;

        var load = previewRoot.GetComponentInChildren<LoadCustomizables>(true);
        if (load != null) load.enabled = false;

        // CustomizableGroup의 Update() 차단 (E/Q 키 입력 방지)
        // — 단, 우리가 참조하는 그룹은 ItemID/UpdateVisibility만 직접 호출하므로 enabled=false OK
        var groups = previewRoot.GetComponentsInChildren<CustomizableGroup>(true);
        for (int i = 0; i < groups.Length; i++)
            groups[i].enabled = false;
    }

    /// <summary>null-safe 버튼 리스너 바인딩.</summary>
    private void Bind(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (btn != null) btn.onClick.AddListener(action);
    }
}
