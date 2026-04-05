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

    // ── 셰이더 프로퍼티 ID (StringToHash와 동일한 최적화) ──────────
    private static readonly int PropPaintMap     = Shader.PropertyToID("_PaintMap");
    private static readonly int PropSplatCenter  = Shader.PropertyToID("_SplatCenter");
    private static readonly int PropSplatRadius  = Shader.PropertyToID("_SplatRadius");
    private static readonly int PropSplatColor   = Shader.PropertyToID("_SplatColor");
    private static readonly int PropSplatHardness = Shader.PropertyToID("_SplatHardness");

    // ── 내부 상태 ─────────────────────────────────────────────────
    private RenderTexture paintMap;
    private Material stampMaterial;
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
    /// </summary>
    /// <param name="worldHitPoint">총알이 맞은 월드 좌표</param>
    /// <param name="hitNormal">충돌 표면의 법선 방향</param>
    /// <param name="teamColor">발사한 팀의 색상</param>
    public void PaintAt(Vector3 worldHitPoint, Vector3 hitNormal, Color teamColor)
    {
        if (paintCollider == null || stampMaterial == null) return;

        // 1단계: 현재 포즈의 메쉬를 베이크하여 MeshCollider에 반영
        UpdatePaintColliderMesh();

        // 2단계: 충돌 지점에서 메쉬로 Raycast하여 UV 좌표 획득
        Vector2 uv;
        if (!TryGetUVAtPoint(worldHitPoint, hitNormal, out uv)) return;

        // 3단계: 해당 UV에 페인트 스플랫 그리기
        DrawSplat(uv, teamColor);
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
    /// PaintStamp 셰이더를 사용하여 페인트맵 RenderTexture 위에 원형 스플랫을 그린다.
    /// [왜 temp RenderTexture를 쓰는가?]
    /// 같은 RenderTexture를 동시에 읽기+쓰기하면 정의되지 않은 동작이 발생한다.
    /// 임시 RT에 먼저 그린 뒤, 다시 원본에 복사해야 안전하다.
    /// </summary>
    void DrawSplat(Vector2 uv, Color color)
    {
        stampMaterial.SetVector(PropSplatCenter, new Vector4(uv.x, uv.y, 0, 0));
        stampMaterial.SetFloat(PropSplatRadius, splatRadius);
        stampMaterial.SetColor(PropSplatColor, color);
        stampMaterial.SetFloat(PropSplatHardness, splatHardness);

        RenderTexture temp = RenderTexture.GetTemporary(paintMap.descriptor);
        Graphics.Blit(paintMap, temp, stampMaterial);
        Graphics.Blit(temp, paintMap);
        RenderTexture.ReleaseTemporary(temp);
    }

    // ── 유틸리티 ──────────────────────────────────────────────────

    void ClearPaintMap()
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = paintMap;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = prev;
    }

    void OnDestroy()
    {
        if (paintMap != null)   { paintMap.Release(); Destroy(paintMap); }
        if (stampMaterial != null) Destroy(stampMaterial);
        if (bakedMesh != null)    Destroy(bakedMesh);
    }
}
