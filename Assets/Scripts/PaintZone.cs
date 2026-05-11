using UnityEngine;
using UnityEngine.Rendering.Universal;
using Photon.Pun;

/// <summary>
/// 페인트 수류탄 폭발 존 (블루존 스타일).
///
/// [동작]
/// 1. 폭발 지점에 반투명 팀색 구체가 천천히 커지며 생성 (expandDuration 동안)
/// 2. 존 안에 있는 적에게 점진적 증가 데미지 (10→45 DPS)
/// 3. duration 후 자동 소멸 (서서히 줄어들며 사라짐)
/// 4. 바닥에 팀색 페인트 데칼 산포
///
/// [비주얼]
/// - 양면 렌더링(Cull Off)으로 존 안에서도 구체가 보임
///
/// [네트워크]
/// - 시각 효과는 모든 클라이언트에서 동일하게 생성 (RPC_Explode에서 호출)
/// - 데미지 판정은 각 클라이언트가 자신의 로컬 플레이어에 대해서만 수행 (Owner-authoritative)
///   → 호스트/비호스트 관계없이 모든 플레이어에게 안정적으로 데미지 적용
/// - 자폭 없음 (ownerViewID 제외)
/// </summary>
public class PaintZone : MonoBehaviour
{
    // ─── 설정 ─────────────────────────────────────────────────────
    float maxRadius = 5f;
    float duration = 5.5f;
    float expandDuration = 0.5f;        // 원이 커지는 시간
    float shrinkDuration = 0.5f;        // 소멸 시 줄어드는 시간
    float damageInterval = 0.5f;        // 데미지 적용 간격

    // DPS 점진적 증가 (시작 → 최대)
    float minDPS = 10f;
    float maxDPS = 45f;
    float dpsRampDuration = 2f;         // 몇 초에 걸쳐 최대 DPS에 도달하는지

    // ─── 상태 ─────────────────────────────────────────────────────
    Color paintColor;
    int ownerViewID;
    float elapsed;
    float currentRadius;
    float lastDamageTime;

    // ─── 비주얼 ───────────────────────────────────────────────────
    GameObject sphereVisual;
    Material zoneMaterial;

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
        // GameSettings에서 수류탄 DPS 기본값 적용 (최소 DPS)
        minDPS = GameSettings.Current.grenadeDPS;
        // 최대 DPS는 최소의 4.5배 (10→45 비율 유지)
        maxDPS = minDPS * 4.5f;

        // ★ Kinematic Rigidbody 추가 — OnTriggerStay 감지 보장
        // Unity 물리 규칙: Trigger 충돌에는 최소 한쪽에 Rigidbody가 필요하다.
        // PaintZone에 Kinematic Rigidbody를 넣어 모든 Collider와 트리거 이벤트 발생.
        var rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // SphereCollider (Trigger) — 데미지 판정용
        var zoneCollider = gameObject.AddComponent<SphereCollider>();
        zoneCollider.isTrigger = true;
        zoneCollider.radius = maxRadius; // 최종 크기로 바로 설정

        // 비주얼: 반투명 양면 렌더링 구체
        CreateSphereVisual();

        // 바닥 데칼 생성
        SpawnFloorDecals();

