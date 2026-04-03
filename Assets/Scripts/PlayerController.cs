using UnityEngine;

/// <summary>
/// 1인칭 플레이어 이동 + 시점 회전 + Animator 연동 + 정지 패널티.
///
/// [설계 원칙]
/// - 각 기능을 Handle___() 메서드로 분리하여 단일 책임 원칙(SRP) 준수.
/// - animator가 null이면 애니메이션 코드를 조용히 건너뜀 (모델 없이도 동작).
/// - 정지 패널티는 키보드 입력이 아닌 '실제 월드 좌표 이동량'으로 판정.
///   → 제자리 점프, 벽에 붙어서 키만 누르기 등의 꼼수를 원천 차단.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("이동 설정")]
    public float walkSpeed = 5f;
    public float runSpeed = 8f;
    public float jumpForce = 5f;
    public float gravity = -9.81f;

    [Header("시점 설정")]
    public Transform cameraTransform;
    public float mouseSensitivity = 2f;
    public float upLimit = -80f;
    public float downLimit = 80f;

    [Header("Animator 연동")]
    [Tooltip("캐릭터의 Animator 컴포넌트. 비워두면 애니메이션 없이 동작")]
    public Animator animator;

    [Header("정지 패널티")]
    [Tooltip("몇 초 동안 안 움직이면 패널티를 줄 것인지")]
    public float penaltyInterval = 5f;
    [Tooltip("이 거리(m) 이상 움직여야 '이동한 것'으로 인정")]
    public float minMoveDistance = 1f;

    // ── Animator 파라미터 해시 (상수) ──────────────────────────────
    // [왜 StringToHash?] 매 프레임 문자열 비교 대신 정수 비교로 성능 확보 + 오타 방지
    private static readonly int AnimSpeed      = Animator.StringToHash("Speed");
    private static readonly int AnimIsGrounded = Animator.StringToHash("isGrounded");
    private static readonly int AnimJump       = Animator.StringToHash("Jump");

    // ── 내부 상태 ─────────────────────────────────────────────────
    private CharacterController characterController;
    private Vector3 velocity;
    private float xRotation;

    // 정지 패널티 추적용
    private Vector3 penaltyCheckPosition;
    private float penaltyTimer;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // 패널티 기준점을 시작 위치로 초기화
        penaltyCheckPosition = transform.position;
        penaltyTimer = 0f;
    }

    void Update()
    {
        HandleLook();
        HandleMovement();
        HandleIdlePenalty();
    }

    // ── 시점 회전 ─────────────────────────────────────────────────

    void HandleLook()
    {
        if (cameraTransform == null) return;

        float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, upLimit, downLimit);
        cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        transform.Rotate(Vector3.up * mouseX);
    }

    // ── 이동 / 점프 / 중력 ────────────────────────────────────────

    void HandleMovement()
    {
        bool isGrounded = characterController.isGrounded;

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        bool isRunning = Input.GetKey(KeyCode.LeftShift);
        float currentSpeed = isRunning ? runSpeed : walkSpeed;

        Vector3 move = transform.right * x + transform.forward * z;
        characterController.Move(move * currentSpeed * Time.deltaTime);

        // 점프
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpForce * -2f * gravity);
            SetAnimTrigger(AnimJump);
        }

        // 중력
        velocity.y += gravity * Time.deltaTime;
        characterController.Move(velocity * Time.deltaTime);

        // Animator 업데이트
        float speedValue = move.magnitude > 0.1f ? (isRunning ? 1f : 0.5f) : 0f;
        SetAnimFloat(AnimSpeed, speedValue);
        SetAnimBool(AnimIsGrounded, isGrounded);
    }

    // ── 5초 정지 패널티 ───────────────────────────────────────────
    // [판정 기준] 키보드 입력(Input.GetAxis)이 아닌 transform.position의 실제 변화량.
    // → 벽에 붙어서 W키만 꾹 누르고 있어도 실제 좌표가 안 바뀌면 패널티 발동.
    // → 제자리 점프만 반복해도 착지 위치가 같으면 패널티 발동.

    void HandleIdlePenalty()
    {
        penaltyTimer += Time.deltaTime;

        if (penaltyTimer < penaltyInterval) return;

        // 5초가 지났으므로 이동 거리 판정
        float distanceMoved = Vector3.Distance(transform.position, penaltyCheckPosition);

        if (distanceMoved <= minMoveDistance)
        {
            // 패널티 발동!
            Debug.Log($"[패널티] 5초간 이동 거리 {distanceMoved:F2}m — 정지 패널티 발동!");
            // TODO: 추후 발밑에 페인트 누수 이펙트(데칼) 생성으로 교체 예정
        }

        // 판정 후 타이머와 기준 위치 리셋 (패널티 발동 여부와 무관하게 항상 리셋)
        penaltyTimer = 0f;
        penaltyCheckPosition = transform.position;
    }

    // ── Animator 헬퍼 ─────────────────────────────────────────────
    // [왜 헬퍼?] null 체크를 한 곳에서 처리 → DRY 원칙

    void SetAnimFloat(int hash, float value)
    {
        if (animator != null)
            animator.SetFloat(hash, value, 0.1f, Time.deltaTime);
    }

    void SetAnimBool(int hash, bool value)
    {
        if (animator != null)
            animator.SetBool(hash, value);
    }

    void SetAnimTrigger(int hash)
    {
        if (animator != null)
            animator.SetTrigger(hash);
    }
}
