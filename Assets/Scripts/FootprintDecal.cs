using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 발자국 데칼 수명 관리.
/// DecalProjector 기반으로 표면에 투영되며, 팀 컬러로 틴팅된다.
/// DecalTintCache의 정적 캐시를 사용하여 머티리얼을 공유.
/// </summary>
public class FootprintDecal : MonoBehaviour
{
    [Tooltip("발자국이 바닥에 남아있는 시간(초)")]
    public float lifeTime = 4f;

    private float timer;
    private DecalProjector projector;

    void Awake()
    {
        projector = GetComponent<DecalProjector>();
    }

    void OnEnable()
    {
        timer = lifeTime;
    }

    /// <summary>
    /// 발자국 색상을 외부에서 설정한다 (팀 컬러).
    /// material 변경 후 projector를 재활성화하여 렌더링 갱신.
    /// </summary>
    public void SetColor(Color color)
    {
        if (projector == null) return;

        Material tinted = DecalTintCache.GetTintedMaterial(color, projector);
        if (tinted != null)
        {
            projector.material = tinted;

            // DecalProjector 렌더링 강제 갱신
            projector.enabled = false;
            projector.enabled = true;
        }
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
            ReturnToPool();
    }

    void ReturnToPool()
    {
        if (ObjectPoolManager.Instance != null)
            ObjectPoolManager.Instance.ReturnFootprint(gameObject);
        else
            gameObject.SetActive(false);
    }
}
