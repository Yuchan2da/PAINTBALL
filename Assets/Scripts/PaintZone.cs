using UnityEngine;
using UnityEngine.Rendering.Universal;
using Photon.Pun;

/// <summary>
/// 페인트 수류탄 폭발 존 (블루존 스타일).
///
/// [동작]
/// 1. 폭발 지점에 반투명 팀색 구체가 천천히 커지며 생성 (expandDuration 동안)
/// 2. 존 안에 있는 적에게 초당 데미지
/// 3. duration 후 자동 소멸 (서서히 줄어들며 사라짐)
/// 4. 바닥에 팀색 페인트 데칼 산포
///
/// [네트워크]
/// - 시각 효과는 모든 클라이언트에서 동일하게 생성 (RPC_Explode에서 호출)
/// - 데미지 판정은 MasterClient만 수행 → TakeDamageNetwork() RPC로 전달
/// - 자폭 없음 (ownerViewID 제외)
/// </summary>
public class PaintZone : MonoBehaviour
{
    // ─── 설정 ─────────────────────────────────────────────────────
    float maxRadius = 4f;
    float duration = 4f;
    float expandDuration = 0.5f;        // 원이 커지는 시간
    float shrinkDuration = 0.5f;        // 소멸 시 줄어드는 시간
    float damagePerSecond = 10f;
    float damageInterval = 0.5f;        // 데미지 적용 간격 (프레임마다 X)

    // ─── 상태 ─────────────────────────────────────────────────────
    Color paintColor;
    int ownerViewID;
    float elapsed;
    float currentRadius;
    float lastDamageTime;

    // ─── 비주얼 ───────────────────────────────────────────────────
    GameObject sphereVisual;
    Material zoneMaterial;
    SphereCollider zoneCollider;

    // ─── 정적 팩토리 ─────────────────────────────────────────────

    /// <summary>
    /// 폭발 지점에 PaintZone을 생성한다. 모든 클라이언트에서 호출됨.
    /// </summary>
    public static PaintZone Spawn(Vector3 position, Color color, int viewID)
    {
        var obj = new GameObject("PaintZone");
        obj.transform.position = position;

        var zone = obj.AddComponent<PaintZone>();
        zone.paintColor = color;
        zone.ownerViewID = viewID;
        zone.Init();

        return zone;
    }

    // ─── 초기화 ───────────────────────────────────────────────────

    void Init()
    {
        // GameSettings에서 수류탄 DPS 적용
        damagePerSecond = GameSettings.Current.grenadeDPS;

        // SphereCollider (Trigger) — 데미지 판정용
        zoneCollider = gameObject.AddComponent<SphereCollider>();
        zoneCollider.isTrigger = true;
        zoneCollider.radius = 0.01f; // 시작은 아주 작게

        // 비주얼: 반투명 구체
        CreateSphereVisual();

        // 바닥 데칼 생성
        SpawnFloorDecals();

        // 자동 소멸
        Destroy(gameObject, duration);
    }

    // ─── 비주얼 ───────────────────────────────────────────────────

