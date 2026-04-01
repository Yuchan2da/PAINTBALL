using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("이동 설정")]
    public float walkSpeed = 5f;
    public float runSpeed = 8f;
    public float jumpForce = 5f;
    public float gravity = -9.81f;

    [Header("시점 설정")]
    public Transform cameraTransform; // 메인 카메라 할당
    public float mouseSensitivity = 2f;
    public float upLimit = -80f; // 고개 올리는 최대 각도
    public float downLimit = 80f; // 고개 내리는 최대 각도

    private CharacterController characterController;
    private Vector3 velocity;
    
    // 마우스 상하(고개) 회전값 누적용
    private float xRotation = 0f;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        
        // 화면 안에서 마우스 커서를 숨기고 고정시킵니다.
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

        // 마우스 입력값 받아오기
        float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        // 상하 시점 회전 (카메라 회전)
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, upLimit, downLimit); // 각도 제한
        cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // 좌우 시점 회전 (플레이어 몸통 전체 회전)
        transform.Rotate(Vector3.up * mouseX);
    }

    void HandleMovement()
    {
        // 바닥에 닿아있으면 중력(수직 속도) 가속을 멈춤
        if (characterController.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f; 
        }

        float x = Input.GetAxis("Horizontal"); // A, D 키
        float z = Input.GetAxis("Vertical");   // W, S 키

        // Shift를 누르고 있으면 달리기 속도 적용
        float currentSpeed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;

        // 로컬 방향 벡터를 기준으로 이동
        Vector3 move = transform.right * x + transform.forward * z;
        characterController.Move(move * currentSpeed * Time.deltaTime);

        // 스페이스바를 누르면(땅에 있을 때) 점프
        if (Input.GetButtonDown("Jump") && characterController.isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpForce * -2f * gravity);
        }

        // 중력 적용
        velocity.y += gravity * Time.deltaTime;
        characterController.Move(velocity * Time.deltaTime);
    }
}
