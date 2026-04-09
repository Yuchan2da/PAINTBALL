using UnityEngine;

/// <summary>
/// 발자국 데칼 수명 관리.
/// 풀에서 꺼내질 때 형광색으로 틴팅되고, 일정 시간 후 자동으로 풀에 반환된다.
///
/// [왜 PaintDecal과 별도?]
/// PaintDecal은 바닥 페인트 자국(대형, 5초 수명)이고,
/// FootprintDecal은 발자국(소형, 4초 수명, 형광색 틴팅).
/// 역할과 수명이 다르므로 스크립트를 분리한다.
/// </summary>
public class FootprintDecal : MonoBehaviour
{
    [Tooltip("발자국이 바닥에 남아있는 시간(초)")]
    public float lifeTime = 4f;

    private float timer;
    private Renderer cachedRenderer;
    private MaterialPropertyBlock mpb;
    private static readonly int ColorProp = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        cachedRenderer = GetComponent<Renderer>();
        mpb = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        timer = lifeTime;

        // 형광 핫핑크 색상으로 틴팅 (매번 풀에서 꺼낼 때 적용)
        if (cachedRenderer != null)
        {
            cachedRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(ColorProp, new Color(1f, 0f, 0.8f, 1f)); // 핫핑크
            cachedRenderer.SetPropertyBlock(mpb);
        }
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            ReturnToPool();
        }
    }

    void ReturnToPool()
    {
        if (ObjectPoolManager.Instance != null)
            ObjectPoolManager.Instance.ReturnFootprint(gameObject);
        else
            gameObject.SetActive(false);
    }
}
