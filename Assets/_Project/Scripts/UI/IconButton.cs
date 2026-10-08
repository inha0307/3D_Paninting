using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Wonju.PaintApp.UI
{
    /// <summary>
    /// 아이콘(+라벨) 버튼. 선택되면 강조색 배경에 흰 아이콘, 마우스를 올리면 연한 배경, 못 쓰면 흐리게 표시합니다.
    /// 마우스를 0.5초 올려 두면 도움말(예: "붓 (B)")을 띄웁니다.
    /// </summary>
    public class IconButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        Image background;
        Image icon;
        Text label;
        CanvasGroup group;
        string tooltip;
        bool selected, hovered, interactable = true;
        Color normalForeground = UIStyle.Ink;
        Color normalBackground = Color.clear;

        public event Action Clicked;

        public void Init(Image background, Image icon, Text label, string tooltip)
        {
            this.background = background;
            this.icon = icon;
            this.label = label;
            this.tooltip = tooltip;
            group = gameObject.AddComponent<CanvasGroup>();
            Refresh();
        }

        /// <summary>선택되지 않았을 때의 색. 완료 버튼처럼 늘 강조색인 버튼에 씁니다.</summary>
        public void SetNormalColors(Color background, Color foreground)
        {
            normalBackground = background;
            normalForeground = foreground;
            Refresh();
        }

        public void SetSelected(bool value)
        {
            if (selected == value) return;
            selected = value;
            Refresh();
        }

        public void SetInteractable(bool value)
        {
            if (interactable == value) return;
            interactable = value;
            Refresh();
        }

        public void SetLabel(string text)
        {
            if (label != null) label.text = text;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            Refresh();
            if (!string.IsNullOrEmpty(tooltip)) Tooltip.Schedule(tooltip, (RectTransform)transform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            Refresh();
            Tooltip.Cancel((RectTransform)transform);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !interactable) return;
            Tooltip.Cancel((RectTransform)transform);
            Clicked?.Invoke();
        }

        void OnDisable()
        {
            hovered = false;
            Refresh();
        }

        void Refresh()
        {
            if (background == null) return;
            bool hover = hovered && interactable && !selected;
            background.color = selected ? UIStyle.Accent : hover ? Blend(normalBackground, UIStyle.Hover) : normalBackground;
            var foreground = selected ? Color.white : normalForeground;
            if (icon != null) icon.color = foreground;
            if (label != null) label.color = foreground;
            group.alpha = interactable ? 1f : UIStyle.DisabledAlpha;
        }

        // 투명 배경이면 연한 강조색, 색이 있는 배경이면 조금 밝게
        static Color Blend(Color normal, Color hover) =>
            normal.a < 0.01f ? hover : Color.Lerp(normal, Color.white, 0.15f);
    }

    /// <summary>화면에 하나뿐인 도움말 말풍선. 버튼 오른쪽(공간이 없으면 위쪽)에 뜹니다.</summary>
    public class Tooltip : MonoBehaviour
    {
        const float Delay = 0.5f;
        static Tooltip instance;

        RectTransform rect;
        Text text;
        RectTransform pendingTarget;
        string pendingText;
        float showAt;

        // 컴포넌트는 늘 켜진 호스트에 두고, 말풍선(rect)만 켜고 끕니다.
        public static void Create(Transform canvas, UIFactory ui)
        {
            var host = UIFactory.Stretch("TooltipHost", canvas);
            var rt = UIFactory.Rect("Tooltip", host, Vector2.zero, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(120f, 40f));
            ui.RoundedImage(rt, UIStyle.TooltipBg, 10f).raycastTarget = false;
            var label = UIFactory.Stretch("Text", rt);
            instance = host.gameObject.AddComponent<Tooltip>();
            instance.rect = rt;
            instance.text = ui.Label(label, "", 18, false, Color.white);
            rt.gameObject.SetActive(false);
            host.SetAsLastSibling();
        }

        public static void Schedule(string text, RectTransform target)
        {
            if (instance == null) return;
            instance.pendingText = text;
            instance.pendingTarget = target;
            instance.showAt = Time.unscaledTime + Delay;
            instance.rect.gameObject.SetActive(false);
        }

        public static void Cancel(RectTransform target)
        {
            if (instance == null || instance.pendingTarget != target) return;
            instance.pendingTarget = null;
            instance.rect.gameObject.SetActive(false);
        }

        void Update()
        {
            if (pendingTarget == null || Time.unscaledTime < showAt || rect.gameObject.activeSelf) return;
            text.text = pendingText;
            rect.sizeDelta = new Vector2(text.preferredWidth + 28f, 40f);
            rect.gameObject.SetActive(true);
            Place();
        }

        void Place()
        {
            var canvasRect = (RectTransform)rect.parent; // 화면 전체를 덮는 호스트
            var corners = new Vector3[4];
            pendingTarget.GetWorldCorners(corners);
            // 대상의 오른쪽 가운데 → 캔버스 좌표
            Vector2 right = canvasRect.InverseTransformPoint((corners[2] + corners[3]) * 0.5f);
            Vector2 top = canvasRect.InverseTransformPoint((corners[1] + corners[2]) * 0.5f);
            var size = canvasRect.rect.size;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            if (right.x + 12f + rect.sizeDelta.x < size.x * 0.5f - 8f)
            {
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = right + new Vector2(12f, 0f);
            }
            else
            {
                rect.pivot = new Vector2(0.5f, 0f);
                float half = rect.sizeDelta.x * 0.5f;
                rect.anchoredPosition = new Vector2(Mathf.Clamp(top.x, -size.x * 0.5f + half + 8f, size.x * 0.5f - half - 8f), top.y + 10f);
            }
        }
    }
}
