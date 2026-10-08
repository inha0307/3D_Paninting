using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Wonju.CharacterShared;

namespace Wonju.PaintApp
{
    /// <summary>
    /// 좌클릭을 누른 자리에 따라 나눕니다. 캐릭터 위면 현재 도구(붓·지우개 획, 채우기, 스포이드), 배경이면 회전.
    /// 한 번 시작한 동작은 버튼을 놓을 때까지 바뀌지 않습니다. 되돌리기 기록과 Ctrl+Z·Ctrl+Y도 여기서 처리합니다.
    /// </summary>
    public class PaintController : MonoBehaviour
    {
        enum DragMode
        {
            None,
            Stroke,
            BackgroundPending,  // 배경을 눌렀지만 아직 회전 임계값만큼 끌지 않음
            BackgroundRotate,
            Consumed,           // 채우기·스포이드처럼 누르는 순간 끝난 동작. 놓을 때까지 대기
        }

        [SerializeField] PaintCanvas canvas;
        [SerializeField] PaintTools tools;
        [SerializeField] Camera viewCamera;
        [SerializeField] OrbitCamera orbit;
        [SerializeField] BrushCursor brushCursor;
        [Tooltip("배경 위에서 보여 줄 회전 커서 (32px, Read/Write 켜짐)")]
        [SerializeField] Texture2D rotateCursor;

        [Tooltip("이 픽셀 간격마다 레이캐스트합니다.")]
        [SerializeField] float screenStepPixels = 4f;
        [Tooltip("연속한 두 점이 반지름의 이 배수보다 멀면(실루엣을 넘은 경우) 잇지 않습니다.")]
        [SerializeField] float maxJoinRadii = 4f;
        [Tooltip("배경에서 이만큼 끌어야 회전을 시작합니다. 실루엣 바로 바깥 클릭이 회전으로 바뀌지 않게 합니다.")]
        [SerializeField] float backgroundDragThreshold = 4f;
        [SerializeField] float pickerCursorRadius = 0.012f;
        [SerializeField] int undoSteps = 20;

        readonly InputAction press = new("Press", InputActionType.Button, "<Mouse>/leftButton");
        readonly InputAction point = new("Point", InputActionType.Value, "<Pointer>/position");
        readonly InputAction undoKey = new("Undo", InputActionType.Button, "<Keyboard>/z");
        readonly InputAction redoKey = new("Redo", InputActionType.Button, "<Keyboard>/y");

        UndoStack history;
        DragMode mode;
        PaintTool strokeTool;
        Vector2 pressScreen, lastScreen;
        bool hasLastHit;
        Vector3 lastPoint;
        Vector2Int pickerPixel = new(-1, -1);
        Color pickerPreview = Color.white;
        SystemCursor systemCursor = SystemCursor.Default;

        enum SystemCursor { Default, Hidden, Rotate }

        // UI가 이 컴포넌트의 Awake보다 먼저 물어볼 수 있으므로 처음 쓸 때 만듭니다.
        UndoStack History
        {
            get
            {
                if (history == null)
                {
                    history = new UndoStack(undoSteps);
                    history.Changed += () => HistoryChanged?.Invoke();
                }
                return history;
            }
        }

        public bool CanUndo => History.CanUndo;
        public bool CanRedo => History.CanRedo;
        public event Action HistoryChanged;

        /// <summary>false면 캔버스 입력과 단축키를 모두 무시합니다 (완료 창이 열려 있을 때 등).</summary>
        public bool InputEnabled { get; set; } = true;

        void OnEnable()
        {
            press.Enable();
            point.Enable();
            undoKey.Enable();
            redoKey.Enable();
        }

        void OnDisable()
        {
            press.Disable();
            point.Disable();
            undoKey.Disable();
            redoKey.Disable();
            mode = DragMode.None;
            ClearHover();
        }

        void OnDestroy() => history?.Dispose();

        void Update()
        {
            if (!InputEnabled)
            {
                mode = DragMode.None;
                ClearHover();
                return;
            }

            HandleShortcuts();

            var screen = point.ReadValue<Vector2>();
            bool overUI = IsPointerOverUI();
            bool blocked = overUI || (orbit != null && orbit.IsDragging);
            RaycastHit hit = default;
            bool hasHit = !blocked && Raycast(screen, out hit);

            if (press.WasPressedThisFrame() && mode == DragMode.None && !blocked)
                Begin(screen, hasHit, hit);
            else if (press.IsPressed())
                Continue(screen);

            if (!press.IsPressed())
                mode = DragMode.None;

            UpdateHover(overUI, hasHit, hit);
            lastScreen = screen;
        }

        public void Undo()
        {
            if (mode == DragMode.Stroke) return;
            if (History.Undo(canvas.Canvas)) canvas.MarkDirty();
        }

        public void Redo()
        {
            if (mode == DragMode.Stroke) return;
            if (History.Redo(canvas.Canvas)) canvas.MarkDirty();
        }

        /// <summary>회차 초기화: 캔버스를 처음 상태로 되돌리고 되돌리기 기록을 지웁니다.</summary>
        public void ResetSession()
        {
            canvas.ResetCanvas();
            History.Clear();
        }

        void Begin(Vector2 screen, bool hasHit, RaycastHit hit)
        {
            pressScreen = screen;
            if (!hasHit)
            {
                mode = DragMode.BackgroundPending;
                return;
            }

            switch (tools.Tool)
            {
                case PaintTool.Brush:
                case PaintTool.Eraser:
                    History.Record(canvas.Canvas);
                    strokeTool = tools.Tool;
                    hasLastHit = false;
                    mode = DragMode.Stroke;
                    PaintHit(hit);
                    break;
                case PaintTool.Fill:
                    var part = canvas.PartAt(hit.textureCoord);
                    if (part != CharacterPart.None)
                    {
                        History.Record(canvas.Canvas);
                        canvas.FillPart(part, tools.Color);
                    }
                    mode = DragMode.Consumed;
                    break;
                case PaintTool.Picker:
                    tools.FinishPicking(canvas.SampleColor(hit.textureCoord));
                    mode = DragMode.Consumed;
                    break;
            }
        }

