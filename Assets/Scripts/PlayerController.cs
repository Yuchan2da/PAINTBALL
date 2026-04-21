using UnityEngine;
using Photon.Pun;

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
public class PlayerController : MonoBehaviourPun
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

    // ── 외부 제어 (사망/리스폰 + 멀티플레이) ──────────────────────
    /// <summary>
    /// false로 설정하면 이동, 시점 회전, 점프가 모두 잠긴다.
    /// MonkeyHealth에서 사망/부활 시 토글한다.
    /// </summary>
    [HideInInspector] public bool inputEnabled = true;

    /// <summary>
    /// 로컬 플레이어 여부. Photon 연동 시 photonView.IsMine으로 교체.
    /// 원격 플레이어는 입력/카메라/커서를 비활성화한다.
    /// </summary>
    [HideInInspector] public bool isLocalPlayer = true;

    // ── 내부 상태 ─────────────────────────────────────────────────
    private CharacterController characterController;
    private Vector3 velocity;
    private float xRotation;
    private bool _isGrounded; // HandleMovement에서 매 프레임 갱신

    // 카메라 반동
    private float recoilOffset;         // 현재 반동으로 밀린 각도
    private float recoilRecoverySpeed = 10f; // 복구 속도

    /// <summary>
    /// 캐릭터가 바닥에 닿아있는지 여부. FootprintManager에서 참조.
    /// </summary>
    public bool IsGrounded => _isGrounded;

    // 정지 패널티 추적용
    private Vector3 penaltyCheckPosition;
    private float penaltyTimer;

    void Start()
    {
        characterController = GetComponent<CharacterController>();

        // Photon 연결 시 IsMine으로 로컬/원격 분리
        if (PhotonNetwork.IsConnected && photonView != null)
            isLocalPlayer = photonView.IsMine;

        // 카메라/오디오: Inspector 미연결 시 자동 탐색
        if (cameraTransform == null)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // ── 로컬/원격 분리 ──
        if (isLocalPlayer)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            // 원격 플레이어: 카메라 + AudioListener 비활성화
            if (cameraTransform != null)
            {
                var cam = cameraTransform.GetComponent<Camera>();
                if (cam != null) cam.enabled = false;
                var listener = cameraTransform.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }

        // 패널티 기준점을 시작 위치로 초기화
        penaltyCheckPosition = transform.position;
        penaltyTimer = 0f;
    }

    void Update()
    {
        // 원격 플레이어는 입력 처리하지 않음
        if (!isLocalPlayer) return;

        if (!inputEnabled)
        {
            // 사망 상태에서도 중력만 적용 (공중에서 죽으면 바닥으로 내려와야 함)
            velocity.y += gravity * Time.deltaTime;
            characterController.Move(velocity * Time.deltaTime);
            return;
        }

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

        // 반동 복구: 매 프레임 부드럽게 0으로 회복
        if (recoilOffset > 0f)
        {
            float recovery = recoilRecoverySpeed * Time.deltaTime;
            float applied = Mathf.Min(recovery, recoilOffset);
            xRotation += applied;   // 반동은 음수(위)였으므로, 복구는 양수(아래)
            recoilOffset -= applied;
        }

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, upLimit, downLimit);
        cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        transform.Rotate(Vector3.up * mouseX);
    }

    // ── 이동 / 점프 / 중력 ────────────────────────────────────────

    void HandleMovement()
    {
        _isGrounded = characterController.isGrounded; // 필드 갱신

        if (_isGrounded && velocity.y < 0)
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
        if (Input.GetButtonDown("Jump") && _isGrounded)
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
        SetAnimBool(AnimIsGrounded, _isGrounded);
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
            Debug.Log($"[패널티] 5초간 이동 거리 {distanceMoved:F2}m — 정지 패널티 발동!");
            SpawnPenaltyDecal();
        }

        // 판정 후 타이머와 기준 위치 리셋 (패널티 발동 여부와 무관하게 항상 리셋)
        penaltyTimer = 0f;
        penaltyCheckPosition = transform.position;
    }

    /// <summary>
    /// 패널티 발동 시 발밑에 페인트 데칼을 소환한다.
    /// [왜 isGrounded 체크를 하는가?]
    /// 공중에서 패널티가 발동되면 데칼이 허공에 생성되므로, 땅에 있을 때만 소환한다.
    /// [왜 CharacterController로 발 위치를 계산하는가?]
    /// transform.position은 캐릭터 중심점이므로, CharacterController의 height/center로
    /// 정확한 발바닥 위치를 구해야 데칼이 발밑에 정확히 붙는다.
    /// </summary>
    void SpawnPenaltyDecal()
    {
        if (!_isGrounded) return;
        if (ObjectPoolManager.Instance == null) return;

        // 발바닥 위치 계산: 중심 - (콜라이더 높이의 절반 + 중심 오프셋)
        float halfHeight = characterController.height / 2f;
        Vector3 footPosition = transform.position
            + characterController.center
            - new Vector3(0f, halfHeight - 0.02f, 0f);

        GameObject decal = ObjectPoolManager.Instance.GetDecal();
        if (decal == null) return; // 풀 소진 안전장치

        decal.transform.position = footPosition;
        // 바닥에 눕히기: Quad 앞면(-Z)이 위(+Y)를 향하도록
        decal.transform.rotation = Quaternion.FromToRotation(-Vector3.forward, Vector3.up);
    }

    // ── 카메라 반동 ───────────────────────────────────────────────

    /// <summary>
    /// 외부(PlayerShooter)에서 호출. 카메라를 즉시 위로 밀고,
    /// HandleLook()에서 매 프레임 자연스럽게 복구한다.
    /// </summary>
    public void ApplyRecoil(float angle)
    {
        xRotation -= angle;  // 음수 = 위로
        recoilOffset += angle;
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
