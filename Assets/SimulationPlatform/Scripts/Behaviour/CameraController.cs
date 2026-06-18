using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("控制开关")]
    public bool isControlEnabled = true;

    [Header("旋转设置")]
    public float rotateSpeed = 2.0f;
    public Transform rotatePivot;
    public float minVerticalAngle = -89f;
    public float maxVerticalAngle = 89f;

    [Header("缩放设置")]
    public float zoomSpeed = 2.0f;
    public float minDistance = 0.5f;
    public float maxDistance = 50f;

    [Header("平移设置")]
    [Range(0.0001f, 0.005f)]
    public float panSpeed = 0.0001f;
    public bool useWorldSpacePan = true;
    public float inputDeadZone = 0.1f;
    public float distanceInfluence = 0.5f;

    [Header("平滑设置")]
    public float rotateSmoothFactor = 8f;

    [Header("第一人称漫游设置")]
    public float moveSpeed = 2.0f;
    public float shiftMultiplier = 3.0f;
    public float minMoveSpeed = 0.5f;
    public float maxMoveSpeed = 10.0f;
    public float speedAdjustSensitivity = 0.5f;

    private Camera mainCamera;
    private float currentX = 0f;
    private float currentY = 0f;
    private float currentDistance = 5f;
    private Vector3 panOffset = Vector3.zero;
    private Vector3 lastMousePos;
    private bool isRotating;
    private Vector3 lastSetPosition;
    private float currentMoveSpeed;
    private bool isFlying;
    private bool isAltPressed;

    void Start()
    {
        mainCamera = GetComponent<Camera>();
        mainCamera.nearClipPlane = 0.01f;
        currentDistance = rotatePivot ? Vector3.Distance(transform.position, rotatePivot.position) : 5f;
        currentMoveSpeed = moveSpeed;
        lastMousePos = Input.mousePosition;

        Vector3 angles = transform.eulerAngles;
        currentX = angles.y;
        currentY = angles.x;
        
        lastSetPosition = transform.position;
    }

    void LateUpdate()
    {
        if (!isControlEnabled) return;

        isAltPressed = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        bool shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float speedMultiplier = shiftPressed ? shiftMultiplier : 1f;

        if (Input.GetMouseButtonDown(1) && !UIUtil.IsPointerOverUI())
        {
            isFlying = true;
            lastMousePos = Input.mousePosition;
        }
        if (Input.GetMouseButtonUp(1))
        {
            isFlying = false;
            
            Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);
            Vector3 pivotPos = rotatePivot ? rotatePivot.position : Vector3.zero;
            panOffset = transform.position - (pivotPos - rotation * Vector3.forward * currentDistance);
            if (rotatePivot)
            {
                currentDistance = Vector3.Distance(transform.position, pivotPos);
            }
        }

        if (isFlying)
        {
            float mouseX = Input.GetAxisRaw("Mouse X") * rotateSpeed * speedMultiplier;
            float mouseY = Input.GetAxisRaw("Mouse Y") * rotateSpeed * speedMultiplier;

            currentX += mouseX;
            currentY -= mouseY;
            currentY = Mathf.Clamp(currentY, minVerticalAngle, maxVerticalAngle);

            Vector3 moveDirection = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) moveDirection += transform.forward;
            if (Input.GetKey(KeyCode.S)) moveDirection -= transform.forward;
            if (Input.GetKey(KeyCode.A)) moveDirection -= transform.right;
            if (Input.GetKey(KeyCode.D)) moveDirection += transform.right;
            if (Input.GetKey(KeyCode.Q)) moveDirection -= Vector3.up;
            if (Input.GetKey(KeyCode.E)) moveDirection += Vector3.up;

            if (moveDirection != Vector3.zero)
            {
                moveDirection.Normalize();
                transform.position += moveDirection * currentMoveSpeed * Time.deltaTime * speedMultiplier;
            }

            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0)
            {
                currentMoveSpeed += scroll * speedAdjustSensitivity;
                currentMoveSpeed = Mathf.Clamp(currentMoveSpeed, minMoveSpeed, maxMoveSpeed);
            }

            lastMousePos = Input.mousePosition;

            UpdateCameraPosition();
        }
        else if (isAltPressed)
        {
            if (Input.GetMouseButton(0) && !UIUtil.IsPointerOverUI())
            {
                if (Input.GetMouseButtonDown(0))
                {
                    lastMousePos = Input.mousePosition;
                }

                float mouseX = Input.GetAxisRaw("Mouse X") * rotateSpeed * speedMultiplier;
                float mouseY = Input.GetAxisRaw("Mouse Y") * rotateSpeed * speedMultiplier;

                currentX += mouseX;
                currentY -= mouseY;
                currentY = Mathf.Clamp(currentY, minVerticalAngle, maxVerticalAngle);

                lastMousePos = Input.mousePosition;
            }
            else if (Input.GetMouseButton(2) && !UIUtil.IsPointerOverUI())
            {
                if (Input.GetMouseButtonDown(2))
                {
                    lastMousePos = Input.mousePosition;
                }

                Vector3 mouseDelta = Input.mousePosition - lastMousePos;

                if (Mathf.Abs(mouseDelta.x) < inputDeadZone && Mathf.Abs(mouseDelta.y) < inputDeadZone)
                {
                    lastMousePos = Input.mousePosition;
                    return;
                }

                float screenScale = Screen.width / 1920f;
                mouseDelta /= screenScale;

                float distanceFactor = Mathf.Lerp(1f, currentDistance, distanceInfluence);
                if (useWorldSpacePan)
                {
                    panOffset += new Vector3(-mouseDelta.x, 0, -mouseDelta.y) * panSpeed * distanceFactor * speedMultiplier;
                }
                else
                {
                    panOffset += transform.TransformDirection(new Vector3(-mouseDelta.x, -mouseDelta.y, 0)) * panSpeed * distanceFactor * speedMultiplier;
                }

                lastMousePos = Input.mousePosition;
            }
            else if (Input.GetMouseButton(1) && !UIUtil.IsPointerOverUI())
            {
                if (Input.GetMouseButtonDown(1))
                {
                    lastMousePos = Input.mousePosition;
                }

                float mouseY = Input.GetAxisRaw("Mouse Y") * zoomSpeed * speedMultiplier;
                currentDistance += mouseY;
                currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);

                lastMousePos = Input.mousePosition;
            }
            else
            {
                lastMousePos = Input.mousePosition;
            }

            float altScroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(altScroll) > 0)
            {
                currentDistance -= altScroll * zoomSpeed * speedMultiplier;
                currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
            }

            UpdateCameraPosition();
        }
        else
        {
            if (Input.GetMouseButton(2) && !UIUtil.IsPointerOverUI())
            {
                if (Input.GetMouseButtonDown(2))
                {
                    lastMousePos = Input.mousePosition;
                }

                Vector3 mouseDelta = Input.mousePosition - lastMousePos;

                if (Mathf.Abs(mouseDelta.x) < inputDeadZone && Mathf.Abs(mouseDelta.y) < inputDeadZone)
                {
                    lastMousePos = Input.mousePosition;
                    return;
                }

                float screenScale = Screen.width / 1920f;
                mouseDelta /= screenScale;

                float distanceFactor = Mathf.Lerp(1f, currentDistance, distanceInfluence);
                if (useWorldSpacePan)
                {
                    panOffset += new Vector3(-mouseDelta.x, 0, -mouseDelta.y) * panSpeed * distanceFactor * speedMultiplier;
                }
                else
                {
                    panOffset += transform.TransformDirection(new Vector3(-mouseDelta.x, -mouseDelta.y, 0)) * panSpeed * distanceFactor * speedMultiplier;
                }

                lastMousePos = Input.mousePosition;
            }
            else
            {
                lastMousePos = Input.mousePosition;
            }

            float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0)
            {
                currentDistance -= scroll * zoomSpeed * speedMultiplier;
                currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
            }

            UpdateCameraPosition();
        }
    }

    void UpdateCameraPosition()
    {
        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);

        if (isFlying)
        {
            transform.rotation = rotation;
            lastSetPosition = transform.position;
        }
        else
        {
            if (transform.position != lastSetPosition)
            {
                Quaternion manualRotation = Quaternion.Euler(currentY, currentX, 0);
                Vector3 manualPivotPos = rotatePivot ? rotatePivot.position : Vector3.zero;
                
                panOffset = transform.position - (manualPivotPos - manualRotation * Vector3.forward * currentDistance);
                currentDistance = rotatePivot ? Vector3.Distance(transform.position, manualPivotPos) : 5f;
            }

            Vector3 pivotPos = rotatePivot ? rotatePivot.position : Vector3.zero;
            Vector3 targetPos = pivotPos + panOffset - rotation * Vector3.forward * currentDistance;

            transform.rotation = Quaternion.Lerp(transform.rotation, rotation, Time.deltaTime * rotateSmoothFactor);
            transform.position = targetPos;
            
            lastSetPosition = transform.position;
        }
    }

    public void ResetCamera()
    {
        panOffset = Vector3.zero;
        currentX = 0f;
        currentY = 0f;
        currentDistance = 5f;
        currentMoveSpeed = moveSpeed;
    }
}