using UnityEngine;

[RequireComponent(typeof(Rigidbody))] // 이 스크립트를 붙이면 자동으로 Rigidbody 추가
public class PaintProjectile : MonoBehaviour
{
    // 맵 밖으로 영원히 날아가는 것을 방지하는 수명
    public float lifeTime = 5f;

    void Start()
    {
        // 생성된 시점(총알 발사 직후)으로부터 5초 뒤에 스스로를 파괴시킵니다.
        Destroy(gameObject, lifeTime);
    }

    void OnCollisionEnter(Collision collision)
    {
        // 총알이 날아가다 어떤 물체 물리엔진에 '부딪혔을 때' 1번 실행
        
        // 1. 그 물체가 '바닥(Floor 레이어)'인가?
        if (collision.gameObject.layer == LayerMask.NameToLayer("Floor"))
        {
            // 이 위치에 페인트 자국을 남긴다~ 라는 주석을 개발일지에 적기 좋음!
            Destroy(gameObject); // 부딪혔으니 이 총알은 삭제
        }
        // 2. 그 물체가 '마네킹/적'의 몸통이나 머리인가?
        else if (collision.gameObject.CompareTag("Body") || collision.gameObject.CompareTag("Head"))
        {
            // 맞췄을 때 로그 띄우기 (추후 데미지/체력 감소 체계 연결)
            Debug.Log($"명중! 부위: {collision.gameObject.tag}");
            
            Destroy(gameObject); // 명중했으므로 총알 삭제
        }
        else
        {
            // 기타 다른 벽(레이어 지정 안 한 곳)에 닿아도 일단 총알 삭제
            Destroy(gameObject);
        }
    }
}
