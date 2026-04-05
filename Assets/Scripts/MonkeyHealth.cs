using UnityEngine;

/// <summary>
/// 원숭이(적/플레이어) 체력 관리 스크립트.
///
/// [설계 원칙]
/// - 이 스크립트는 최상위 캐릭터 오브젝트에 붙인다 (히트박스 콜라이더가 아닌 부모).
/// - 히트박스(Head/Body)는 자식 오브젝트에 콜라이더+태그로만 존재하고,
///   데미지 처리는 부모의 이 스크립트가 일괄 담당한다. (단일 책임 원칙)
/// - 추후 Photon 적용 시 TakeDamage를 RPC로 교체하기만 하면 됨.
/// </summary>
public class MonkeyHealth : MonoBehaviour
{
    [Header("체력 설정")]
    public int maxHp = 100;

    // [왜 프로퍼티?] 외부에서 읽을 수는 있지만 수정은 TakeDamage()를 통해서만 가능.
    // → 체력이 임의로 바뀌는 버그를 구조적으로 차단.
    public int CurrentHp { get; private set; }
    public bool IsDead => CurrentHp <= 0;

    void Start()
    {
        CurrentHp = maxHp;
    }

    // ── [테스트 전용] ─────────────────────────────────────────────
    // 적 AI가 없는 프로토타입 단계에서 HP 감소를 확인하기 위한 임시 코드.
    // 적 AI가 완성되면 이 Update() 전체를 삭제한다.
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T)) TakeDamage(10); // Body 피격 시뮬레이션
        if (Input.GetKeyDown(KeyCode.Y)) TakeDamage(20); // Head 피격 시뮬레이션
    }
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 외부에서 호출하는 유일한 데미지 진입점.
    /// </summary>
    public void TakeDamage(int damage)
    {
        if (IsDead) return; // 이미 죽은 대상 중복 처리 방지

        CurrentHp -= damage;
        CurrentHp = Mathf.Max(CurrentHp, 0); // 음수 방지

        Debug.Log($"[피격] {gameObject.name} | 데미지: {damage} | 남은 HP: {CurrentHp}/{maxHp}");

        if (IsDead)
        {
            HandleDeath();
        }
    }

    /// <summary>
    /// 체력이 0이 되었을 때 호출.
    /// [왜 별도 메서드?] 사망 시 연출(이펙트, 사운드, 리스폰 등)을 
    /// 이 메서드 안에서만 관리하면 돼서 유지보수가 편하다.
    /// </summary>
    void HandleDeath()
    {
        Debug.Log($"[처치] {gameObject.name} 사망!");
        // TODO: 사망 연출 (ragdoll, 파티클, 데스캠 등) 추가 예정
        // TODO: Photon 적용 시 리스폰 로직 연결
    }
}
