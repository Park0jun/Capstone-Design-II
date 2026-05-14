using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class ImageGenClient : MonoBehaviour
{
    [Header("Server")]
    public string endpointUrl = "http://localhost:8000/generate";
    public bool sendRequests = true;

    [Header("Visitor")]
    public string faceImagePath = "";
    [Range(0f, 1f)] public float styleStrength = 0.7f;

    public void SendTrigger(string sceneId, string actionName)
    {
        if (!sendRequests || string.IsNullOrEmpty(endpointUrl))
        {
            Debug.Log($"[ImageGenClient] skipped: {sceneId}/{actionName}");
            return;
        }

        StartCoroutine(PostTrigger(sceneId, actionName));
    }

    private IEnumerator PostTrigger(string sceneId, string actionName)
    {
        string json = JsonUtility.ToJson(new Payload
        {
            sceneId = sceneId,
            actionName = actionName,
            faceImagePath = faceImagePath,
            styleStrength = styleStrength,
            timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        using var request = new UnityWebRequest(endpointUrl, UnityWebRequest.kHttpVerbPOST);
        byte[] body = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
            Debug.LogWarning($"[ImageGenClient] failed: {request.error}");
        else
            Debug.Log($"[ImageGenClient] sent: {sceneId}/{actionName}");
    }

    [System.Serializable]
    private struct Payload
    {
        public string sceneId;
        public string actionName;
        public string faceImagePath;
        public float styleStrength;
        public long timestamp;
    }
}