        void Continue(Vector2 screen)
        {
            switch (mode)
            {
                case DragMode.Stroke:
                    PaintAlong(lastScreen, screen);
                    break;
                case DragMode.BackgroundPending:
                    if (Vector2.Distance(screen, pressScreen) >= backgroundDragThreshold)
                    {
                        mode = DragMode.BackgroundRotate;
                        orbit.Orbit(screen - pressScreen);
                    }
                    break;
                case DragMode.BackgroundRotate:
                    orbit.Orbit(screen - lastScreen);
                    break;
            }
        }

        /// <summary>화면 좌표 두 점 사이를 일정 간격으로 따라가며 칠합니다.</summary>
        void PaintAlong(Vector2 from, Vector2 to)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / screenStepPixels));
            for (int i = 1; i <= steps; i++)
            {
                if (Raycast(Vector2.Lerp(from, to, (float)i / steps), out var hit))
                    PaintHit(hit);
                else
                    hasLastHit = false;
            }
        }

        // 직전 점이 가까우면 선으로 잇고, 실루엣을 넘어 멀어졌으면 새로 찍습니다.
        void PaintHit(RaycastHit hit)
        {
            float radius = tools.Radius;
            bool join = hasLastHit && Vector3.Distance(lastPoint, hit.point) <= radius * maxJoinRadii;
            var start = join ? lastPoint : hit.point;
            if (strokeTool == PaintTool.Eraser)
                canvas.EraseSegment(start, hit.point, hit.normal, radius);
            else
                canvas.PaintSegment(start, hit.point, hit.normal, tools.Color, radius);
            lastPoint = hit.point;
            hasLastHit = true;
        }

        void UpdateHover(bool overUI, bool hasHit, RaycastHit hit)
        {
            if (mode is DragMode.BackgroundPending or DragMode.BackgroundRotate)
            {
                ShowRing(false);
                canvas.HighlightPart = CharacterPart.None;
                SetSystemCursor(SystemCursor.Rotate);
                return;
            }
            if (overUI || (orbit != null && orbit.IsDragging))
            {
                ClearHover();
                return;
            }
            if (!hasHit)
            {
                ShowRing(false);
                canvas.HighlightPart = CharacterPart.None;
                SetSystemCursor(mode == DragMode.Stroke ? SystemCursor.Hidden : SystemCursor.Rotate);
                return;
            }

            var tool = mode == DragMode.Stroke ? strokeTool : tools.Tool;
            canvas.HighlightPart = tool == PaintTool.Fill ? canvas.PartAt(hit.textureCoord) : CharacterPart.None;
            switch (tool)
            {
                case PaintTool.Brush:
                    brushCursor.Show(hit.point, hit.normal, tools.Radius, BrushCursor.ModeBrush, tools.Color);
                    SetSystemCursor(SystemCursor.Hidden);
                    break;
                case PaintTool.Eraser:
                    brushCursor.Show(hit.point, hit.normal, tools.Radius, BrushCursor.ModeEraser, Color.white);
                    SetSystemCursor(SystemCursor.Hidden);
                    break;
                case PaintTool.Picker:
                    brushCursor.Show(hit.point, hit.normal, pickerCursorRadius, BrushCursor.ModePicker, PickerPreview(hit.textureCoord));
                    SetSystemCursor(SystemCursor.Hidden);
                    break;
                default:
                    ShowRing(false);
                    SetSystemCursor(SystemCursor.Default);
                    break;
            }
        }

        // 스포이드 미리보기 색. 마우스가 다른 텍셀로 옮겨 갔을 때만 GPU에서 읽습니다.
        Color PickerPreview(Vector2 uv)
        {
            var pixel = new Vector2Int((int)(uv.x * canvas.Canvas.width), (int)(uv.y * canvas.Canvas.height));
            if (pixel != pickerPixel)
            {
                pickerPixel = pixel;
                pickerPreview = canvas.SampleColor(uv);
            }
            return pickerPreview;
        }

        void ClearHover()
        {
            ShowRing(false);
            if (canvas != null) canvas.HighlightPart = CharacterPart.None;
            SetSystemCursor(SystemCursor.Default);
        }

        void ShowRing(bool show)
        {
            if (!show && brushCursor != null) brushCursor.Hide();
        }

        void SetSystemCursor(SystemCursor value)
        {
            if (systemCursor == value) return;
            systemCursor = value;
            Cursor.visible = value != SystemCursor.Hidden;
            if (value == SystemCursor.Rotate && rotateCursor != null)
                Cursor.SetCursor(rotateCursor, new Vector2(rotateCursor.width / 2f, rotateCursor.height / 2f), CursorMode.Auto);
            else
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        void HandleShortcuts()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.ctrlKey.isPressed) return;
            bool shift = keyboard.shiftKey.isPressed;
            if (undoKey.WasPressedThisFrame())
            {
                if (shift) Redo();
                else Undo();
            }
            if (redoKey.WasPressedThisFrame())
                Redo();
        }

        bool Raycast(Vector2 screen, out RaycastHit hit) =>
            canvas.Collider.Raycast(viewCamera.ScreenPointToRay(screen), out hit, 100f);

        static bool IsPointerOverUI() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
