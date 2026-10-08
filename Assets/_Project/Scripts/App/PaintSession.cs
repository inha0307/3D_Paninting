using UnityEngine;
using Wonju.PaintApp.UI;

namespace Wonju.PaintApp
{
    /// <summary>
    /// 앱 전체 흐름을 묶습니다: 설정을 읽고, 서버 연결 상태를 표시하고, 완료 창의 [보내기]를 PNG 업로드로 처리합니다.
    /// </summary>
    public class PaintSession : MonoBehaviour
    {
        [SerializeField] PaintCanvas canvas;
        [SerializeField] PaintUI ui;

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
        }

        void Start()
        {
            ui.SetStudent(config.HasValidStudentId ? config.studentId : null);
            ui.SubmitRequested += Submit;
            status.Changed += connected => ui.SetConnection(connected);
            StartCoroutine(status.Poll());
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
