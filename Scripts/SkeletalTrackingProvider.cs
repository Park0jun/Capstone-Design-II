using Microsoft.Azure.Kinect.BodyTracking;
using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

/// <summary>
/// Azure Kinect 장치를 백그라운드 스레드에서 구동
/// Body Tracking + RGB + Depth 데이터를 수집해 메인 스레드에 전달
///
/// 전시 설정:
///   ColorResolution        : R720p  (1280 × 720)
///   DepthMode              : NFOV_Unbinned  (640 × 576)
///   FPS                    : 30
///   SynchronizedImagesOnly : true   (RGB-Depth 동기 캡처 보장)
///   ProcessingMode         : Cuda   (GPU 없으면 Cpu로 변경)
/// </summary>
public class SkeletalTrackingProvider : BackgroundDataProvider
{
    private bool     _readFirstFrame = false;
    private TimeSpan _initialTimestamp;

    // Depth → 그레이스케일 변환 기준 (mm). Inspector 노출이 필요하면 main_single에서 주입
    private const float MAX_DEPTH_MM = 5000f;

    public SkeletalTrackingProvider(int deviceId) : base(deviceId) { }

    protected override void RunBackgroundThreadAsync(int id, CancellationToken token)
    {
        try
        {
            Debug.Log("[SkeletalTrackingProvider] 백그라운드 스레드 시작");

            BackgroundData frameData = new BackgroundData();

            using Device device = Device.Open(id);

            device.StartCameras(new DeviceConfiguration
            {
                CameraFPS              = FPS.FPS30,
                ColorResolution        = ColorResolution.R720p,
                ColorFormat            = ImageFormat.ColorBGRA32,
                DepthMode              = DepthMode.NFOV_Unbinned,
                WiredSyncMode          = WiredSyncMode.Standalone,
                SynchronizedImagesOnly = true
            });

            Debug.Log($"[SkeletalTrackingProvider] 장치 열기 성공  SN={device.SerialNum}");

            var calibration = device.GetCalibration();

            using Tracker tracker = Tracker.Create(calibration, new TrackerConfiguration
            {
                ProcessingMode    = TrackerProcessingMode.Cuda,
                SensorOrientation = SensorOrientation.Default
            });

            Debug.Log("[SkeletalTrackingProvider] Body Tracker 생성 완료");

            while (!token.IsCancellationRequested)
            {
                // ── 캡처 ──────────────────────────────────────────────────
                using Capture capture = device.GetCapture();
                tracker.EnqueueCapture(capture);

                using Frame frame = tracker.PopResult(TimeSpan.Zero, throwOnTimeout: false);
                if (frame == null) continue;

                IsRunning = true;

                // ── Body 데이터 ───────────────────────────────────────────
                frameData.NumOfBodies = frame.NumberOfBodies;
                uint bodyCount = (uint)Math.Min((ulong)frame.NumberOfBodies, 6UL);
                for (uint i = 0; i < bodyCount; i++)
                {
                    try { frameData.Bodies[i].CopyFromBodyTrackingSdk(frame.GetBody(i), calibration); }
                    catch (Exception e) { Debug.LogWarning($"[SkeletalTrackingProvider] Body {i} 복사 실패: {e.Message}"); }
                }

                using Capture bodyCapture = frame.Capture;

                // ── Depth 이미지 (그레이스케일 RGB 변환, 상하 반전) ───────
                Image depthImg = bodyCapture.Depth;
                if (depthImg == null) { Debug.LogWarning("[SkeletalTrackingProvider] Depth 이미지 null"); continue; }

                if (!_readFirstFrame)
                {
                    _readFirstFrame   = true;
                    _initialTimestamp = depthImg.DeviceTimestamp;
                }

                frameData.TimestampInMs    = (float)(depthImg.DeviceTimestamp - _initialTimestamp).TotalMilliseconds;
                frameData.DepthImageWidth  = depthImg.WidthPixels;
                frameData.DepthImageHeight = depthImg.HeightPixels;
                frameData.DepthImageSize   = depthImg.WidthPixels * depthImg.HeightPixels * 3;

                var depthSpan  = MemoryMarshal.Cast<byte, ushort>(depthImg.Memory.Span);
                int pixelCount = depthImg.WidthPixels * depthImg.HeightPixels;
                int byteIdx    = 0;

                for (int it = pixelCount - 1; it >= 0; it--)
                {
                    byte b = (byte)(depthSpan[it] / MAX_DEPTH_MM * 255f);
                    frameData.DepthImage[byteIdx++] = b;
                    frameData.DepthImage[byteIdx++] = b;
                    frameData.DepthImage[byteIdx++] = b;
                }

                // ── RGB 이미지 (BGRA32 그대로 전달, RGBPreviewViewer에서 변환) ──
                Image colorImg = bodyCapture.Color;
                if (colorImg != null)
                {
                    frameData.ColorImageWidth  = colorImg.WidthPixels;
                    frameData.ColorImageHeight = colorImg.HeightPixels;
                    frameData.ColorImageSize   = colorImg.WidthPixels * colorImg.HeightPixels * 4;

                    var colorSpan = colorImg.Memory.Span;
                    int copyLen   = Math.Min(colorSpan.Length, frameData.ColorImage.Length);
                    colorSpan.Slice(0, copyLen).CopyTo(frameData.ColorImage.AsSpan(0, copyLen));
                }
                else
                {
                    frameData.ColorImageSize = 0;
                    Debug.LogWarning("[SkeletalTrackingProvider] Color 이미지 null");
                }

                // ── 메인 스레드로 전달 ────────────────────────────────────
                SetCurrentFrameData(ref frameData);
            }

            tracker.Dispose();
            device.Dispose();
            Debug.Log("[SkeletalTrackingProvider] 스레드 정상 종료");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SkeletalTrackingProvider] 예외: {e.Message}");
            token.ThrowIfCancellationRequested();
        }
    }
}
