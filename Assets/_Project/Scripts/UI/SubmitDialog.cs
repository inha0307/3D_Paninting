using System;
using UnityEngine;
using UnityEngine.UI;

namespace Wonju.PaintApp.UI
{
    /// <summary>
    /// 완료 확인 창. 앞·뒤 미리보기와 [더 칠하기] [보내기]를 보여 주고, 보내는 중·성공·실패 상태를 표시합니다.
    /// 실제 업로드는 Submitted를 받는 쪽이 하고, 끝나면 ShowSuccess 또는 ShowFailure를 호출합니다.
    /// </summary>
    public class SubmitDialog : MonoBehaviour
    {
        const float SuccessCloseDelay = 2f;

        Text title, message;
        RawImage front, back;
        IconButton cancelButton, submitButton;
        float closeAt = -1f;

        public event Action Submitted;
        public event Action Closed;
        public bool IsOpen => gameObject.activeSelf;

        public static SubmitDialog Create(Transform canvas, UIFactory ui, RenderTexture frontTexture, RenderTexture backTexture)
        {
            var root = UIFactory.Stretch("SubmitDialog", canvas);
            root.gameObject.AddComponent<Image>().color = UIStyle.Dim; // 뒤를 막는 어두운 배경
            var dialog = root.gameObject.AddComponent<SubmitDialog>();

            var panel = UIFactory.Rect("Panel", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 600f));
            ui.RoundedImage(panel, Color.white, 24f);

            dialog.title = ui.Label(UIFactory.Rect("Title", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(640f, 44f)),
                "이대로 보낼까요?", 32, true, UIStyle.Ink);

            dialog.front = Preview(panel, ui, "앞", -150f, frontTexture);
            dialog.back = Preview(panel, ui, "뒤", 150f, backTexture);

            dialog.message = ui.Label(UIFactory.Rect("Message", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 112f), new Vector2(640f, 32f)),
                "", 22, false, UIStyle.Quiet);

            dialog.cancelButton = TextButton(panel, ui, "더 칠하기", -112f, false);
            dialog.submitButton = TextButton(panel, ui, "보내기", 112f, true);
            dialog.cancelButton.Clicked += dialog.Close;
            dialog.submitButton.Clicked += () => dialog.Submitted?.Invoke();

            root.gameObject.SetActive(false);
            return dialog;
        }

        public void Open()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            ShowConfirm();
        }

        public void Close()
        {
            closeAt = -1f;
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        public void ShowConfirm()
        {
            title.text = "이대로 보낼까요?";
            message.text = "보낸 뒤에도 계속 칠하고 다시 보낼 수 있어요.";
            message.color = UIStyle.Quiet;
            submitButton.SetLabel("보내기");
            SetButtons(true);
        }

        public void ShowSending()
        {
            message.text = "보내는 중…";
            message.color = UIStyle.Quiet;
            SetButtons(false);
        }

        public void ShowSuccess()
        {
            title.text = "보냈어요!";
            message.text = "VR에서 내 캐릭터를 만날 수 있어요.";
            message.color = UIStyle.Good;
            SetButtons(false);
            closeAt = Time.unscaledTime + SuccessCloseDelay;
        }

        public void ShowFailure(string reason)
        {
            title.text = "보내지 못했어요";
            message.text = reason;
            message.color = UIStyle.Bad;
            submitButton.SetLabel("다시 시도");
            SetButtons(true);
        }

        void Update()
        {
            if (closeAt > 0f && Time.unscaledTime >= closeAt) Close();
        }

        void SetButtons(bool interactable)
        {
            cancelButton.SetInteractable(interactable);
            submitButton.SetInteractable(interactable);
        }

        static RawImage Preview(RectTransform panel, UIFactory ui, string caption, float x, RenderTexture texture)
        {
            var frame = UIFactory.Rect(caption + "Preview", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, -96f), new Vector2(260f, 318f));
            ui.RoundedImage(frame, UIStyle.Hover, 16f);
            var imageRect = UIFactory.Rect("Image", frame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(244f, 270f));
            var image = imageRect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            ui.Label(UIFactory.Rect("Caption", frame, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(244f, 28f)),
                caption, 20, true, UIStyle.Quiet);
            return image;
        }

        static IconButton TextButton(RectTransform panel, UIFactory ui, string text, float x, bool primary)
        {
            var rt = UIFactory.Rect(text + "Button", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 32f), new Vector2(200f, 64f));
            var bg = ui.RoundedImage(rt, Color.clear, 16f);
            if (!primary)
            {
                var outline = UIFactory.Stretch("Outline", rt);
                ui.RoundedImage(outline, UIStyle.Border, 16f, true).raycastTarget = false;
            }
            var label = ui.Label(UIFactory.Stretch("Label", rt), text, 24, true, UIStyle.Ink);
            var button = rt.gameObject.AddComponent<IconButton>();
            button.Init(bg, null, label, null);
            if (primary) button.SetNormalColors(UIStyle.Accent, Color.white);
            return button;
        }
    }
}