        // 자동 소멸
        Destroy(gameObject, duration);
    }

    // ─── 비주얼 ───────────────────────────────────────────────────

    /// <summary>
    /// 반투명 팀색 구체를 생성한다.
    /// Cull Off (양면 렌더링)로 존 안에서도 볼 수 있다.
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

        // 반투명 양면 렌더링 머티리얼
        var renderer = sphereVisual.GetComponent<Renderer>();
        zoneMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Color c = paintColor;
        c.a = 0.2f; // 반투명
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

        // ★ 핵심: 양면 렌더링 (Cull Off) → 존 안에서도 구체가 보임
        zoneMaterial.SetFloat("_Cull", 0f); // 0 = Off, 1 = Front, 2 = Back
        zoneMaterial.SetInt("_CullMode", 0);

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
                c.a = Mathf.Lerp(0.2f, 0f, t);
                zoneMaterial.color = c;
            }
        }

        // 비주얼 크기 업데이트
        if (sphereVisual != null)
        {
            float diameter = currentRadius * 2f;
            sphereVisual.transform.localScale = new Vector3(diameter, diameter, diameter);
        }

        // ─── 거리 기반 데미지 판정 (프레임마다) ────────────────────
        // OnTriggerStay 대신 거리 기반 판정을 사용.
        // 이유: 모든 클라이언트에서 자신의 로컬 플레이어에 대해 판정하므로
        //       트리거 충돌 레이어/Rigidbody 설정에 의존하지 않아 더 안정적.
        UpdateDamage();
    }

    // ─── 데미지 판정 ─────────────────────────────────────────────

    /// <summary>
    /// 현재 시점의 DPS를 계산한다.
    /// 시간이 지남에 따라 minDPS → maxDPS로 점진적 증가.
    /// </summary>
    float GetCurrentDPS()
    {
        // 확장 완료 후부터 DPS 증가 시작
        float damageElapsed = Mathf.Max(0f, elapsed - expandDuration);
        float rampT = Mathf.Clamp01(damageElapsed / dpsRampDuration);
        // 가속 커브 (처음엔 느리게, 나중엔 빠르게)
        rampT = rampT * rampT;
        return Mathf.Lerp(minDPS, maxDPS, rampT);
    }

    /// <summary>
    /// 거리 기반 데미지 판정. 각 클라이언트가 자신의 로컬 플레이어에 대해서만 실행.
    /// OnTriggerStay 방식 대신 사용하여 네트워크 환경에서 안정적으로 동작.
    ///
    /// [왜 이 방식?]
    /// - OnTriggerStay는 Rigidbody/Collider 레이어 설정에 따라 원격 플레이어를 감지 못할 수 있음
    /// - 각 클라이언트가 자기 플레이어만 판정 → 중복 데미지 없음 + 네트워크 지연 최소
    /// </summary>
    void UpdateDamage()
    {
        // 확장 중에는 데미지 없음
        if (elapsed < expandDuration) return;

        // 수축 중에는 데미지 없음
        if (elapsed > duration - shrinkDuration) return;

        // 데미지 간격 체크
        if (Time.time - lastDamageTime < damageInterval) return;

        // 로컬 플레이어 찾기
        MonkeyHealth localHealth = FindLocalPlayerHealth();
        if (localHealth == null) return;

        // 자폭 방지: 투척자 제외
        if (localHealth.photonView != null && localHealth.photonView.ViewID == ownerViewID) return;

        // 이미 죽은 대상 무시
        if (localHealth.IsDead) return;

        // 거리 판정: 현재 반경 안에 있는가?
        float dist = Vector3.Distance(transform.position, localHealth.transform.position);
        if (dist > currentRadius) return;

        // 점진적 증가 데미지 적용
        float currentDPS = GetCurrentDPS();
        int damage = Mathf.RoundToInt(currentDPS * damageInterval);
        damage = Mathf.Max(1, damage); // 최소 1 데미지 보장

        // 직접 로컬 TakeDamage 호출 (자기 자신이므로 RPC 불필요)
        Color dmgColor = paintColor;
        localHealth.TakeDamage(damage, "PaintZone", false, dmgColor);

        lastDamageTime = Time.time;
    }

    /// <summary>
    /// 로컬 플레이어(내가 소유한)의 MonkeyHealth를 찾는다.
    /// 캐싱으로 매 프레임 검색 비용 최소화.
    /// </summary>
    MonkeyHealth cachedLocalHealth;
    bool localHealthSearched;

    MonkeyHealth FindLocalPlayerHealth()
    {
        if (localHealthSearched) return cachedLocalHealth;

        // 씬의 모든 MonkeyHealth 중 내 것만 찾기
        var allHealth = FindObjectsByType<MonkeyHealth>(FindObjectsSortMode.None);
        foreach (var h in allHealth)
        {
            if (h.photonView != null && h.photonView.IsMine)
            {
                cachedLocalHealth = h;
                break;
            }
        }

        // 오프라인 모드 (연습모드)
        if (cachedLocalHealth == null && !PhotonNetwork.IsConnected && allHealth.Length > 0)
            cachedLocalHealth = allHealth[0];

        localHealthSearched = true;
        return cachedLocalHealth;
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
