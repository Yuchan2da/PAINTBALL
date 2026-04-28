using UnityEngine;

/// <summary>
/// UV 텍스처 페인팅 시스템.
/// 캐릭터에 부착하면 총알이 맞은 지점에 팀 색상 페인트가 칠해진다.
///
/// [작동 흐름]
/// 1. 캐릭터마다 빈 RenderTexture(페인트맵)를 생성한다.
/// 2. 총알이 맞으면 BakeMesh → MeshCollider Raycast로 UV 좌표를 구한다.
/// 3. 해당 UV 위치에 Graphics.Blit + PaintStamp 셰이더로 원형 페인트를 그린다.
/// 4. PaintSkin 셰이더가 페인트맵을 읽어 칠해진 부분만 보여준다.
///
/// [왜 MeshCollider로 UV를 구하는가?]
/// RaycastHit.textureCoord는 MeshCollider에서만 UV 좌표를 반환한다.
/// SkinnedMeshRenderer는 매 프레임 변형되므로, BakeMesh()로 현재 포즈를
/// 스냅샷 찍은 뒤 MeshCollider에 넣어 Raycast해야 정확한 UV를 얻을 수 있다.
/// </summary>
public class PaintReceiver : MonoBehaviour
{
    [Header("렌더러 연결")]
    [Tooltip("페인트를 받을 Renderer (SkinnedMeshRenderer 또는 MeshRenderer)")]
    public Renderer targetRenderer;

    [Header("페인트 설정")]
    [Tooltip("페인트맵 해상도 (높을수록 정밀하지만 메모리 소모 증가)")]
    public int textureSize = 512;

    [Tooltip("한 방울의 페인트 크기 (UV 공간 기준, 0.01~0.1)")]
    public float splatRadius = 0.05f;

    [Tooltip("페인트 가장자리 선명도 (1=딱딱, 0.1=부드러움)")]
    public float splatHardness = 0.6f;

    // ── 셰이더 프로퍼티 ID ────────────────────────────────────────
    private static readonly int PropPaintMap      = Shader.PropertyToID("_PaintMap");
    private static readonly int PropSplatCenter   = Shader.PropertyToID("_SplatCenter");
    private static readonly int PropSplatRadius   = Shader.PropertyToID("_SplatRadius");
    private static readonly int PropSplatColor    = Shader.PropertyToID("_SplatColor");
    private static readonly int PropSplatHardness = Shader.PropertyToID("_SplatHardness");
    private static readonly int PropSplatTex      = Shader.PropertyToID("_SplatTex");
    private static readonly int PropSplatAngle    = Shader.PropertyToID("_SplatAngle");
    private static readonly int PropRevealAmount  = Shader.PropertyToID("_RevealAmount");

    // ── 내부 상태 ─────────────────────────────────────────────────
    private RenderTexture paintMap;
    private Material stampMaterial;
    private Texture2D splatTexture;
    private Mesh bakedMesh;
    private MeshCollider paintCollider;
    private MaterialPropertyBlock mpb;
    private int paintLayerMask;

    void Start()
    {
        if (targetRenderer == null)
        {
            Debug.LogWarning($"[PaintReceiver] {gameObject.name}: targetRenderer가 비어있습니다.");
            return;
        }

        InitPaintMap();
        InitStampMaterial();
        InitPaintCollider();
        ApplyPaintMapToRenderer();
    }

    // ── 초기화 ────────────────────────────────────────────────────

    /// <summary>
    /// 투명 검정(RGBA 0,0,0,0)으로 초기화된 페인트맵 RenderTexture 생성.
    /// </summary>
    void InitPaintMap()
    {
        paintMap = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
        paintMap.filterMode = FilterMode.Bilinear;
        paintMap.wrapMode = TextureWrapMode.Clamp;
        paintMap.Create();
        ClearPaintMap();
    }

