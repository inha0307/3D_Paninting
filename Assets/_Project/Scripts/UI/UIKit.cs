using UnityEngine;
using UnityEngine.UI;

namespace Wonju.PaintApp.UI
{
    /// <summary>색칠 앱 UI의 색과 크기. 1920×1080 기준 픽셀입니다.</summary>
    public static class UIStyle
    {
        public static readonly Color Ink = Hex(0x1F2430);
        public static readonly Color Quiet = Hex(0x5B6475);
        public static readonly Color Accent = Hex(0x2F6FED);
        public static readonly Color Hover = Hex(0xE9EEF7);
        public static readonly Color Panel = new(1f, 1f, 1f, 0.94f);
        public static readonly Color Border = Hex(0xD7DCE5);
        public static readonly Color Good = Hex(0x2E9E5B);
        public static readonly Color Bad = Hex(0xD93A3A);
        public static readonly Color Dim = new(0.08f, 0.1f, 0.14f, 0.5f);
        public static readonly Color TooltipBg = new(0.12f, 0.14f, 0.19f, 0.95f);

        public const float DisabledAlpha = 0.4f;

        public static Color Hex(int rgb) =>
            new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    /// <summary>코드로 uGUI 요소를 만드는 도우미. 위치는 부모 기준 앵커·피벗·좌표·크기로 지정합니다.</summary>
    public sealed class UIFactory
    {
        public readonly Font Regular;
        public readonly Font SemiBold;
        public readonly Sprite Round;
        public readonly Sprite RoundOutline;
        public readonly Sprite Circle;

        public UIFactory(Font regular, Font semiBold, Sprite round, Sprite roundOutline, Sprite circle)
        {
            Regular = regular;
            SemiBold = semiBold;
            Round = round;
            RoundOutline = roundOutline;
            Circle = circle;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>부모를 가득 채우는 영역.</summary>
        public static RectTransform Stretch(string name, Transform parent)
        {
            var rt = Rect(name, parent, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            return rt;
        }

        /// <summary>둥근 사각형. radius는 화면 픽셀 기준 모서리 반지름입니다.</summary>
        public Image RoundedImage(RectTransform rt, Color color, float radius = 16f, bool outline = false)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = outline ? RoundOutline : Round;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 16f / Mathf.Max(1f, radius);
            image.color = color;
            return image;
        }

        public Image CircleImage(RectTransform rt, Color color)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = Circle;
            image.color = color;
            image.preserveAspect = true;
            return image;
        }

        public static Image IconImage(RectTransform rt, Sprite sprite, Color color)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        public Text Label(RectTransform rt, string text, int size, bool bold, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var label = rt.gameObject.AddComponent<Text>();
            label.font = bold ? SemiBold : Regular;
            label.fontSize = size;
            label.text = text;
            label.color = color;
            label.alignment = align;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }
    }
}
