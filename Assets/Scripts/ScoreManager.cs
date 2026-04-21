using UnityEngine;
using System;
using System.Collections.Generic;
using Photon.Pun;

/// <summary>
/// 킬/데스 점수 관리 싱글톤.
///
/// [설계 원칙]
/// - 로컬 우선 구현. Photon 적용 시 데이터를 [Networked]로 교체하면 됨.
/// - 킬 발생 시 OnKillEvent를 발행하여 GameHUD의 킬 피드가 구독.
/// - 순위 정렬은 킬 수 기준 내림차순. 동점이면 데스 적은 순.
/// </summary>
public class ScoreManager : MonoBehaviourPun
{
    public static ScoreManager Instance { get; private set; }

    // ── 이벤트 ──────────────────────────────────────────────────────
    /// <summary>
    /// 킬 발생 시 발행. (킬러 이름, 피해자 이름, 헤드샷 여부)
    /// </summary>
    public event Action<string, string, bool> OnKillEvent;

    // ── 플레이어 데이터 ─────────────────────────────────────────────
    [Serializable]
    public class PlayerScore
    {
        public string playerName;
        public int kills;
        public int deaths;

        public PlayerScore(string name)
        {
            playerName = name;
            kills = 0;
            deaths = 0;
        }
    }

    // 등록된 전체 플레이어 점수 목록
    private Dictionary<string, PlayerScore> scoreMap = new Dictionary<string, PlayerScore>();

    // 정렬된 순위 리스트 (킬 피드/점수판에서 사용)
    private List<PlayerScore> sortedRanking = new List<PlayerScore>();
    private bool rankingDirty = true;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ── 플레이어 등록 ────────────────────────────────────────────────

    /// <summary>
    /// 게임 시작 시 각 플레이어를 등록한다.
    /// 이미 등록된 이름이면 무시.
    /// </summary>
    public void RegisterPlayer(string playerName)
    {
        if (scoreMap.ContainsKey(playerName)) return;

        scoreMap[playerName] = new PlayerScore(playerName);
        rankingDirty = true;

        Debug.Log($"[ScoreManager] 플레이어 등록: {playerName}");
    }

    // ── 킬/데스 기록 ─────────────────────────────────────────────────

    /// <summary>
    /// 킬 발생 시 호출. 킬러와 피해자의 점수를 동시에 갱신한다.
    /// </summary>
    public void RecordKill(string killerName, string victimName, bool isHeadshot = false)
    {
        // 킬러 점수 증가
        if (scoreMap.TryGetValue(killerName, out var killerScore))
            killerScore.kills++;

        // 피해자 데스 증가
        if (scoreMap.TryGetValue(victimName, out var victimScore))
            victimScore.deaths++;

        rankingDirty = true;

        Debug.Log($"[ScoreManager] 킬! {killerName} → {victimName} (헤드샷: {isHeadshot})");

        // 킬 이벤트 발행 → GameHUD 킬 피드가 구독
        OnKillEvent?.Invoke(killerName, victimName, isHeadshot);
    }

    // ── 네트워크 킬 기록 ─────────────────────────────────────────

    /// <summary>
    /// 네트워크 환경에서의 킬 기록.
    /// RPC로 전 클라이언트에 브로드캐스트하여 모든 화면에 킬피드 표시.
    /// </summary>
    public void RecordKillNetwork(string killerName, string victimName, bool isHeadshot)
    {
        if (photonView != null && PhotonNetwork.IsConnected)
            photonView.RPC(nameof(RPC_RecordKill), RpcTarget.All, killerName, victimName, isHeadshot);
        else
            RecordKill(killerName, victimName, isHeadshot);
    }

    [PunRPC]
    void RPC_RecordKill(string killerName, string victimName, bool isHeadshot)
    {
        // 미등록 플레이어 자동 등록 (뒤늦게 입장한 클라이언트 대응)
        if (!scoreMap.ContainsKey(killerName)) RegisterPlayer(killerName);
        if (!scoreMap.ContainsKey(victimName)) RegisterPlayer(victimName);

        RecordKill(killerName, victimName, isHeadshot);
    }

    // ── 순위 조회 ────────────────────────────────────────────────────

    /// <summary>
    /// 킬 수 기준 내림차순 정렬된 순위 리스트를 반환한다.
    /// 동점이면 데스가 적은 플레이어가 위.
    /// </summary>
    public List<PlayerScore> GetRanking()
    {
        if (rankingDirty)
        {
            sortedRanking.Clear();
            sortedRanking.AddRange(scoreMap.Values);
            sortedRanking.Sort((a, b) =>
            {
                int killCompare = b.kills.CompareTo(a.kills);
                if (killCompare != 0) return killCompare;
                return a.deaths.CompareTo(b.deaths); // 데스 적은 순
            });
            rankingDirty = false;
        }
        return sortedRanking;
    }

    /// <summary>
    /// 특정 플레이어의 점수를 조회한다. 없으면 null.
    /// </summary>
    public PlayerScore GetScore(string playerName)
    {
        scoreMap.TryGetValue(playerName, out var score);
        return score;
    }

    /// <summary>
    /// 모든 점수를 초기화한다. (새 라운드 시작 시)
    /// </summary>
    public void ResetAllScores()
    {
        foreach (var score in scoreMap.Values)
        {
            score.kills = 0;
            score.deaths = 0;
        }
        rankingDirty = true;
        Debug.Log("[ScoreManager] 전체 점수 리셋");
    }
}