    void InitStampMaterial()
    {
        var stampShader = Shader.Find("Hidden/PaintStamp");
        if (stampShader == null)
        {
            Debug.LogError("[PaintReceiver] Hidden/PaintStamp 셰이더를 찾을 수 없습니다!");
            return;
        }
        stampMaterial = new Material(stampShader);

        // 벽/바닥 데칼과 동일한 PaintSplat 텍스처를 스탬프 마스크로 사용
        splatTexture = Resources.Load<Texture2D>("PaintSplat");
        if (splatTexture == null)
        {
            // Resources 폴더에 없으면 AssetDatabase에서 직접 로드 시도
            splatTexture = UnityEngine.Resources.Load<Texture2D>("PaintSplat");
        }

        if (splatTexture != null)
            stampMaterial.SetTexture(PropSplatTex, splatTexture);
        else
            Debug.LogWarning("[PaintReceiver] PaintSplat 텍스처를 찾을 수 없습니다. 기본 원형이 사용됩니다.");
    }

    /// <summary>
    /// UV 조회 전용 MeshCollider를 생성한다.
    /// [왜 별도 자식 오브젝트에?]
    /// PaintMesh 레이어에 놓아서 일반 물리 충돌에 참여하지 않게 격리한다.
    /// </summary>
    void InitPaintCollider()
    {
        bakedMesh = new Mesh();

        var colliderObj = new GameObject("_PaintCollider");
        colliderObj.transform.SetParent(targetRenderer.transform, false);

        // PaintMesh 레이어 설정 (없으면 기본 레이어 사용)
        int paintLayer = LayerMask.NameToLayer("PaintMesh");
        if (paintLayer >= 0)
            colliderObj.layer = paintLayer;

        paintCollider = colliderObj.AddComponent<MeshCollider>();
        paintLayerMask = paintLayer >= 0 ? (1 << paintLayer) : ~0;

        // 초기 메쉬 할당
        UpdatePaintColliderMesh();
    }

    /// <summary>
    /// MaterialPropertyBlock으로 _PaintMap을 렌더러에 전달한다.
    /// [왜 MaterialPropertyBlock?] 원본 Material을 복제하지 않고 값만 오버라이드하여
    /// 캐릭터마다 고유한 페인트맵을 사용할 수 있다.
    /// </summary>
    void ApplyPaintMapToRenderer()
    {
        mpb = new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(mpb);
        mpb.SetTexture(PropPaintMap, paintMap);
        targetRenderer.SetPropertyBlock(mpb);
    }

    // ── 페인트 칠하기 (외부에서 호출) ─────────────────────────────

    /// <summary>
    /// 월드 좌표 충돌 지점에 페인트를 칠한다.
    /// PaintProjectile에서 호출된다.
    /// 
    /// [이중 시스템]
    /// 1) UV 페인트맵: PaintSkin 셰이더가 읽어서 스텔스(투명) 해제용.
    /// 2) DecalProjector: 벽/바닥과 동일한 자연스러운 페인트 비주얼. 본에 부착.
    /// </summary>
    public void PaintAt(Vector3 worldHitPoint, Vector3 hitNormal, Color teamColor, string hitTag = "Body")
    {
        // ── 1) UV 페인트맵 스탬프 (스텔스 해제용) ──
        if (paintCollider != null && stampMaterial != null)
        {
            UpdatePaintColliderMesh();
            Vector2 uv;
            if (TryGetUVAtPoint(worldHitPoint, hitNormal, out uv))
            {
                float radius = GetSplatRadius(hitTag);
                DrawSplat(uv, teamColor, radius);
            }
        }

        // ── 2) DecalProjector 스폰 (비주얼용 — 벽 데칼과 동일한 모양) ──
        Transform bone = FindNearestBone(worldHitPoint);
        if (bone != null)
            SpawnBodyDecal(worldHitPoint, hitNormal, teamColor, bone, hitTag);
    }

    /// <summary>
    /// 히트 태그에 따른 UV 스플랫 크기를 반환한다.
    /// </summary>
    float GetSplatRadius(string hitTag)
    {
        switch (hitTag)
        {
            case "Head": return splatRadius * 0.6f;
            case "Body": return splatRadius * 1.4f;
            default:     return splatRadius;
        }
    }

