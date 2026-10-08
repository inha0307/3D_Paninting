using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Wonju.PaintApp.UI
{
    /// <summary>
    /// 색칠 앱 화면을 코드로 만들고 PaintTools·PaintController와 연결합니다.
    /// 배치(1920×1080 기준): 왼쪽 도구 막대, 아래 24색 팔레트, 오른쪽 위 연결 상태와 완료, 오른쪽 아래 처음 보기와 마우스 안내.
    /// </summary>
    public class PaintUI : MonoBehaviour
    {
        [Serializable]
        public struct Icons
        {
            public Sprite brush, eraser, fill, picker, undo, redo, viewReset, done, wifi, wifiOff, mouseLeft, mouseRight, mouseWheel;
        }

        static readonly (string name, int rgb)[] Palette =
        {
            ("빨강", 0xE53935), ("주황", 0xFB8C00), ("노랑", 0xFDD835), ("연두", 0x7CB342), ("초록", 0x2E7D32), ("하늘", 0x29B6F6),
            ("파랑", 0x1E88E5), ("남색", 0x283593), ("보라", 0x8E24AA), ("분홍", 0xEC407A), ("갈색", 0x6D4C41), ("검정", 0x212121),
            ("흰색", 0xFFFFFF), ("연회색", 0xBDBDBD), ("회색", 0x757575), ("피부 1", 0xFFE0C4), ("피부 2", 0xF1C27D), ("피부 3", 0xC68642),
            ("피부 4", 0x8D5524), ("연분홍", 0xF8BBD0), ("연노랑", 0xFFF59D), ("민트", 0xA5D6A7), ("연하늘", 0xB3E5FC), ("연보라", 0xD1C4E9),
        };

        [SerializeField] PaintTools tools;
        [SerializeField] PaintController controller;
        [SerializeField] OrbitCamera orbit;
        [SerializeField] Renderer character;
        [SerializeField] Light headlight;

        [Header("에셋")]
        [SerializeField] Font regularFont;
        [SerializeField] Font semiBoldFont;
        [SerializeField] Sprite roundSprite;
        [SerializeField] Sprite roundOutlineSprite;
        [SerializeField] Sprite circleSprite;
        [SerializeField] Icons icons;

        UIFactory ui;
        Text studentLabel;
        Image statusIcon;
        Text statusText;
        IconButton doneButton;
        readonly Dictionary<PaintTool, IconButton> toolButtons = new();
        readonly List<IconButton> sizeButtons = new();
        RectTransform sizeRow;
        IconButton undoButton, redoButton;
        Image currentColor;
        readonly List<(Color color, Image outline)> swatches = new();
        SubmitDialog dialog;
        RenderTexture frontPreview, backPreview;

        /// <summary>완료 창에서 [보내기]를 눌렀을 때. 받는 쪽이 업로드 후 Dialog.ShowSuccess/ShowFailure를 호출합니다.</summary>
        public event Action<SubmitDialog> SubmitRequested;
        public SubmitDialog Dialog => dialog;

        void Awake()
        {
            ui = new UIFactory(regularFont, semiBoldFont, roundSprite, roundOutlineSprite, circleSprite);
            EnsureEventSystem();
            var canvas = CreateCanvas();

            BuildHeader(canvas);
            BuildToolbar(canvas);
            BuildPalette(canvas);
            BuildViewControls(canvas);

            frontPreview = new RenderTexture(488, 540, 24) { name = "SubmitPreviewFront", antiAliasing = 4 };
            backPreview = new RenderTexture(488, 540, 24) { name = "SubmitPreviewBack", antiAliasing = 4 };
            dialog = SubmitDialog.Create(canvas, ui, frontPreview, backPreview);
            dialog.Submitted += OnSubmit;
            dialog.Closed += () => SetPaintingEnabled(true);
            Tooltip.Create(canvas, ui);

            SetStudent(null);
            SetConnection(null);
        }

        void OnEnable()
        {
            tools.Changed += RefreshTools;
            controller.HistoryChanged += RefreshHistory;
            RefreshTools();
            RefreshHistory();
        }

        void OnDisable()
        {
            tools.Changed -= RefreshTools;
            controller.HistoryChanged -= RefreshHistory;
        }

        void OnDestroy()
        {
            if (frontPreview != null) frontPreview.Release();
            if (backPreview != null) backPreview.Release();
        }

        /// <summary>왼쪽 위 표시. null이면 "색칠하기"만 보입니다.</summary>
        public void SetStudent(string studentId) =>
            studentLabel.text = string.IsNullOrEmpty(studentId) ? "색칠하기" : $"학생 {studentId} · 색칠하기";

        /// <summary>서버 연결 표시. null이면 숨깁니다 (아직 확인 전).</summary>
        public void SetConnection(bool? connected)
        {
            statusIcon.transform.parent.gameObject.SetActive(connected.HasValue);
            if (!connected.HasValue) return;
            statusIcon.sprite = connected.Value ? icons.wifi : icons.wifiOff;
            statusIcon.color = connected.Value ? UIStyle.Good : UIStyle.Bad;
            statusText.text = connected.Value ? "연결됨" : "연결 끊김";
            statusText.color = connected.Value ? UIStyle.Quiet : UIStyle.Bad;
        }

        /// <summary>한 번 보낸 뒤에는 완료 버튼을 "다시 보내기"로 바꿉니다.</summary>
        public void MarkSubmitted() => doneButton.SetLabel("다시 보내기");

        // ───────────────────────── 화면 구성 ─────────────────────────

        Transform CreateCanvas()
        {
            var go = new GameObject("PaintUICanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return go.transform;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
        }

        void BuildHeader(Transform canvas)
        {
            studentLabel = ui.Label(UIFactory.Rect("Student", canvas, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -28f), new Vector2(600f, 48f)),
                "", 28, true, UIStyle.Ink, TextAnchor.MiddleLeft);

            var done = UIFactory.Rect("Done", canvas, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -24f), new Vector2(208f, 64f));
            var bg = ui.RoundedImage(done, Color.clear, 16f);
            var icon = UIFactory.IconImage(UIFactory.Rect("Icon", done, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(32f, 32f)), icons.done, Color.white);
            var label = ui.Label(UIFactory.Rect("Label", done, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(68f, 0f), new Vector2(124f, 40f)),
                "완료", 26, true, Color.white, TextAnchor.MiddleLeft);
            doneButton = done.gameObject.AddComponent<IconButton>();
            doneButton.Init(bg, icon, label, "다 칠했으면 눌러서 보내요");
            doneButton.SetNormalColors(UIStyle.Accent, Color.white);
            doneButton.Clicked += OpenDialog;

            var status = UIFactory.Rect("Status", canvas, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-260f, -36f), new Vector2(150f, 40f));
            statusIcon = UIFactory.IconImage(UIFactory.Rect("Icon", status, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(28f, 28f)), icons.wifi, UIStyle.Good);
            statusText = ui.Label(UIFactory.Rect("Text", status, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(38f, 0f), new Vector2(112f, 32f)),
                "", 20, false, UIStyle.Quiet, TextAnchor.MiddleLeft);
        }

        void BuildToolbar(Transform canvas)
        {
            const float width = 104f, inner = 80f, pad = 12f;
            var bar = UIFactory.Rect("Toolbar", canvas, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(width, 588f));
            ui.RoundedImage(bar, UIStyle.Panel, 20f);
            float y = -pad;

            (PaintTool tool, Sprite icon, string label, string tip)[] items =
            {
                (PaintTool.Brush, icons.brush, "붓", "붓 (B)"),
                (PaintTool.Eraser, icons.eraser, "지우개", "지우개 (E)"),
                (PaintTool.Fill, icons.fill, "채우기", "부위 채우기 (G)"),
                (PaintTool.Picker, icons.picker, "스포이드", "색 가져오기 (I, Alt)"),
            };
            foreach (var item in items)
            {
                var button = ToolButton(bar, item.icon, item.label, item.tip, y, inner, 80f, 36f);
                var tool = item.tool;
                button.Clicked += () => tools.SelectTool(tool);
                toolButtons[tool] = button;
                y -= 80f + 8f;
            }

            y -= 8f;
            var divider = UIFactory.Rect("Divider", bar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(inner - 8f, 2f));
            divider.gameObject.AddComponent<Image>().color = UIStyle.Border;
            y -= 2f + 14f;

            sizeRow = UIFactory.Rect("Sizes", bar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(inner, 32f));
            float[] dots = { 8f, 14f, 22f };
            string[] tips = { "작게 (1)", "보통 (2)", "크게 (3)" };
            for (int i = 0; i < dots.Length; i++)
            {
                var rt = UIFactory.Rect("Size" + (i + 1), sizeRow, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * 28f, 0f), new Vector2(24f, 32f));
                var bg = ui.RoundedImage(rt, Color.clear, 8f);
                var dot = UIFactory.Rect("Dot", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(dots[i], dots[i]));
                var dotImage = ui.CircleImage(dot, UIStyle.Ink);
                dotImage.raycastTarget = false;
                var button = rt.gameObject.AddComponent<IconButton>();
                button.Init(bg, dotImage, null, tips[i]);
                int index = i;
                button.Clicked += () => tools.SetSize(index);
                sizeButtons.Add(button);
            }
            y -= 32f + 16f;

            undoButton = ToolButton(bar, icons.undo, "되돌리기", "되돌리기 (Ctrl+Z)", y, inner, 64f, 28f);
            undoButton.Clicked += controller.Undo;
            y -= 64f + 8f;
            redoButton = ToolButton(bar, icons.redo, "다시하기", "다시하기 (Ctrl+Y)", y, inner, 64f, 28f);
            redoButton.Clicked += controller.Redo;
            y -= 64f + pad;

            bar.sizeDelta = new Vector2(width, -y);
        }

        IconButton ToolButton(RectTransform bar, Sprite sprite, string text, string tip, float y, float width, float height, float iconSize)
        {
            var rt = UIFactory.Rect(text, bar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(width, height));
            var bg = ui.RoundedImage(rt, Color.clear, 14f);
            var icon = UIFactory.IconImage(UIFactory.Rect("Icon", rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(iconSize, iconSize)), sprite, UIStyle.Ink);
            var label = ui.Label(UIFactory.Rect("Label", rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(width, 22f)),
                text, 16, false, UIStyle.Ink);
            var button = rt.gameObject.AddComponent<IconButton>();
            button.Init(bg, icon, label, tip);
            return button;
        }

        void BuildPalette(Transform canvas)
        {
            const float swatch = 44f, gap = 8f, pad = 12f, current = 72f;
            float width = pad + current + 16f + 12 * swatch + 11 * gap + pad;
            float height = pad + 2 * swatch + gap + pad;
            var bar = UIFactory.Rect("Palette", canvas, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(width, height));
            ui.RoundedImage(bar, UIStyle.Panel, 20f);

            var currentRect = UIFactory.Rect("Current", bar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(pad, 0f), new Vector2(current, current));
            currentColor = ui.RoundedImage(currentRect, Color.white, 14f);
            currentColor.raycastTarget = false;
            ui.RoundedImage(UIFactory.Stretch("Outline", currentRect), UIStyle.Border, 14f, true).raycastTarget = false;

            for (int i = 0; i < Palette.Length; i++)
            {
                int col = i % 12, row = i / 12;
                var rt = UIFactory.Rect(Palette[i].name, bar, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(pad + current + 16f + col * (swatch + gap), -pad - row * (swatch + gap)), new Vector2(swatch, swatch));
                var color = UIStyle.Hex(Palette[i].rgb);
                var fill = ui.RoundedImage(rt, color, 10f);
                // 흰색처럼 밝은 칸도 경계가 보이도록 옅은 테두리
                ui.RoundedImage(UIFactory.Stretch("Edge", rt), UIStyle.Border, 10f, true).raycastTarget = false;
                var selected = UIFactory.Rect("Selected", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(swatch + 10f, swatch + 10f));
                var outline = ui.RoundedImage(selected, UIStyle.Ink, 14f, true);
                outline.raycastTarget = false;
                var button = rt.gameObject.AddComponent<IconButton>();
                button.Init(null, null, null, Palette[i].name);
                button.Clicked += () => tools.SetColor(color);
                fill.raycastTarget = true;
                swatches.Add((color, outline));
            }
        }

        void BuildViewControls(Transform canvas)
        {
            const float width = 248f;
            var hint = UIFactory.Rect("MouseHint", canvas, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(width, 140f));
            ui.RoundedImage(hint, UIStyle.Panel, 20f);
            HintRow(hint, icons.mouseLeft, "왼쪽: 칠하기", 0);
            HintRow(hint, icons.mouseRight, "오른쪽: 돌리기", 1);
            HintRow(hint, icons.mouseWheel, "휠: 크게 보기", 2);

            var reset = UIFactory.Rect("ViewReset", canvas, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 24f + 140f + 12f), new Vector2(width, 60f));
            ui.RoundedImage(reset, UIStyle.Panel, 16f);
            var bg = ui.RoundedImage(UIFactory.Stretch("Background", reset), Color.clear, 16f);
            var icon = UIFactory.IconImage(UIFactory.Rect("Icon", reset, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(30f, 30f)), icons.viewReset, UIStyle.Ink);
            var label = ui.Label(UIFactory.Rect("Label", reset, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(62f, 0f), new Vector2(170f, 32f)),
                "처음 보기 (Home)", 20, false, UIStyle.Ink, TextAnchor.MiddleLeft);
            var button = bg.gameObject.AddComponent<IconButton>();
            button.Init(bg, icon, label, "캐릭터를 정면으로 (Home)");
            button.Clicked += orbit.ResetView;
        }

        void HintRow(RectTransform panel, Sprite sprite, string text, int row)
        {
            float y = -16f - row * 38f;
            UIFactory.IconImage(UIFactory.Rect("Icon", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, y), new Vector2(30f, 30f)), sprite, UIStyle.Quiet);
            ui.Label(UIFactory.Rect("Text", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(62f, y), new Vector2(170f, 30f)),
                text, 20, false, UIStyle.Ink, TextAnchor.MiddleLeft);
        }

        // ───────────────────────── 상태 반영 ─────────────────────────

        void RefreshTools()
        {
            foreach (var pair in toolButtons)
                pair.Value.SetSelected(pair.Key == tools.Tool);

            bool sized = tools.Tool is PaintTool.Brush or PaintTool.Eraser;
            sizeRow.gameObject.SetActive(sized);
            for (int i = 0; i < sizeButtons.Count; i++)
                sizeButtons[i].SetSelected(i == tools.SizeIndex);

            currentColor.color = tools.Color;
            foreach (var (color, outline) in swatches)
                outline.enabled = SameColor(color, tools.Color);
        }

        void RefreshHistory()
        {
            undoButton.SetInteractable(controller.CanUndo);
            redoButton.SetInteractable(controller.CanRedo);
        }

        static bool SameColor(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f && Mathf.Abs(a.b - b.b) < 0.002f;

        // ───────────────────────── 완료 흐름 ─────────────────────────

        void OpenDialog()
        {
            SetPaintingEnabled(false);
            CharacterSnapshot.Capture(Camera.main, character, headlight, 180f, frontPreview);
            CharacterSnapshot.Capture(Camera.main, character, headlight, 0f, backPreview);
            dialog.Open();
        }

        void OnSubmit()
        {
            if (SubmitRequested == null)
            {
                dialog.ShowFailure("업로드는 아직 연결되지 않았어요.");
                return;
            }
            dialog.ShowSending();
            SubmitRequested.Invoke(dialog);
        }

        void SetPaintingEnabled(bool value)
        {
            controller.InputEnabled = value;
            tools.ShortcutsEnabled = value;
            orbit.enabled = value;
        }
    }
}
