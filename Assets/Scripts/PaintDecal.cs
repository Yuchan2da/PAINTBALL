using UnityEngine;

/// <summary>
/// 바닥에 남는 페인트 자국.
/// 풀에서 꺼내져 배치된 뒤, 일정 시간 후 자동으로 풀에 반환된다.
///
/// [발자국 감지용 Trigger — 자식 오브젝트 방식]
/// 데칼 본체는 Default 레이어(카메라에 보인다)를 유지하고,
/// 보이지 않는 자식 오브젝트에 BoxCollider(isTrigger)를 PaintTrigger 레이어로 배치.
/// → 카메라 culling 문제 없이 FootprintManager가 CheckSphere로 감지 가능.
/// </summary>
public class PaintDecal : MonoBehaviour
{
    [Tooltip("데칼이 바닥에 남아있는 시간(초). 기획서 기준 5초")]
    public float lifeTime = 5f;

    private float timer;
    private GameObject triggerChild;

    void Awake()
    {
        int paintTriggerLayer = LayerMask.NameToLayer("PaintTrigger");

        // 보이지 않는 자식 오브젝트를 만들어 Trigger 콜라이더를 배치
        // → 본체(Default 레이어)는 카메라에 정상 렌더링됨
        triggerChild = new GameObject("PaintTriggerZone");
        triggerChild.transform.SetParent(transform, false);
        triggerChild.transform.localPosition = Vector3.zero;
        triggerChild.transform.localRotation = Quaternion.identity;

        // PaintTrigger 레이어 설정 (자식에만!)
        if (paintTriggerLayer >= 0)
            triggerChild.layer = paintTriggerLayer;

        // 얇고 넓은 BoxCollider (isTrigger)
        BoxCollider bc = triggerChild.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(1f, 0.05f, 1f);
        bc.center = new Vector3(0f, 0.025f, 0f);
    }

    void OnEnable()
    {
        // 풀에서 꺼내질 때마다 수명 타이머 리셋
        timer = lifeTime;

        // 자식 트리거도 활성화
        if (triggerChild != null)
            triggerChild.SetActive(true);
    }

    void OnDisable()
    {
        // 풀 반환 시 자식 트리거도 비활성화
        if (triggerChild != null)
            triggerChild.SetActive(false);
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
            ObjectPoolManager.Instance.ReturnDecal(gameObject);
        else
            gameObject.SetActive(false);
    }
}
