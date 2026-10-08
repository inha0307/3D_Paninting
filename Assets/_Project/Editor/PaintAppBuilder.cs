using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Wonju.PaintApp.EditorTools
{
    /// <summary>
    /// 학생용 PC 배포 폴더를 만듭니다: Builds/PaintApp/PaintApp.exe + config.json + 사용법.txt + licenses/.
    /// 폴더째 학생용 PC에 복사하고 config.json의 studentId만 PC마다 바꾸면 됩니다.
    /// </summary>
    public static class PaintAppBuilder
    {
        const string OutputFolder = "Builds/PaintApp";
        const string ExeName = "PaintApp.exe";
        const string Scene = "Assets/_Project/Scenes/PaintApp.unity";

        [MenuItem("Tools/Build/Paint App (Windows)")]
        static void BuildMenu()
        {
            var summary = Build();
            Debug.Log(summary);
            EditorUtility.RevealInFinder(Path.Combine(OutputFolder, ExeName));
        }

        public static string Build()
        {
            ApplyKioskPlayerSettings();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Path.Combine(OutputFolder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                return $"[PaintAppBuilder] 빌드 실패: {report.summary.result}, 오류 {report.summary.totalErrors}개";

            WriteDeployFiles();
            return $"[PaintAppBuilder] 빌드 성공: {Path.GetFullPath(OutputFolder)} ({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime.TotalSeconds:F0}초)";
        }

        static void ApplyKioskPlayerSettings()
        {
            PlayerSettings.companyName = "Wonju";
            PlayerSettings.productName = "PaintApp";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.forceSingleInstance = true;   // 실수로 두 번 실행해도 창이 하나만 뜸
            PlayerSettings.usePlayerLog = true;          // 문제 확인용 Player.log
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            AssetDatabase.SaveAssets();
        }

        static void WriteDeployFiles()
        {
            // Burst 디버그 정보는 배포하지 않습니다.
            foreach (var dir in Directory.GetDirectories(OutputFolder, "*_DoNotShip"))
                Directory.Delete(dir, true);

            // 예시 설정을 config.json으로 복사 (이미 있으면 덮어쓰지 않음)
            var config = Path.Combine(OutputFolder, "config.json");
            if (!File.Exists(config)) File.Copy("config.example.json", config);

            var licenses = Path.Combine(OutputFolder, "licenses");
            Directory.CreateDirectory(licenses);
            File.Copy("Assets/_Project/Fonts/Pretendard-LICENSE.txt", Path.Combine(licenses, "Pretendard-OFL.txt"), true);
            File.Copy("Art/Icons/LICENSE-lucide.txt", Path.Combine(licenses, "Lucide-ISC.txt"), true);

            File.WriteAllText(Path.Combine(OutputFolder, "사용법.txt"), Usage, new UTF8Encoding(true));
        }

        const string Usage =
@"3D 색칠하기 앱 (학생용 PC)

[설치]
1. 이 폴더(PaintApp)를 학생용 PC에 통째로 복사합니다.
2. config.json을 메모장으로 열어 두 값을 바꿉니다.
   - serverUrl : 운영 PC 주소. 예) http://192.168.0.10:8000
   - studentId : 이 PC의 학생 번호 = Quest 번호. 예) ""07""
3. PaintApp.exe를 실행합니다. 처음 실행 때 Windows 방화벽 창이 뜨면 [허용]을 누릅니다.

  config.json을 고치지 않고 바로가기에 실행 인수를 붙여도 됩니다.
  예) PaintApp.exe -studentId 07 -server http://192.168.0.10:8000
  점검할 때는 -windowed를 붙이면 전체화면 대신 창으로 뜹니다.
  예) PaintApp.exe -windowed -screen-fullscreen 0 -screen-width 1280 -screen-height 720

[화면 확인]
- 왼쪽 위에 '학생 07 · 색칠하기', 오른쪽 위에 초록색 '연결됨'이 보이면 준비 완료입니다.
- '연결 끊김'이면 운영 PC 서버가 켜져 있는지, 같은 네트워크인지 확인합니다.

[운영자 단축키]
- Ctrl + Shift + R : 회차 초기화 (그림, 되돌리기 기록, 도구, 시점을 처음 상태로. 서버에 보낸 그림은 남음)
- Ctrl + Shift + Q : 앱 종료

[문제가 생겼을 때]
- 보내기에 실패해도 그림은 Backups 폴더에 PNG로 저장됩니다. 운영 PC 서버로 직접 옮길 수 있습니다.
- 앱 기록: %USERPROFILE%\AppData\LocalLow\Wonju\PaintApp\Player.log
";
    }
}
