using Microsoft.Azure.Kinect.BodyTracking;
using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Runtime.Serialization;

/// <summary>
/// Azure Kinect 한 명의 Body 데이터
/// - 3D / 2D 관절 위치, 회전, 신뢰도
/// - CopyFromBodyTrackingSdk()로 SDK 데이터를 복사해 사용
/// </summary>
[Serializable]
public struct Body : ISerializable
{
    public System.Numerics.Vector3[]    JointPositions3D;
    public System.Numerics.Vector2[]    JointPositions2D;
    public System.Numerics.Quaternion[] JointRotations;
    public JointConfidenceLevel[]       JointPrecisions;
    public int  Length;
    public uint Id;

    public Body(int maxJoints)
    {
        JointPositions3D = new System.Numerics.Vector3[maxJoints];
        JointPositions2D = new System.Numerics.Vector2[maxJoints];
        JointRotations   = new System.Numerics.Quaternion[maxJoints];
        JointPrecisions  = new JointConfidenceLevel[maxJoints];
        Length = 0;
        Id     = 0;
    }

    /// <summary>
    /// Azure Kinect Body Tracking SDK → 이 구조체로 복사
    /// 위치 단위: mm → m 변환 (/ 1000)
    /// </summary>
    public void CopyFromBodyTrackingSdk(Microsoft.Azure.Kinect.BodyTracking.Body body,
                                        Calibration sensorCalibration)
    {
        Id     = body.Id;
        Length = Microsoft.Azure.Kinect.BodyTracking.Skeleton.JointCount;

        for (int i = 0; i < Length; i++)
        {
            var joint = body.Skeleton.GetJoint(i);

            JointPositions3D[i] = joint.Position / 1000f;   // mm → m
            JointRotations[i]   = joint.Quaternion;
            JointPrecisions[i]  = joint.ConfidenceLevel;

            var pos2d = sensorCalibration.TransformTo2D(
                JointPositions3D[i],
                CalibrationDeviceType.Depth,
                CalibrationDeviceType.Depth);

            if (pos2d.HasValue)
                JointPositions2D[i] = pos2d.Value;
            else
            {
                JointPositions2D[i].X = Constants.Invalid2DCoordinate;
                JointPositions2D[i].Y = Constants.Invalid2DCoordinate;
            }
        }
    }

    // ── ISerializable (원시 데이터 로깅이 필요할 때 사용) ─────────────────
    public Body(SerializationInfo info, StreamingContext context)
    {
        float[] x3 = (float[])info.GetValue("P3X", typeof(float[]));
        float[] y3 = (float[])info.GetValue("P3Y", typeof(float[]));
        float[] z3 = (float[])info.GetValue("P3Z", typeof(float[]));
        JointPositions3D = new System.Numerics.Vector3[x3.Length];
        for (int i = 0; i < x3.Length; i++)
            JointPositions3D[i] = new System.Numerics.Vector3(x3[i], y3[i], z3[i]);

        float[] x2 = (float[])info.GetValue("P2X", typeof(float[]));
        float[] y2 = (float[])info.GetValue("P2Y", typeof(float[]));
        JointPositions2D = new System.Numerics.Vector2[x2.Length];
        for (int i = 0; i < x2.Length; i++)
            JointPositions2D[i] = new System.Numerics.Vector2(x2[i], y2[i]);

        float[] qx = (float[])info.GetValue("QX", typeof(float[]));
        float[] qy = (float[])info.GetValue("QY", typeof(float[]));
        float[] qz = (float[])info.GetValue("QZ", typeof(float[]));
        float[] qw = (float[])info.GetValue("QW", typeof(float[]));
        JointRotations = new System.Numerics.Quaternion[qx.Length];
        for (int i = 0; i < qx.Length; i++)
            JointRotations[i] = new System.Numerics.Quaternion(qx[i], qy[i], qz[i], qw[i]);

        uint[] cl = (uint[])info.GetValue("CL", typeof(uint[]));
        JointPrecisions = new JointConfidenceLevel[cl.Length];
        for (int i = 0; i < cl.Length; i++)
            JointPrecisions[i] = (JointConfidenceLevel)cl[i];

        Length = (int) info.GetValue("Length", typeof(int));
        Id     = (uint)info.GetValue("Id",     typeof(uint));
    }

    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        float[] x3 = new float[Length], y3 = new float[Length], z3 = new float[Length];
        for (int i = 0; i < Length; i++) { x3[i]=JointPositions3D[i].X; y3[i]=JointPositions3D[i].Y; z3[i]=JointPositions3D[i].Z; }
        info.AddValue("P3X", x3); info.AddValue("P3Y", y3); info.AddValue("P3Z", z3);

        float[] x2 = new float[Length], y2 = new float[Length];
        for (int i = 0; i < Length; i++) { x2[i]=JointPositions2D[i].X; y2[i]=JointPositions2D[i].Y; }
        info.AddValue("P2X", x2); info.AddValue("P2Y", y2);

        float[] qx=new float[Length],qy=new float[Length],qz=new float[Length],qw=new float[Length];
        for (int i=0;i<Length;i++){qx[i]=JointRotations[i].X;qy[i]=JointRotations[i].Y;qz[i]=JointRotations[i].Z;qw[i]=JointRotations[i].W;}
        info.AddValue("QX",qx);info.AddValue("QY",qy);info.AddValue("QZ",qz);info.AddValue("QW",qw);

        uint[] cl = new uint[Length];
        for (int i = 0; i < Length; i++) cl[i] = (uint)JointPrecisions[i];
        info.AddValue("CL", cl);
        info.AddValue("Length", Length);
        info.AddValue("Id", Id);
    }
}
