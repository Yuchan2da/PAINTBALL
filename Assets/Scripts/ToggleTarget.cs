using UnityEngine;

/// <summary>
/// 지정된 대상 GameObject의 활성 상태를 토글하는 단순 유틸리티.
/// 항상 활성인 버튼 오브젝트에 부착하여 비활성 패널을 열고 닫는다.
/// </summary>
public class ToggleTarget : MonoBehaviour
{
    [Tooltip("토글할 대상 GameObject")]
    [SerializeField] private GameObject target;

    /// <summary>대상의 활성 상태를 반전시킨다.</summary>
    public void Toggle()
    {
        if (target != null)
            target.SetActive(!target.activeSelf);
    }
}
