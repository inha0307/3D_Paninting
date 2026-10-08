using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Wonju.PaintApp
{
    /// <summary>
    /// 텍스처 PNG를 운영 PC 서버(POST /api/texture)로 올립니다.
    /// 보내기 전에 로컬 백업을 남기고, 실패하면 간격을 늘려 가며 다시 시도합니다.
    /// </summary>
    public class TextureUploader
    {
        readonly AppConfig config;
        readonly int attempts;
        readonly int timeoutSeconds;

        public TextureUploader(AppConfig config, int attempts = 3, int timeoutSeconds = 10)
        {
            this.config = config;
            this.attempts = attempts;
            this.timeoutSeconds = timeoutSeconds;
        }

        public string LastBackupPath { get; private set; }

        /// <summary>업로드 코루틴. 끝나면 onDone(성공 여부, 실패 이유)을 호출합니다.</summary>
        public IEnumerator Upload(byte[] png, Action<bool, string> onDone)
        {
            SaveBackup(png);

            string error = null;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                using var request = UnityWebRequest.Post($"{config.serverUrl}/api/texture", new List<IMultipartFormSection>
                {
                    new MultipartFormDataSection("studentId", config.studentId),
                    new MultipartFormDataSection("characterId", config.characterId),
                    new MultipartFormFileSection("file", png, $"{config.studentId}.png", "image/png"),
                });
                request.timeout = timeoutSeconds;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[Upload] 학생 {config.studentId} 업로드 성공 ({png.Length / 1024} KB): {request.downloadHandler.text}");
                    onDone(true, null);
                    yield break;
                }

                error = request.responseCode > 0 ? $"HTTP {request.responseCode} {request.downloadHandler?.text}" : request.error;
                Debug.LogWarning($"[Upload] {attempt}/{attempts}번째 시도 실패: {error}");
                // 서버가 요청 자체를 거절(4xx)했으면 다시 보내도 같으므로 멈춥니다.
                if (request.responseCode is >= 400 and < 500) break;
                if (attempt < attempts) yield return new WaitForSecondsRealtime(attempt);
            }
            onDone(false, error);
        }

        void SaveBackup(byte[] png)
        {
            try
            {
                Directory.CreateDirectory(config.BackupPath);
                string id = string.IsNullOrEmpty(config.studentId) ? "unknown" : config.studentId;
                LastBackupPath = Path.Combine(config.BackupPath, $"student{id}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                File.WriteAllBytes(LastBackupPath, png);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Upload] 백업 저장 실패: {e.Message}");
            }
        }
    }

    /// <summary>서버 /api/health를 주기적으로 확인합니다.</summary>
    public class ServerStatus
    {
        readonly AppConfig config;

        public ServerStatus(AppConfig config) => this.config = config;

        public bool? Connected { get; private set; }
        public event Action<bool> Changed;

        public IEnumerator Poll()
        {
            while (true)
            {
                using (var request = UnityWebRequest.Get($"{config.serverUrl}/api/health"))
                {
                    request.timeout = 3;
                    yield return request.SendWebRequest();
                    bool ok = request.result == UnityWebRequest.Result.Success;
                    if (Connected != ok)
                    {
                        Connected = ok;
                        Changed?.Invoke(ok);
                    }
                }
                yield return new WaitForSecondsRealtime(Mathf.Max(1f, config.healthCheckSeconds));
            }
        }
    }
}
