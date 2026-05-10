using UnityEngine;

/// <summary>
/// 정지 패널티 시 하늘에서 떨어지는 페인트 폭탄.
/// 런타임에 색깔 구체를 생성하고, 바닥에 도달하면 폭발 파티클을 생성한다.
/// 외부 에셋 불필요 — Sphere 프리미티브를 코드로 생성.
///
/// [사용법]
/// PenaltyBomb.Spawn(착지위치, 팀색상, callback) 호출 시:
///   1. 착지점 위 15m에서 색깔 구체 생성
///   2. 낙하 (0.8초, 가속)
///   3. 착지 시 폭발 파티클 + 효과음 + 콜백 호출 후 자동 파괴
/// </summary>
public class PenaltyBomb : MonoBehaviour
{
    private Vector3 targetPosition;
    private Vector3 startPosition;
    private Color bombColor;
    private float fallDuration = 0.8f;    // 낙하 시간 (초) — 잘 보이도록 느리게
    private float timer;
    private bool hasLanded;

    private System.Action onLandCallback;

    /// <summary>
    /// 패널티 폭탄을 소환한다. 모든 클라이언트에서 호출 가능.
    /// onLand: 착지 시 실행할 콜백 (데칼 생성 등)
    /// </summary>
    public static void Spawn(Vector3 landPosition, Color teamColor, System.Action onLand = null)
    {
        // 구체 생성
        GameObject bomb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bomb.name = "PenaltyBomb";
        bomb.transform.localScale = Vector3.one * 1.2f;  // 크게

        // 물리 충돌 제거 (시각 전용)
        var collider = bomb.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        // 색상 적용
        var renderer = bomb.GetComponent<Renderer>();
        if (renderer != null)
        {
            // 발광하는 Unlit 머티리얼로 눈에 띄게
            Material mat = null;
            var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader != null && unlitShader.name != "Hidden/InternalErrorShader")
                mat = new Material(unlitShader);
            else
                mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = teamColor;
            renderer.material = mat;
        }

        // 트레일 추가 (떨어지면서 색깔 꼬리)
        var trail = bomb.AddComponent<TrailRenderer>();
        trail.time = 0.5f;
        trail.startWidth = 0.8f;
        trail.endWidth = 0.05f;
        var trailMat = new Material(Shader.Find("Sprites/Default"));
        if (trailMat != null) trail.material = trailMat;
        trail.startColor = teamColor;
        Color fadeColor = teamColor;
        fadeColor.a = 0f;
        trail.endColor = fadeColor;
        trail.minVertexDistance = 0.1f;

        // 시작 위치: 착지점 위 15m
        float dropHeight = 15f;
        bomb.transform.position = landPosition + Vector3.up * dropHeight;

        // PenaltyBomb 컴포넌트 부착
        var comp = bomb.AddComponent<PenaltyBomb>();
        comp.targetPosition = landPosition;
        comp.startPosition = bomb.transform.position;
        comp.bombColor = teamColor;
        comp.onLandCallback = onLand;
    }

    void Update()
    {
        if (hasLanded) return;

        timer += Time.deltaTime;
        float t = timer / fallDuration;

        if (t >= 1f)
        {
            // 착지!
            transform.position = targetPosition;
            OnLand();
            return;
        }

        // 가속 낙하 (EaseIn — 점점 빨라지는 느낌)
        float easedT = t * t;
        transform.position = Vector3.Lerp(startPosition, targetPosition, easedT);

        // 떨어지면서 크기 커짐 (임박감)
        float scale = Mathf.Lerp(0.8f, 1.5f, t);
        transform.localScale = Vector3.one * scale;
    }

    void OnLand()
    {
        hasLanded = true;

        // 폭발 파티클 생성
        SpawnExplosionParticle();

        // 효과음
        if (SFXManager.Instance != null)
            SFXManager.Instance.PlayHit(targetPosition);

        // 착지 콜백 (데칼 생성 등)
        onLandCallback?.Invoke();

        // 자기 자신 제거
        Destroy(gameObject, 0.1f);
    }

    /// <summary>
    /// 착지 시 페인트 폭발 파티클을 코드로 생성한다.
    /// 위로 튀는 페인트 방울 느낌의 작은 구체들.
    /// </summary>
    void SpawnExplosionParticle()
    {
        GameObject fx = new GameObject("PenaltyExplosion");
        fx.transform.position = targetPosition;

        var ps = fx.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startColor = bombColor;
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        main.maxParticles = 40;
        main.duration = 0.1f;
        main.loop = false;
        main.gravityModifier = 1.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0f, 25, 40)
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 60f;
        shape.radius = 0.5f;

        // 머티리얼 (기본 파티클)
        var psr = fx.GetComponent<ParticleSystemRenderer>();
        if (psr != null)
        {
            psr.material = new Material(Shader.Find("Sprites/Default"));
            psr.material.color = bombColor;
        }

        // 크기 감소 (시간에 따라)
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0f)
            ));

        ps.Play();
        Destroy(fx, 2f);
    }
}
