using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Azure Kinect RGB 이미지를 Unity UI RawImage에 실시간 표시
/// 테스트·디버그용 컴포넌트 — 전시 중에는 비활성화 가능
/// </summary>
public class RGBPreviewViewer : MonoBehaviour
{
    [Header("References")]
    public main_single mainController;
    public RawImage    previewImage;

    [Header("Settings")]
    public bool  enablePreview  = true;
    public float updateInterval = 0.1f;  // 초당 10프레임

    private Texture2D _tex;
    private float     _lastUpdate = 0f;

    void Start()
    {
        if (mainController == null)
            mainController = FindAnyObjectByType<main_single>();

        if (previewImage == null)
            Debug.LogWarning("[RGBPreviewViewer] previewImage가 연결되지 않았습니다.");
    }

    void Update()
    {
        if (!enablePreview || previewImage == null || mainController == null) return;
        if (Time.time - _lastUpdate < updateInterval) return;
        _lastUpdate = Time.time;

        var frame = mainController.lastFrameData;
        if (frame == null || frame.ColorImageSize <= 0) return;

        int    w         = frame.ColorImageWidth;
        int    h         = frame.ColorImageHeight;
        byte[] colorData = frame.ColorImage;

        // Texture 해상도 변경 시 재생성
        if (_tex == null || _tex.width != w || _tex.height != h)
        {
            if (_tex != null) Destroy(_tex);
            _tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            previewImage.texture = _tex;
            Debug.Log($"[RGBPreviewViewer] Texture 생성: {w}×{h}");
        }

        // BGRA32 → RGB24 변환 + 상하 반전
        byte[] rgb    = new byte[w * h * 3];
        int    stride = w * 4;

        for (int y = 0; y < h; y++)
        {
            int srcRow = (h - 1 - y) * stride;  // 상하 반전
            int dstRow = y * w * 3;

            for (int x = 0; x < w; x++)
            {
                int si = srcRow + x * 4;
                int di = dstRow + x * 3;

                if (si + 3 < colorData.Length)
                {
                    rgb[di]     = colorData[si + 2]; // R  ← BGRA[2]
                    rgb[di + 1] = colorData[si + 1]; // G  ← BGRA[1]
                    rgb[di + 2] = colorData[si];     // B  ← BGRA[0]
                }
            }
        }

        _tex.LoadRawTextureData(rgb);
        _tex.Apply();
    }

    void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
    }
}
