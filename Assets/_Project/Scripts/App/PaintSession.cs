using UnityEngine;
using UnityEngine.InputSystem;
using Wonju.PaintApp.UI;

namespace Wonju.PaintApp
{
    /// <summary>
    /// 앱 전체 흐름을 묶습니다: 설정을 읽고, 서버 연결 상태를 표시하고, 완료 창의 [보내기]를 PNG 업로드로 처리합니다.
    /// 운영자 단축키: Ctrl+Shift+R 회차 초기화, Ctrl+Shift+Q 앱 종료. 학생이 실수로 누르기 어렵도록 세 키 조합만 씁니다.
    /// </summary>
    public class PaintSession : MonoBehaviour
    {
        [SerializeField] PaintCanvas canvas;
        [SerializeField] PaintController controller;
        [SerializeField] PaintTools tools;
        [SerializeField] OrbitCamera orbit;
        [SerializeField] PaintUI ui;

        readonly InputAction resetKey = new("ResetRound", InputActionType.Button, "<Keyboard>/r");
        readonly InputAction quitKey = new("Quit", InputActionType.Button, "<Keyboard>/q");

        AppConfig config;
        TextureUploader uploader;
        ServerStatus status;
        bool uploading;

        public AppConfig Config => config;

        void Awake()
        {
            config = AppConfig.Load();
            uploader = new TextureUploader(config);
            status = new ServerStatus(config);

            if (!Application.isEditor && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-windowed") < 0)
            {
                // 키오스크: 창 테두리 없는 전체화면, 모니터 해상도 그대로 (-windowed로 실행하면 창 모드 유지)
                var display = Screen.currentResolution;
                Screen.SetResolution(display.width, display.height, FullScreenMode.FullScreenWindow);
            }
        }

        void OnEnable()
        {
            resetKey.Enable();
            quitKey.Enable();
        }

        void OnDisable()
        {
            resetKey.Disable();
            quitKey.Disable();
        }

        void Start()
        {
            ui.SetStudent(config.HasValidStudentId ? config.Id.ToString() : null);
            ui.SubmitRequested += Submit;
            status.Changed += connected => ui.SetConnection(connected);
            StartCoroutine(status.Poll());
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.ctrlKey.isPressed || !keyboard.shiftKey.isPressed) return;
            if (resetKey.WasPressedThisFrame()) ResetRound();
            if (quitKey.WasPressedThisFrame()) Quit();
        }

        /// <summary>다음 학생을 위해 캔버스·되돌리기·도구·시점·완료 버튼을 처음 상태로 되돌립니다. 서버의 기존 업로드는 그대로 둡니다.</summary>
        public void ResetRound()
        {
            if (uploading) return;
            controller.ResetSession();
            tools.ResetToDefaults();
            orbit.ResetView();
            ui.ResetRound();
            Debug.Log("[PaintSession] 회차 초기화");
        }

        void Quit()
        {
            Debug.Log("[PaintSession] 운영자 종료");
            Application.Quit();
        }

        void Submit(SubmitDialog dialog)
        {
            if (uploading) return;
            if (!config.HasValidStudentId)
            {
                dialog.ShowFailure("이 PC의 학생 번호가 설정되지 않았어요. 선생님을 불러 주세요.");
                return;
            }
            uploading = true;
            var png = canvas.EncodePng();
            StartCoroutine(uploader.Upload(png, (ok, error) =>
            {
                uploading = false;
                if (ok)
                {
                    ui.MarkSubmitted();
                    ui.SetConnection(true);
                    dialog.ShowSuccess();
                }
                else
                {
                    Debug.LogWarning($"[PaintSession] 업로드 실패, 백업: {uploader.LastBackupPath} ({error})");
                    dialog.ShowFailure("연결이 안 돼요. 선생님을 불러 주세요.");
                }
            }));
        }
    }
}