    // ── DecalProjector 바디 데칼 ─────────────────────────────────

    /// <summary>
    /// 히트 본의 자식으로 DecalProjector 데칼을 생성한다.
    /// 벽/바닥 데칼과 동일한 머티리얼 → 동일한 자연스러운 비주얼.
    /// </summary>
    void SpawnBodyDecal(Vector3 worldPoint, Vector3 normal, Color color, Transform bone, string hitTag)
    {
        if (ObjectPoolManager.Instance == null) return;

        GameObject decal = ObjectPoolManager.Instance.GetDecal();
        decal.transform.SetParent(bone, true);
        decal.transform.position = worldPoint + normal * 0.005f;
        decal.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);

        // 부위별 데칼 크기 (자연스러운 페인트 크기)
        float decalSize = hitTag == "Head" ? 0.30f : hitTag == "Body" ? 0.45f : 0.40f;
        var projector = decal.GetComponent<UnityEngine.Rendering.Universal.DecalProjector>();
        if (projector != null)
            projector.size = new Vector3(decalSize, decalSize, 0.1f);

        decal.tag = "BodyDecal";

        var paintDecal = decal.GetComponent<PaintDecal>();
        if (paintDecal != null)
            paintDecal.SetColor(color);
    }

    /// <summary>
    /// 충돌 지점에서 가장 가까운 히트박스 본을 찾는다.
    /// </summary>
    Transform FindNearestBone(Vector3 worldPoint)
    {
        int hitboxLayer = LayerMask.NameToLayer("Hitbox");
        Transform nearest = null;
        float minDist = float.MaxValue;

        foreach (var col in GetComponentsInChildren<Collider>(true))
        {
            if (col.gameObject.layer != hitboxLayer) continue;
            float dist = Vector3.Distance(col.transform.position, worldPoint);
            if (dist < minDist) { minDist = dist; nearest = col.transform; }
        }

        if (nearest == null && targetRenderer is SkinnedMeshRenderer smr)
        {
            foreach (var bone in smr.bones)
            {
                if (bone == null) continue;
                float dist = Vector3.Distance(bone.position, worldPoint);
                if (dist < minDist) { minDist = dist; nearest = bone; }
            }
        }
        return nearest;
    }

    /// <summary>
    /// 본에 부착된 모든 바디 데칼을 풀로 반환한다.
    /// 리스폰 시 MonkeyHealth에서 호출.
    /// </summary>
    public void ClearBodyDecals()
    {
        foreach (var decal in GetComponentsInChildren<PaintDecal>(true))
        {
            if (decal.transform.parent != null && decal.transform.parent != transform)
                decal.ReturnToPoolPublic();
        }
    }



    /// <summary>
    /// SkinnedMeshRenderer면 BakeMesh, 일반 MeshRenderer면 sharedMesh를 사용한다.
    /// </summary>
    void UpdatePaintColliderMesh()
    {
        if (targetRenderer is SkinnedMeshRenderer smr)
        {
            smr.BakeMesh(bakedMesh);
            paintCollider.sharedMesh = null; // 강제 리프레시
            paintCollider.sharedMesh = bakedMesh;
        }
        else
        {
            var mf = targetRenderer.GetComponent<MeshFilter>();
            if (mf != null && paintCollider.sharedMesh != mf.sharedMesh)
                paintCollider.sharedMesh = mf.sharedMesh;
        }
    }

    /// <summary>
    /// 충돌 지점 근처에서 MeshCollider로 Raycast하여 UV 좌표를 구한다.
    /// [왜 hitNormal 방향으로 0.3m 뒤에서 쏘는가?]
    /// 총알의 충돌 지점은 Head/Body 콜라이더 표면이지, 메쉬 표면이 아니다.
    /// 약간 뒤에서 메쉬 방향으로 쏴야 MeshCollider 표면의 정확한 UV를 얻을 수 있다.
    /// </summary>
    bool TryGetUVAtPoint(Vector3 worldHitPoint, Vector3 hitNormal, out Vector2 uv)
    {
        uv = Vector2.zero;

        Vector3 rayOrigin = worldHitPoint + hitNormal * 0.3f;
        Vector3 rayDir = -hitNormal;

        RaycastHit hit;
        if (Physics.Raycast(rayOrigin, rayDir, out hit, 1.0f, paintLayerMask))
        {
            uv = hit.textureCoord;
            return uv != Vector2.zero; // UV가 유효한지 확인
        }

        return false;
    }

    /// <summary>
    /// PaintStamp 셰이더를 사용하여 페인트맵 위에 스플래터 텍스처를 그린다.
    /// 매번 랜덤 회전각을 적용하여 반복감을 줄인다.
    /// [왜 temp RenderTexture를 쓰는가?]
    /// 같은 RenderTexture를 동시에 읽기+쓰기하면 정의되지 않은 동작이 발생한다.
    /// </summary>
    void DrawSplat(Vector2 uv, Color color, float radius)
    {
        stampMaterial.SetVector(PropSplatCenter, new Vector4(uv.x, uv.y, 0, 0));
        stampMaterial.SetFloat(PropSplatRadius, radius);
        stampMaterial.SetColor(PropSplatColor, color);
        stampMaterial.SetFloat(PropSplatHardness, splatHardness);
        stampMaterial.SetFloat(PropSplatAngle, Random.Range(0f, Mathf.PI * 2f));

        RenderTexture temp = RenderTexture.GetTemporary(paintMap.descriptor);
        Graphics.Blit(paintMap, temp, stampMaterial);
        Graphics.Blit(temp, paintMap);
        RenderTexture.ReleaseTemporary(temp);
    }

    // ── 테스트 / 유틸리티 ──────────────────────────────────────────

    /// <summary>
    /// [테스트 전용] 랜덤 UV 좌표에 직접 페인트를 칠한다.
    /// [왜 별도 메서드?] SimulateEnemyHit처럼 실제 총알이 없는 상황에서는
    /// 월드→UV 변환 Raycast가 빗나갈 수 있다.
    /// UV에 직접 칠하면 페인트 렌더링 파이프라인만 정확히 테스트할 수 있다.
    /// </summary>
    public void PaintAtRandomUV(Color color)
    {
        if (stampMaterial == null || paintMap == null) return;

        Vector2 randomUV = new Vector2(Random.Range(0.05f, 0.95f), Random.Range(0.05f, 0.95f));
        DrawSplat(randomUV, color, splatRadius);
    }

    /// <summary>
    /// 페인트맵을 완전히 투명하게 초기화하고, RevealAmount도 0으로 리셋한다.
    /// 리스폰 시 MonkeyHealth에서 호출하여 깨끗한 상태로 부활.
    /// </summary>
    public void ClearPaintMap()
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = paintMap;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = prev;

        // 스텔스 복원 (투명화)
        SetReveal(0f);
    }

    /// <summary>
    /// 캐릭터의 정체 노출량을 제어한다.
    /// 0 = 스텔스 (페인트만 보임), 1 = 전신 불투명 (사망 시 정체 노출)
    /// [MaterialPropertyBlock으로 제어하는 이유]
    /// 원본 머티리얼을 변경하지 않아서 캐릭터마다 독립적으로 작동.
    /// </summary>
    public void SetReveal(float amount)
    {
        if (targetRenderer == null) return;

        // mpb가 아직 초기화 안 된 경우 (Start() 전이거나 원격 플레이어)
        if (mpb == null)
        {
            mpb = new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(mpb);
        }

        mpb.SetFloat(PropRevealAmount, amount);
        targetRenderer.SetPropertyBlock(mpb);
    }

    void OnDestroy()
    {
        if (paintMap != null)   { paintMap.Release(); Destroy(paintMap); }
        if (stampMaterial != null) Destroy(stampMaterial);
        if (bakedMesh != null)    Destroy(bakedMesh);
    }
}
