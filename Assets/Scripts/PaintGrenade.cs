using UnityEngine;
using Photon.Pun;

/// <summary>
/// 페인트 수류탄 투척체.
/// PhotonNetwork.Instantiate로 생성되며, InstantiationData로 팀 색상을 전달받는다.
///
/// [생명주기]
/// 1. PlayerShooter.ThrowGrenade()에서 Instantiate + AddForce
/// 2. 포물선 비행 (Rigidbody 물리, CCD로 벽 관통 방지)
/// 3. fuseTime(2초) 후 기폭 → RPC로 모든 클라이언트에 폭발 전파
/// 4. 폭발 지점에 PaintZone 생성 → 자신은 파괴
///
/// [네트워크]
/// - Owner만 타이머/기폭 판정
/// - RPC_Explode로 모든 클라이언트에서 PaintZone 생성
/// </summary>
public class PaintGrenade : MonoBehaviourPun
{
    // ─── 설정 ─────────────────────────────────────────────────────
    [Header("수류탄 설정")]
    [SerializeField] float fuseTime = 2f;

    // ─── 상태 ─────────────────────────────────────────────────────
    Color teamColor = Color.red;
    float timer;
    bool hasExploded;
    int ownerViewID;
    Rigidbody rb;

    // ─── 초기화 ───────────────────────────────────────────────────

    void Start()
    {
        // Photon InstantiationData에서 팀 색상 + ownerViewID 추출
        object[] data = photonView.InstantiationData;
        if (data != null && data.Length >= 5)
        {
            teamColor = new Color((float)data[0], (float)data[1],
                                  (float)data[2], (float)data[3]);
            ownerViewID = (int)data[4];
        }

        // Rigidbody 물리 설정: CCD로 벽 관통 방지 + 적절한 드래그
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearDamping = 0.5f;         // 공기저항 → 너무 멀리 안 날아감
            rb.angularDamping = 1f;          // 회전 감쇠
        }

        // 바운스 최소화 (벽에 과도하게 튀지 않게)
        var col = GetComponent<Collider>();
        if (col != null)
        {
            var physMat = new PhysicsMaterial("GrenadeMat");
            physMat.bounciness = 0.15f;
            physMat.dynamicFriction = 0.6f;
            physMat.staticFriction = 0.6f;
            physMat.bounceCombine = PhysicsMaterialCombine.Minimum;
            physMat.frictionCombine = PhysicsMaterialCombine.Average;
            col.material = physMat;
        }
    }

    void Update()
    {
        // Owner만 타이머 관리
        if (!photonView.IsMine) return;

        timer += Time.deltaTime;
        if (timer >= fuseTime && !hasExploded)
            Explode();
    }

    // ─── 충돌 시 속도 대폭 감소 (바운스 느낌) ────────────────────

    void OnCollisionEnter(Collision collision)
    {
        // 벽/바닥에 부딪히면 속도를 30%로 줄여 자연스럽게 굴러가게
        if (rb != null)
        {
            rb.linearVelocity *= 0.3f;
            rb.angularVelocity *= 0.5f;
        }
    }

    // ─── 기폭 ─────────────────────────────────────────────────────

    void Explode()
    {
        hasExploded = true;

        Vector3 pos = transform.position;

        // 모든 클라이언트에서 PaintZone 생성
        photonView.RPC(nameof(RPC_Explode), RpcTarget.All,
            pos, teamColor.r, teamColor.g, teamColor.b, ownerViewID);

        // 수류탄 오브젝트 파괴
        PhotonNetwork.Destroy(gameObject);
    }

    [PunRPC]
    void RPC_Explode(Vector3 pos, float r, float g, float b, int viewID)
    {
        Color color = new Color(r, g, b, 1f);
        PaintZone.Spawn(pos, color, viewID);
    }
}
