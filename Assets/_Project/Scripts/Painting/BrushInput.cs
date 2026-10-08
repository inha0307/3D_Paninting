using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Wonju.PaintApp
{
    /// <summary>
    /// 좌클릭 드래그를 캐릭터 위의 획으로 바꿉니다.
    /// 화면에서 일정 간격으로 레이캐스트해 표면을 따라가므로, 빠르게 그어도 획이 끊기거나 허공을 가로지르지 않습니다.
    /// </summary>
    public class BrushInput : MonoBehaviour
    {
        [SerializeField] PaintCanvas canvas;
        [SerializeField] Camera viewCamera;
        [SerializeField] OrbitCamera orbit;

        [SerializeField] Color color = new(0.93f, 0.26f, 0.21f);
        [Tooltip("브러시 반지름 (m). 캐릭터 키는 약 1m입니다.")]
        [SerializeField] float radius = 0.03f;
        [Tooltip("이 픽셀 간격마다 레이캐스트합니다.")]
        [SerializeField] float screenStepPixels = 4f;
        [Tooltip("연속한 두 점이 반지름의 이 배수보다 멀면(실루엣을 넘은 경우) 잇지 않습니다.")]
        [SerializeField] float maxJoinRadii = 4f;

        readonly InputAction paint = new("Paint", InputActionType.Button, "<Mouse>/leftButton");
        readonly InputAction point = new("Point", InputActionType.Value, "<Pointer>/position");

        bool stroking;
        Vector2 lastScreen;
        bool hasLastHit;
        Vector3 lastPoint;

        public Color Color { get => color; set => color = value; }
        public float Radius { get => radius; set => radius = value; }
        public bool IsStroking => stroking;

        /// <summary>획을 시작하기 직전. 되돌리기 스냅샷을 여기서 찍습니다.</summary>
        public event Action StrokeStarted;
        public event Action StrokeEnded;

        void OnEnable()
        {
            paint.Enable();
            point.Enable();
        }

        void OnDisable()
        {
            paint.Disable();
            point.Disable();
            EndStroke();
        }

        void Update()
        {
            var screen = point.ReadValue<Vector2>();

            if (!stroking && paint.WasPressedThisFrame() && CanStartStroke())
            {
                stroking = true;
                hasLastHit = false;
                StrokeStarted?.Invoke();
                PaintAt(screen);
                lastScreen = screen;
            }
            else if (stroking && paint.IsPressed())
            {
                PaintAlong(lastScreen, screen);
                lastScreen = screen;
            }

            if (stroking && !paint.IsPressed())
                EndStroke();
        }

        /// <summary>화면 좌표 두 점 사이를 일정 간격으로 따라가며 칠합니다.</summary>
        public void PaintAlong(Vector2 from, Vector2 to)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / screenStepPixels));
            for (int i = 1; i <= steps; i++)
                PaintAt(Vector2.Lerp(from, to, (float)i / steps));
        }

        /// <summary>화면 좌표 아래 표면을 칠하고, 직전 점이 가까우면 선으로 잇습니다.</summary>
        public void PaintAt(Vector2 screen)
        {
            var ray = viewCamera.ScreenPointToRay(screen);
            if (!canvas.Collider.Raycast(ray, out var hit, 100f))
            {
                hasLastHit = false;
                return;
            }

            bool join = hasLastHit && Vector3.Distance(lastPoint, hit.point) <= radius * maxJoinRadii;
            canvas.PaintSegment(join ? lastPoint : hit.point, hit.point, hit.normal, color, radius);
            lastPoint = hit.point;
            hasLastHit = true;
        }

        void EndStroke()
        {
            if (!stroking) return;
            stroking = false;
            hasLastHit = false;
            StrokeEnded?.Invoke();
        }

        bool CanStartStroke()
        {
            if (orbit != null && orbit.IsDragging) return false;
            return EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject();
        }
    }
}
