using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 표면에 투영되는 페인트 데칼.
/// URP DecalProjector를 사용하여 모서리, 곡면에도 자연스럽게 감싸진다.
///
/// [발자국 감지용 Trigger]
/// 보이지 않는 자식 오브젝트에 BoxCollider(isTrigger) 배치.
/// FootprintManager가 CheckSphere로 감지.
/// </summary>
public class PaintDecal : MonoBehaviour
{
    [Tooltip("데칼이 바닥에 남아있는 시간(초). 기획서 기준 5초")]
    public float lifeTime = 5f;

    private float timer;
    private DecalProjector projector;
    private GameObject triggerChild;

    void Awake()
    {
        projector = GetComponent<DecalProjector>();

        // ── 발자국 감지용 Trigger 자식 오브젝트 ──
        int paintTriggerLayer = LayerMask.NameToLayer("PaintTrigger");

        triggerChild = new GameObject("PaintTriggerZone");
        triggerChild.transform.SetParent(transform, false);
        triggerChild.transform.localPosition = Vector3.zero;
        triggerChild.transform.localRotation = Quaternion.identity;

        if (paintTriggerLayer >= 0)
            triggerChild.layer = paintTriggerLayer;

        BoxCollider bc = triggerChild.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(1f, 0.05f, 1f);
        bc.center = new Vector3(0f, 0.025f, 0f);
    }

    void OnEnable()
    {
        timer = lifeTime;

        if (triggerChild != null)
            triggerChild.SetActive(true);
    }

    void OnDisable()
    {
        if (triggerChild != null)
            triggerChild.SetActive(false);
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
            ReturnToPool();
    }

    /// <summary>
    /// 팀 컬러를 적용한다. DecalTintCache의 공용 캐시 사용.
    /// </summary>
    public void SetColor(Color teamColor)
    {
        if (projector == null) return;

        Material tinted = DecalTintCache.GetTintedMaterial(teamColor, projector);
        if (tinted != null)
        {
            projector.material = tinted;

            // DecalProjector 렌더링 강제 갱신
            projector.enabled = false;
            projector.enabled = true;
        }
    }

    void ReturnToPool()
    {
        if (ObjectPoolManager.Instance != null)
            ObjectPoolManager.Instance.ReturnDecal(gameObject);
        else
            gameObject.SetActive(false);
    }
}
