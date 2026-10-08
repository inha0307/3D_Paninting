using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Wonju.PaintApp
{
    /// <summary>
    /// 캐릭터를 중심으로 도는 카메라. 우클릭 드래그로 회전, 휠로 커서 위치를 향해 확대·축소, Home 키로 시점 초기화.
    /// 좌클릭은 칠하기에 쓰므로 여기서는 다루지 않습니다. 조명은 카메라를 따라와 어느 면을 봐도 같은 밝기입니다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class OrbitCamera : MonoBehaviour
    {
        [Tooltip("이 렌더러의 바운드 중심을 회전 중심으로 씁니다.")]
        [SerializeField] Renderer target;

        [Header("회전")]
        [SerializeField] float degreesPerPixel = 0.3f;
        [SerializeField] float minPitch = -85f;
        [SerializeField] float maxPitch = 85f;

        [Header("확대·축소")]
        [SerializeField] float minDistance = 0.6f;
        [SerializeField] float maxDistance = 4f;
        [Tooltip("휠 한 칸에 거리가 줄어드는 비율")]
        [SerializeField] float zoomPerNotch = 0.12f;

        [Header("초기 시점")]
        [SerializeField] float initialYaw = 180f;
        [SerializeField] float initialPitch = 10f;
        [SerializeField] float initialDistance = 2.6f;

        [Header("헤드라이트")]
        [Tooltip("카메라를 따라 회전하는 조명. 비우면 조명을 움직이지 않습니다.")]
        [SerializeField] Light headlight;
        [Tooltip("카메라 방향에서 조명을 위·옆으로 기울이는 각도")]
        [SerializeField] Vector2 headlightOffset = new(20f, 20f);

        [Tooltip("값이 클수록 입력을 빨리 따라갑니다.")]
        [SerializeField] float smoothing = 18f;

        readonly InputAction rotateHold = new("RotateHold", InputActionType.Button, "<Mouse>/rightButton");
        readonly InputAction pointerDelta = new("PointerDelta", InputActionType.Value, "<Pointer>/delta");
        readonly InputAction scroll = new("Scroll", InputActionType.Value, "<Mouse>/scroll/y");
        readonly InputAction resetView = new("ResetView", InputActionType.Button, "<Keyboard>/home");

        Camera cam;
        float yaw, pitch, distance;
        Vector3 pivotOffset;
        float currentYaw, currentPitch, currentDistance;
        Vector3 currentPivotOffset;
        bool dragging;

        Vector3 TargetCenter => target != null ? target.bounds.center : Vector3.zero;
        public Vector3 Pivot => TargetCenter + currentPivotOffset;
        public bool IsDragging => dragging;

        void Awake()
        {
            cam = GetComponent<Camera>();
            ResetView();
            SnapToTarget();
        }

        void OnEnable()
        {
            rotateHold.Enable();
            pointerDelta.Enable();
            scroll.Enable();
            resetView.Enable();
        }

        void OnDisable()
        {
            rotateHold.Disable();
            pointerDelta.Disable();
            scroll.Disable();
            resetView.Disable();
            dragging = false;
        }

        void Update()
        {
            if (rotateHold.WasPressedThisFrame() && !IsPointerOverUI())
                dragging = true;
            if (!rotateHold.IsPressed())
                dragging = false;
            if (dragging)
                Orbit(pointerDelta.ReadValue<Vector2>());

            float wheel = scroll.ReadValue<float>();
            if (wheel != 0f && !IsPointerOverUI())
                ZoomAt(ToNotches(wheel), Pointer.current.position.ReadValue());

            if (resetView.WasPressedThisFrame())
                ResetView();
        }

        void LateUpdate()
        {
            float t = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            currentYaw = Mathf.LerpAngle(currentYaw, yaw, t);
            currentPitch = Mathf.Lerp(currentPitch, pitch, t);
            currentDistance = Mathf.Lerp(currentDistance, distance, t);
            currentPivotOffset = Vector3.Lerp(currentPivotOffset, pivotOffset, t);
            Apply();
        }

        /// <summary>화면 픽셀 이동량만큼 회전합니다. 오른쪽으로 끌면 캐릭터가 오른쪽으로 돕니다.</summary>
        public void Orbit(Vector2 pixelDelta)
        {
            yaw += pixelDelta.x * degreesPerPixel;
            pitch = Mathf.Clamp(pitch - pixelDelta.y * degreesPerPixel, minPitch, maxPitch);
        }

        /// <summary>회전 중심을 향해 확대·축소합니다. 양수면 가까이, 음수면 멀리. 휠 한 칸 = 1.</summary>
        public void Zoom(float notches)
        {
            distance = Mathf.Clamp(distance * Mathf.Pow(1f - zoomPerNotch, notches), minDistance, maxDistance);
        }

        /// <summary>
        /// 화면 좌표 아래의 지점이 그대로 커서 아래에 남도록 확대·축소합니다.
        /// 회전 중심을 지나는 화면과 평행한 평면에서, 그 지점을 기준으로 회전 중심을 거리 비율만큼 끌어당깁니다.
        /// </summary>
        public void ZoomAt(float notches, Vector2 screenPosition)
        {
            float before = distance;
            Zoom(notches);
            float ratio = distance / before;
            if (Mathf.Approximately(ratio, 1f) || target == null)
                return;

            // 멀어질 때는 초기 거리에 가까워질수록 회전 중심을 캐릭터 가운데로 되돌립니다.
            if (ratio > 1f)
            {
                float remaining = initialDistance - before;
                pivotOffset = remaining > 0f ? pivotOffset * Mathf.Clamp01((initialDistance - distance) / remaining) : Vector3.zero;
                return;
            }

            var pivot = TargetCenter + pivotOffset;
            var plane = new Plane(-transform.forward, pivot);
            var ray = cam.ScreenPointToRay(screenPosition);
            if (!plane.Raycast(ray, out float enter))
                return;

            var underCursor = ray.GetPoint(enter);
            var newPivot = underCursor + (pivot - underCursor) * ratio;
            pivotOffset = ClampToTarget(newPivot) - TargetCenter;
        }

        /// <summary>정면 초기 시점으로 돌아갑니다. UI 버튼에서도 호출합니다.</summary>
        public void ResetView()
        {
            yaw = initialYaw;
            pitch = initialPitch;
            distance = Mathf.Clamp(initialDistance, minDistance, maxDistance);
            pivotOffset = Vector3.zero;
        }

        /// <summary>보간 없이 목표 시점으로 즉시 이동합니다.</summary>
        public void SnapToTarget()
        {
            currentYaw = yaw;
            currentPitch = pitch;
            currentDistance = distance;
            currentPivotOffset = pivotOffset;
            Apply();
        }

        void Apply()
        {
            var rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
            transform.SetPositionAndRotation(Pivot - rotation * Vector3.forward * currentDistance, rotation);
            if (headlight != null)
                headlight.transform.rotation = rotation * Quaternion.Euler(headlightOffset.x, headlightOffset.y, 0f);
        }

        // 회전 중심이 캐릭터 바깥으로 나가 화면에서 사라지지 않게 합니다.
        Vector3 ClampToTarget(Vector3 point)
        {
            var b = target.bounds;
            return new Vector3(
                Mathf.Clamp(point.x, b.min.x, b.max.x),
                Mathf.Clamp(point.y, b.min.y, b.max.y),
                Mathf.Clamp(point.z, b.min.z, b.max.z));
        }

        // 스크롤 값이 플랫폼 설정에 따라 ±1 또는 ±120 단위로 들어옵니다.
        static float ToNotches(float wheel) => Mathf.Abs(wheel) >= 10f ? wheel / 120f : wheel;

        static bool IsPointerOverUI() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
