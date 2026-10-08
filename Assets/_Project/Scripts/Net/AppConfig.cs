using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Wonju.PaintApp
{
    /// <summary>
    /// 학생용 PC마다 다른 설정. 실행 파일 옆 config.json에서 읽고, 실행 인수(-studentId 07, -server http://...)로 덮어쓸 수 있습니다.
    /// 에디터에서는 프로젝트 폴더의 config.json을 읽습니다. 파일이 없으면 기본값을 씁니다.
    /// </summary>
    [Serializable]
    public class AppConfig
    {
        public const string FileName = "config.json";
        static readonly Regex IdPattern = new("^[A-Za-z0-9_-]{1,32}$"); // 서버(main.py)의 ID_PATTERN과 같음

        public string serverUrl = "http://127.0.0.1:8000";
        public string studentId = "";
        public string characterId = "base";
        public float healthCheckSeconds = 10f;
        public string backupFolder = "Backups";

        /// <summary>exe 옆 폴더 (에디터에서는 프로젝트 폴더).</summary>
        public static string AppFolder => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public string BackupPath => Path.IsPathRooted(backupFolder) ? backupFolder : Path.Combine(AppFolder, backupFolder);
        public bool HasValidStudentId => IdPattern.IsMatch(studentId ?? "");

        public static AppConfig Load()
        {
            var config = new AppConfig();
            var path = Path.Combine(AppFolder, FileName);
            if (File.Exists(path))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(path), config);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AppConfig] {path}를 읽지 못했습니다. 기본값을 씁니다. {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[AppConfig] {path}가 없어 기본값을 씁니다.");
            }

            config.ApplyCommandLine(Environment.GetCommandLineArgs());
            config.serverUrl = config.serverUrl.TrimEnd('/');
            if (!config.HasValidStudentId)
                Debug.LogWarning($"[AppConfig] 학생 번호 '{config.studentId}'가 비었거나 형식이 맞지 않습니다 (영문·숫자·_·- 1~32자).");
            return config;
        }

        void ApplyCommandLine(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "-studentId": studentId = args[i + 1]; break;
                    case "-server": serverUrl = args[i + 1]; break;
                    case "-characterId": characterId = args[i + 1]; break;
                }
            }
        }
    }
}
