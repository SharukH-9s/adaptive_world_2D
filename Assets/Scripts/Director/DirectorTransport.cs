using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Executes UnityWebRequest operations for the Adaptive Director service.
    /// Extracted from <see cref="AdaptiveDirectorClient"/> (Architecture Improvement Plan, Phase 5).
    /// </summary>
    public class DirectorTransport
    {
        public string ApiBaseUrl { get; set; } = "http://127.0.0.1:8001";

        public DirectorTransport(string apiBaseUrl = "http://127.0.0.1:8001")
        {
            ApiBaseUrl = apiBaseUrl;
        }

        /// <summary>
        /// Sends a batch of telemetry events via POST to /api/telemetry/batch.
        /// </summary>
        public IEnumerator PostTelemetryBatch(string jsonPayload, Action<string> onSuccess, Action<string> onError)
        {
            string url = ApiBaseUrl + "/api/telemetry/batch";
            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(req.downloadHandler.text);
                }
                else
                {
                    onError?.Invoke(req.error);
                }
            }
        }

        /// <summary>
        /// Solves and fetches a Mission 2 manifest via POST to /api/mission/solve.
        /// </summary>
        public IEnumerator PostMissionSolve(int seed, Action<string> onSuccess, Action<string> onError)
        {
            string url = ApiBaseUrl + "/api/mission/solve";
            string body = "{\"seed\":" + seed + "}";

            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(req.downloadHandler.text);
                }
                else
                {
                    onError?.Invoke(req.error);
                }
            }
        }

        /// <summary>
        /// Resets the active session on the backend via POST to /api/reset.
        /// </summary>
        public IEnumerator PostReset(Action onSuccess, Action<string> onError)
        {
            string url = ApiBaseUrl + "/api/reset";
            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes("{}");
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke();
                }
                else
                {
                    onError?.Invoke(req.error);
                }
            }
        }
    }
}