    /// <summary>
    /// 반투명 팀색 구체를 생성한다.
    /// 내장 Sphere 메쉬 + 투명 셰이더로 구현.
    /// </summary>
    void CreateSphereVisual()
    {
        sphereVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereVisual.transform.SetParent(transform);
        sphereVisual.transform.localPosition = Vector3.zero;
        sphereVisual.transform.localScale = Vector3.zero; // 시작은 0

        // Collider 제거 (부모에 이미 있음)
        var col = sphereVisual.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // 반투명 머티리얼
        var renderer = sphereVisual.GetComponent<Renderer>();
        zoneMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Color c = paintColor;
        c.a = 0.25f; // 반투명
        zoneMaterial.color = c;

        // 투명 모드 설정 (URP Lit)
        zoneMaterial.SetFloat("_Surface", 1f); // Transparent
        zoneMaterial.SetFloat("_Blend", 0f);   // Alpha
        zoneMaterial.SetFloat("_AlphaClip", 0f);
        zoneMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        zoneMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        zoneMaterial.SetFloat("_ZWrite", 0f);
        zoneMaterial.renderQueue = 3000;
        zoneMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        renderer.material = zoneMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ─── 업데이트 ─────────────────────────────────────────────────

    void Update()
    {
        elapsed += Time.deltaTime;

        // 확장 페이즈: 0 → maxRadius
        if (elapsed < expandDuration)
        {
            float t = elapsed / expandDuration;
            t = t * t * (3f - 2f * t); // SmoothStep
            currentRadius = Mathf.Lerp(0f, maxRadius, t);
        }
        // 유지 페이즈
        else if (elapsed < duration - shrinkDuration)
        {
            currentRadius = maxRadius;
        }
        // 수축 페이즈: maxRadius → 0
        else
        {
            float shrinkElapsed = elapsed - (duration - shrinkDuration);
            float t = shrinkElapsed / shrinkDuration;
            t = Mathf.Clamp01(t);
            currentRadius = Mathf.Lerp(maxRadius, 0f, t);

            // 투명도도 함께 줄이기
            if (zoneMaterial != null)
            {
                Color c = paintColor;
                c.a = Mathf.Lerp(0.25f, 0f, t);
                zoneMaterial.color = c;
            }
        }

        // 비주얼 크기 업데이트
        if (sphereVisual != null)
        {
            float diameter = currentRadius * 2f;
            sphereVisual.transform.localScale = new Vector3(diameter, diameter, diameter);
        }

        // 콜라이더 크기 업데이트
        if (zoneCollider != null)
            zoneCollider.radius = currentRadius;
    }

    // ─── 데미지 판정 ─────────────────────────────────────────────

    void OnTriggerStay(Collider other)
    {
        // MasterClient만 데미지 판정 (네트워크 권한)
        if (PhotonNetwork.IsConnected && !PhotonNetwork.IsMasterClient) return;

        // 데미지 간격 체크 (프레임마다 X, interval마다)
        if (Time.time - lastDamageTime < damageInterval) return;

        var health = other.GetComponentInParent<MonkeyHealth>();
        if (health == null) return;

        // 자폭 방지: 투척자 제외
        if (health.photonView != null && health.photonView.ViewID == ownerViewID) return;

        // 이미 죽은 대상 무시
        if (health.IsDead) return;

        // 데미지 적용
        int damage = Mathf.RoundToInt(damagePerSecond * damageInterval);
        float[] colorArr = { paintColor.r, paintColor.g, paintColor.b, paintColor.a };
        health.TakeDamageNetwork(damage, "PaintZone", false, colorArr);

        lastDamageTime = Time.time;
    }

    // ─── 바닥 데칼 ───────────────────────────────────────────────

    /// <summary>
    /// 존 영역 바닥에 팀색 페인트 데칼을 5~8개 랜덤으로 뿌린다.
    /// 기존 DecalProjector 기반 시스템 재활용.
    /// </summary>
    void SpawnFloorDecals()
    {
        if (ObjectPoolManager.Instance == null) return;

        int count = Random.Range(5, 9);
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = Random.insideUnitCircle * maxRadius * 0.7f;
            Vector3 decalPos = transform.position + new Vector3(offset.x, 0.05f, offset.y);

            // Raycast로 바닥 찾기
            if (Physics.Raycast(decalPos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f))
            {
                decalPos = hit.point + hit.normal * 0.02f;
            }

            GameObject decalObj = ObjectPoolManager.Instance.GetDecal();
            if (decalObj == null) continue;

            decalObj.transform.position = decalPos;
            decalObj.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);

            // DecalProjector 색상 적용
            var projector = decalObj.GetComponent<DecalProjector>();
            if (projector != null)
            {
                // 런타임 머티리얼 인스턴스 생성 후 색상 설정
                if (projector.material != null)
                {
                    var mat = new Material(projector.material);
                    mat.color = paintColor;
                    projector.material = mat;
                }
                projector.size = new Vector3(
                    Random.Range(1.5f, 2.5f),
                    Random.Range(1.5f, 2.5f),
                    0.5f);
            }

            decalObj.SetActive(true);
        }
    }

    // ─── 정리 ─────────────────────────────────────────────────────

    void OnDestroy()
    {
        if (zoneMaterial != null)
            Destroy(zoneMaterial);
    }
}
