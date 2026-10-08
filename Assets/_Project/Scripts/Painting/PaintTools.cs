using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Wonju.PaintApp
{
    public enum PaintTool
    {
        Brush,
        Eraser,
        Fill,
        Picker,
    }

    /// <summary>
    /// 현재 도구·색·붓 크기와 도구 단축키를 관리합니다. UI와 PaintController가 이 상태를 함께 봅니다.
    /// 단축키는 포토샵 관례를 따릅니다: B 붓, E 지우개, G 채우기, I 스포이드(Alt를 누르는 동안 임시), 1·2·3과 [ ] 크기.
    /// </summary>
    public class PaintTools : MonoBehaviour
    {
        [SerializeField] PaintTool tool = PaintTool.Brush;
        [SerializeField] Color color = new(0.898f, 0.224f, 0.208f); // 팔레트의 빨강 #E53935
        [Tooltip("붓·지우개 반지름 (m), 작게·보통·크게")]
        [SerializeField] float[] sizes = { 0.015f, 0.03f, 0.06f };
        [SerializeField] int sizeIndex = 1;

        PaintTool beforePicker = PaintTool.Brush;
        bool altPicker;

        readonly InputAction brushKey = new("Brush", InputActionType.Button, "<Keyboard>/b");
        readonly InputAction eraserKey = new("Eraser", InputActionType.Button, "<Keyboard>/e");
        readonly InputAction fillKey = new("Fill", InputActionType.Button, "<Keyboard>/g");
        readonly InputAction pickerKey = new("Picker", InputActionType.Button, "<Keyboard>/i");
        readonly InputAction altKey = new("AltPicker", InputActionType.Button);
        readonly InputAction size1Key = new("Size1", InputActionType.Button, "<Keyboard>/1");
        readonly InputAction size2Key = new("Size2", InputActionType.Button, "<Keyboard>/2");
        readonly InputAction size3Key = new("Size3", InputActionType.Button, "<Keyboard>/3");
        readonly InputAction smallerKey = new("Smaller", InputActionType.Button, "<Keyboard>/leftBracket");
        readonly InputAction biggerKey = new("Bigger", InputActionType.Button, "<Keyboard>/rightBracket");

        /// <summary>도구·색·크기 중 하나라도 바뀌면 호출됩니다.</summary>
        public event Action Changed;

        /// <summary>선택된 도구. Alt를 누르는 동안에는 Picker를 돌려줍니다.</summary>
        public PaintTool Tool => altPicker ? PaintTool.Picker : tool;
        public PaintTool SelectedTool => tool;
        public Color Color => color;
        public int SizeIndex => sizeIndex;
        public int SizeCount => sizes.Length;
        public float Radius => sizes[sizeIndex];
        public bool ShortcutsEnabled { get; set; } = true;

        void Awake()
        {
            altKey.AddBinding("<Keyboard>/leftAlt");
            altKey.AddBinding("<Keyboard>/rightAlt");
        }

        void OnEnable()
        {
            foreach (var a in Actions()) a.Enable();
        }

        void OnDisable()
        {
            foreach (var a in Actions()) a.Disable();
            SetAltPicker(false);
        }

        void Update()
        {
            if (!ShortcutsEnabled || IsCtrlHeld())
            {
                SetAltPicker(false);
                return;
            }
            if (brushKey.WasPressedThisFrame()) SelectTool(PaintTool.Brush);
            if (eraserKey.WasPressedThisFrame()) SelectTool(PaintTool.Eraser);
            if (fillKey.WasPressedThisFrame()) SelectTool(PaintTool.Fill);
            if (pickerKey.WasPressedThisFrame()) SelectTool(PaintTool.Picker);
            if (size1Key.WasPressedThisFrame()) SetSize(0);
            if (size2Key.WasPressedThisFrame()) SetSize(1);
            if (size3Key.WasPressedThisFrame()) SetSize(2);
            if (smallerKey.WasPressedThisFrame()) SetSize(sizeIndex - 1);
            if (biggerKey.WasPressedThisFrame()) SetSize(sizeIndex + 1);
            SetAltPicker(altKey.IsPressed());
        }

        public void SelectTool(PaintTool value)
        {
            if (value == PaintTool.Picker && tool != PaintTool.Picker)
                beforePicker = tool;
            if (tool == value) return;
            tool = value;
            Changed?.Invoke();
        }

        public void SetColor(Color value)
        {
            value.a = 1f;
            if (color == value) return;
            color = value;
            Changed?.Invoke();
        }

        public void SetSize(int index)
        {
            index = Mathf.Clamp(index, 0, sizes.Length - 1);
            if (sizeIndex == index) return;
            sizeIndex = index;
            Changed?.Invoke();
        }

        /// <summary>스포이드로 색을 집은 뒤 호출합니다. 선택해서 쓰던 스포이드면 직전 도구로 돌아갑니다.</summary>
        public void FinishPicking(Color picked)
        {
            SetColor(picked);
            if (tool == PaintTool.Picker)
                SelectTool(beforePicker == PaintTool.Picker ? PaintTool.Brush : beforePicker);
        }

        void SetAltPicker(bool value)
        {
            if (altPicker == value) return;
            altPicker = value;
            Changed?.Invoke();
        }

        static bool IsCtrlHeld() => Keyboard.current != null && Keyboard.current.ctrlKey.isPressed;

        InputAction[] Actions() => new[]
        {
            brushKey, eraserKey, fillKey, pickerKey, altKey, size1Key, size2Key, size3Key, smallerKey, biggerKey,
        };
    }
}
