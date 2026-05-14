using System;
using System.Runtime.Serialization;

/// <summary>
/// 백그라운드 스레드 → 메인 스레드 데이터 컨테이너
/// RGB(BGRA32) + Depth + Body 데이터를 한 번에 담아 스레드 안전하게 교환
///
/// 기본 크기 기준 (Azure Kinect NFOV Unbinned + R720p):
///   Depth : 640 × 576 × 3 bytes  (그레이스케일 RGB 표현)
///   Color : 1280 × 720 × 4 bytes (BGRA32)
///   Body  : 최대 6명 × 32 관절
/// </summary>
[Serializable]
public class BackgroundData : ISerializable
{
    public float TimestampInMs   { get; set; }

    // ── Depth ─────────────────────────────────────────────────────────────────
    public byte[] DepthImage     { get; set; }
    public int    DepthImageWidth  { get; set; }
    public int    DepthImageHeight { get; set; }
    public int    DepthImageSize   { get; set; }

    // ── Color (BGRA32 원본) ───────────────────────────────────────────────────
    public byte[] ColorImage     { get; set; }
    public int    ColorImageWidth  { get; set; }
    public int    ColorImageHeight { get; set; }
    public int    ColorImageSize   { get; set; }

    // ── Body ──────────────────────────────────────────────────────────────────
    public ulong  NumOfBodies { get; set; }
    public Body[] Bodies      { get; set; }

    public BackgroundData()
    {
        DepthImage = new byte[640 * 576 * 3];
        ColorImage = new byte[1280 * 720 * 4];

        const int MAX_BODIES = 6;
        const int MAX_JOINTS = 32;
        Bodies = new Body[MAX_BODIES];
        for (int i = 0; i < MAX_BODIES; i++)
            Bodies[i] = new Body(MAX_JOINTS);
    }

    // ── ISerializable ─────────────────────────────────────────────────────────
    public BackgroundData(SerializationInfo info, StreamingContext context)
    {
        TimestampInMs    = (float) info.GetValue("TimestampInMs",    typeof(float));
        DepthImageWidth  = (int)   info.GetValue("DepthImageWidth",  typeof(int));
        DepthImageHeight = (int)   info.GetValue("DepthImageHeight", typeof(int));
        DepthImageSize   = (int)   info.GetValue("DepthImageSize",   typeof(int));
        ColorImageWidth  = (int)   info.GetValue("ColorImageWidth",  typeof(int));
        ColorImageHeight = (int)   info.GetValue("ColorImageHeight", typeof(int));
        ColorImageSize   = (int)   info.GetValue("ColorImageSize",   typeof(int));
        NumOfBodies      = (ulong) info.GetValue("NumOfBodies",      typeof(ulong));
        Bodies           = (Body[])info.GetValue("Bodies",           typeof(Body[]));
        DepthImage       = (byte[])info.GetValue("DepthImage",       typeof(byte[]));
        ColorImage       = (byte[])info.GetValue("ColorImage",       typeof(byte[]));
    }

    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue("TimestampInMs",    TimestampInMs);
        info.AddValue("DepthImageWidth",  DepthImageWidth);
        info.AddValue("DepthImageHeight", DepthImageHeight);
        info.AddValue("DepthImageSize",   DepthImageSize);
        info.AddValue("ColorImageWidth",  ColorImageWidth);
        info.AddValue("ColorImageHeight", ColorImageHeight);
        info.AddValue("ColorImageSize",   ColorImageSize);
        info.AddValue("NumOfBodies",      NumOfBodies);

        Body[] validBodies = new Body[NumOfBodies];
        for (int i = 0; i < (int)NumOfBodies; i++) validBodies[i] = Bodies[i];
        info.AddValue("Bodies", validBodies, typeof(Body[]));

        byte[] vDepth = new byte[DepthImageSize];
        Array.Copy(DepthImage, vDepth, DepthImageSize);
        info.AddValue("DepthImage", vDepth, typeof(byte[]));

        byte[] vColor = new byte[ColorImageSize];
        Array.Copy(ColorImage, vColor, ColorImageSize);
        info.AddValue("ColorImage", vColor, typeof(byte[]));
    }
}
