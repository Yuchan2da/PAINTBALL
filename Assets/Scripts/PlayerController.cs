using UnityEngine;

/// <summary>
/// 1인칭 플레이어 이동 + 시점 회전 + Animator 연동.
///
/// [Animator 연동 설계 원칙]
/// - animator 필드가 null이면 모든 애니메이션 코드를 조용히 건너뜀.
///   → 지금처럼 캐릭터 모델 없이 캡슐로 테스트할 때도 에러 없이 동작.
///   → 나중에 PSPSPS Monkey 에셋을 붙이면 인스펙터에 드래그만 하면 끝.
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
    [Tooltip("PSPSPS Monkey 에셋 등 캐릭터의 Animator 컴포넌트를 드래그. 없으면 비워둬도 됨.")]
    public Animator animator;

    // Animator 파라미터 이름을 상수로 관리
    // [왜 상수인가?] 오타로 동작 안하는 버그를 사전에 막고,
    // 나중에 파라미터 이름이 바뀌어도 이 한 줄만 수정하면 된다.
    private static readonly int ParamSpeed      = Animator.StringToHash("Speed");
    private static readonly int ParamIsGrounded = Animator.StringToHash("isGrounded");
    private static readonly int ParamJump       = Animator.StringToHash("Jump");

    private CharacterController characterController;
    private Vector3 velocity;
    private float xRotation = 0f;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        HandleLook();
        HandleMovement();
    }

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
            UpdateAnimatorTrigger(ParamJump); // Jump 트리거 발동
        }

        velocity.y += gravity * Time.deltaTime;
        characterController.Move(velocity * Time.deltaTime);

        // Animator 업데이트 (move.magnitude = 실제 이동 입력 크기 0~1)
        // 달리면 1.0, 걸으면 0.5, 가만히 있으면 0으로 블렌드 트리와 연동
        float speedValue = move.magnitude > 0.1f ? (isRunning ? 1f : 0.5f) : 0f;
        UpdateAnimatorFloat(ParamSpeed, speedValue);
        UpdateAnimatorBool(ParamIsGrounded, isGrounded);
    }

    // ── Animator 헬퍼 메서드 ─────────────────────────────────────
    // [왜 헬퍼 메서드를 쓰는가?]
    // 모든 Animator 호출마다 null 체크를 반복하지 않아도 됨. (DRY 원칙)

    void UpdateAnimatorFloat(int paramHash, float value)
    {
        if (animator == null) return;
        // 0.1f = 댐핑값. 값이 즉시 바뀌지 않고 부드럽게 전환됨 (발걸음 애니 끊김 방지)
        animator.SetFloat(paramHash, value, 0.1f, Time.deltaTime);
    }

    void UpdateAnimatorBool(int paramHash, bool value)
    {
        if (animator == null) return;
        animator.SetBool(paramHash, value);
    }

    void UpdateAnimatorTrigger(int paramHash)
    {
        if (animator == null) return;
        animator.SetTrigger(paramHash);
    }
}
