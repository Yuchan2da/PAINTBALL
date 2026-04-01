using UnityEngine;

public class PlayerShooter : MonoBehaviour
{
    [Header("사격 설정")]
    public GameObject projectilePrefab; // 발사할 총알 원본 프리팹
    public Transform firePoint;         // 총알이 튀어나올 1인칭 카메라 앞 위치
    public Camera playerCamera;         // 에임을 맞추기 위한 로컬 카메라
    public float fireForce = 35f;       // 발사 추진력

    void Update()
    {
        // 개발 일지 1일차용: 마우스 왼쪽 버튼을 한 번 딸깍 누를 때마다 발사 (쿨타임 X, 장전 X)
        if (Input.GetMouseButtonDown(0))
        {
            FireProjectile();
        }
    }

    void FireProjectile()
    {
        if (projectilePrefab == null || firePoint == null) return;

        // 1. 총알을 'firePoint' 위치에 즉시 1개 복제 생성(Instantiate) 합니다.
        GameObject newProjectile = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);

        // 2. 총알의 머리(앞) 방향을 우리가 바라보는 에임(카메라 정면) 방향과 일치시킵니다.
        newProjectile.transform.rotation = Quaternion.LookRotation(playerCamera.transform.forward);

        // 3. 총알에 붙어있는 Rigidbody(물리 모듈)를 찾아 전방으로 강하게 밀어줍니다.
        Rigidbody rb = newProjectile.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.AddForce(playerCamera.transform.forward * fireForce, ForceMode.Impulse);
        }
    }
}
