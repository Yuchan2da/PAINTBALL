using UnityEngine;
using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// 게임 설정 데이터. 호스트가 로비에서 설정하고 Photon Room CustomProperties로 동기화된다.
///
/// [사용법]
/// - 로비: RoomSettingsUI에서 값 변경 → SaveToRoom()
/// - 게임: GameManager.Awake()에서 LoadFromRoom() → GameSettings.Current 사용
///
/// [OfflineMode]
/// 연습 모드에서는 Room Properties가 없으므로 기본값 그대로 사용.
/// </summary>
[System.Serializable]
public class GameSettings
{
    // ─── 싱글톤 ───────────────────────────────────────────────────
    public static GameSettings Current { get; private set; } = new GameSettings();

    // ─── 설정 값 ──────────────────────────────────────────────────
    [Header("라운드")]
    public float roundDuration = 180f;

    [Header("체력")]
    public int maxHealth = 100;

    [Header("무기")]
    public int magazineSize = 15;
    public int headshotDamage = 20;
    public int bodyshotDamage = 10;

    [Header("정지 패널티")]
    public float idlePenaltyDelay = 5f;
    public int idlePenaltyDamage = 5;

    [Header("수류탄")]
    public int grenadeCount = 1;
    public float grenadeDPS = 10f;

    // ─── Photon 직렬화 키 ─────────────────────────────────────────
    const string PREFIX = "gs_";

    // 각 설정의 키를 상수로 관리하여 오타 방지
    static readonly string KEY_ROUND_DUR   = PREFIX + "roundDur";
    static readonly string KEY_MAX_HP      = PREFIX + "maxHp";
    static readonly string KEY_MAG_SIZE    = PREFIX + "magSize";
    static readonly string KEY_HEAD_DMG    = PREFIX + "headDmg";
    static readonly string KEY_BODY_DMG    = PREFIX + "bodyDmg";
    static readonly string KEY_IDLE_TIME   = PREFIX + "idleTime";
    static readonly string KEY_IDLE_DMG    = PREFIX + "idleDmg";
    static readonly string KEY_GREN_CNT    = PREFIX + "grenCnt";
    static readonly string KEY_GREN_DPS    = PREFIX + "grenDPS";

    // ─── 저장 (호스트 → Room Properties) ─────────────────────────

    /// <summary>
    /// 현재 설정을 Photon Room CustomProperties에 저장한다.
    /// 호스트(MasterClient)만 호출해야 한다.
    /// </summary>
    public void SaveToRoom()
    {
        if (!PhotonNetwork.InRoom) return;

        var props = new Hashtable
        {
            { KEY_ROUND_DUR, roundDuration },
            { KEY_MAX_HP,    maxHealth },
            { KEY_MAG_SIZE,  magazineSize },
            { KEY_HEAD_DMG,  headshotDamage },
            { KEY_BODY_DMG,  bodyshotDamage },
            { KEY_IDLE_TIME, idlePenaltyDelay },
            { KEY_IDLE_DMG,  idlePenaltyDamage },
            { KEY_GREN_CNT,  grenadeCount },
            { KEY_GREN_DPS,  grenadeDPS },
        };

        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    // ─── 로드 (Room Properties → 로컬) ──────────────────────────

    /// <summary>
    /// Room CustomProperties에서 설정을 읽어 Current를 갱신한다.
    /// 게임 씬 진입 시 모든 클라이언트가 호출.
    /// </summary>
    public static GameSettings LoadFromRoom()
    {
        var s = new GameSettings();

        if (!PhotonNetwork.InRoom)
        {
            Current = s;
            return s;
        }

        var props = PhotonNetwork.CurrentRoom.CustomProperties;

        if (props.TryGetValue(KEY_ROUND_DUR, out var v1)) s.roundDuration     = (float)v1;
        if (props.TryGetValue(KEY_MAX_HP, out var v2))    s.maxHealth         = (int)v2;
        if (props.TryGetValue(KEY_MAG_SIZE, out var v3))  s.magazineSize      = (int)v3;
        if (props.TryGetValue(KEY_HEAD_DMG, out var v4))  s.headshotDamage    = (int)v4;
        if (props.TryGetValue(KEY_BODY_DMG, out var v5))  s.bodyshotDamage    = (int)v5;
        if (props.TryGetValue(KEY_IDLE_TIME, out var v6)) s.idlePenaltyDelay  = (float)v6;
        if (props.TryGetValue(KEY_IDLE_DMG, out var v7))  s.idlePenaltyDamage = (int)v7;
        if (props.TryGetValue(KEY_GREN_CNT, out var v8))  s.grenadeCount      = (int)v8;
        if (props.TryGetValue(KEY_GREN_DPS, out var v9))  s.grenadeDPS        = (float)v9;

        Current = s;
        return s;
    }

    // ─── 기본값 리셋 ─────────────────────────────────────────────

    /// <summary>
    /// 모든 설정을 기본값으로 초기화한다.
    /// </summary>
    public void ResetToDefaults()
    {
        var defaults = new GameSettings();
        roundDuration     = defaults.roundDuration;
        maxHealth         = defaults.maxHealth;
        magazineSize      = defaults.magazineSize;
        headshotDamage    = defaults.headshotDamage;
        bodyshotDamage    = defaults.bodyshotDamage;
        idlePenaltyDelay  = defaults.idlePenaltyDelay;
        idlePenaltyDamage = defaults.idlePenaltyDamage;
        grenadeCount      = defaults.grenadeCount;
        grenadeDPS        = defaults.grenadeDPS;
    }
}
