using System.Collections.Generic;
using UnityEngine;
using Microsoft.Azure.Kinect.BodyTracking;

/// <summary>
/// 가장 가까운 1명을 힙 위치 기반으로 Lock 추적
/// - 처음 감지된 사람을 Lock
/// - 이후 매 프레임, 이전 힙 위치에 가장 가까운 Body를 계속 선택 (ID 변경 대응)
/// - absoluteJointRotations / jointPositions 를 매 프레임 갱신
///   → PuppetAvatar_single / SkeletonFeatureExtractor 가 이 값을 읽음
/// </summary>
public class TrackerHandler_single : MonoBehaviour
{
    [Header("Tracking Data (ReadOnly)")]
    public Quaternion[] absoluteJointRotations = new Quaternion[(int)JointId.Count];
    public Vector3[]    jointPositions          = new Vector3[(int)JointId.Count];
    public Vector3[]    modelJointPositions     = new Vector3[(int)JointId.Count];
    public ulong        currentBodyTrackingID   = 0;

    [Header("Debug")]
    public bool drawGizmos = true;

    // Joint 계층 구조
    public Dictionary<JointId, JointId> parentJointMap { get; private set; }
    private Dictionary<JointId, Quaternion> _basisMap;

    private static readonly Quaternion Y_180 = new Quaternion(0f, 1f, 0f, 0f);

    // Lock 상태
    private bool    _hasLock         = false;
    private int     _lockedBodyIndex = -1;
    private Vector3 _lockedHipPos    = Vector3.zero;

    // ── 초기화 ────────────────────────────────────────────────────────────────
    void Awake() => BuildJointMaps();

    private void BuildJointMaps()
    {
        parentJointMap = new Dictionary<JointId, JointId>();
        _basisMap      = new Dictionary<JointId, Quaternion>();

        Vector3 xp = Vector3.right, yp = Vector3.up, zp = Vector3.forward;

        // ── Basis 회전 ────────────────────────────────────────────────────────
        Quaternion spineB  = Quaternion.LookRotation(xp,  -zp);
        Quaternion lHipB   = Quaternion.LookRotation(xp,  -zp);
        Quaternion rHipB   = Quaternion.LookRotation(xp,   zp);
        Quaternion lArmB   = Quaternion.LookRotation(yp,  -zp);
        Quaternion rArmB   = Quaternion.LookRotation(-yp,  zp);
        Quaternion lHandB  = Quaternion.LookRotation(-zp, -yp);
        Quaternion rHandB  = Quaternion.identity;
        Quaternion lFootB  = Quaternion.LookRotation(xp,   yp);
        Quaternion rFootB  = Quaternion.LookRotation(xp,  -yp);

        // ── 부모 관계 ──────────────────────────────────────────────────────────
        // 척추
        parentJointMap[JointId.Pelvis]       = JointId.Count; // 루트
        parentJointMap[JointId.SpineNavel]   = JointId.Pelvis;
        parentJointMap[JointId.SpineChest]   = JointId.SpineNavel;
        parentJointMap[JointId.Neck]         = JointId.SpineChest;
        parentJointMap[JointId.Head]         = JointId.Neck;
        // 왼팔
        parentJointMap[JointId.ClavicleLeft]  = JointId.SpineChest;
        parentJointMap[JointId.ShoulderLeft]  = JointId.ClavicleLeft;
        parentJointMap[JointId.ElbowLeft]     = JointId.ShoulderLeft;
        parentJointMap[JointId.WristLeft]     = JointId.ElbowLeft;
        parentJointMap[JointId.HandLeft]      = JointId.WristLeft;
        parentJointMap[JointId.HandTipLeft]   = JointId.HandLeft;
        parentJointMap[JointId.ThumbLeft]     = JointId.HandLeft;
        // 오른팔
        parentJointMap[JointId.ClavicleRight] = JointId.SpineChest;
        parentJointMap[JointId.ShoulderRight] = JointId.ClavicleRight;
        parentJointMap[JointId.ElbowRight]    = JointId.ShoulderRight;
        parentJointMap[JointId.WristRight]    = JointId.ElbowRight;
        parentJointMap[JointId.HandRight]     = JointId.WristRight;
        parentJointMap[JointId.HandTipRight]  = JointId.HandRight;
        parentJointMap[JointId.ThumbRight]    = JointId.HandRight;
        // 왼다리
        parentJointMap[JointId.HipLeft]       = JointId.SpineNavel;
        parentJointMap[JointId.KneeLeft]      = JointId.HipLeft;
        parentJointMap[JointId.AnkleLeft]     = JointId.KneeLeft;
        parentJointMap[JointId.FootLeft]      = JointId.AnkleLeft;
        // 오른다리
        parentJointMap[JointId.HipRight]      = JointId.SpineNavel;
        parentJointMap[JointId.KneeRight]     = JointId.HipRight;
        parentJointMap[JointId.AnkleRight]    = JointId.KneeRight;
        parentJointMap[JointId.FootRight]     = JointId.AnkleRight;
        // 얼굴
        parentJointMap[JointId.Nose]          = JointId.Head;
        parentJointMap[JointId.EyeLeft]       = JointId.Head;
        parentJointMap[JointId.EarLeft]       = JointId.Head;
        parentJointMap[JointId.EyeRight]      = JointId.Head;
        parentJointMap[JointId.EarRight]      = JointId.Head;

        // ── Basis 매핑 ────────────────────────────────────────────────────────
        foreach (JointId j in new[]{ JointId.Pelvis,JointId.SpineNavel,JointId.SpineChest,JointId.Neck,JointId.Head,
                                     JointId.Nose,JointId.EyeLeft,JointId.EarLeft,JointId.EyeRight,JointId.EarRight })
            _basisMap[j] = spineB;

        foreach (JointId j in new[]{ JointId.ClavicleLeft,JointId.ShoulderLeft,JointId.ElbowLeft,JointId.ThumbLeft })
            _basisMap[j] = lArmB;
        _basisMap[JointId.WristLeft]    = lHandB;
        _basisMap[JointId.HandLeft]     = lHandB;
        _basisMap[JointId.HandTipLeft]  = lHandB;

        foreach (JointId j in new[]{ JointId.ClavicleRight,JointId.ShoulderRight,JointId.ElbowRight,JointId.ThumbRight })
            _basisMap[j] = rArmB;
        _basisMap[JointId.WristRight]   = rHandB;
        _basisMap[JointId.HandRight]    = rHandB;
        _basisMap[JointId.HandTipRight] = rHandB;

        foreach (JointId j in new[]{ JointId.HipLeft,JointId.KneeLeft,JointId.AnkleLeft })
            _basisMap[j] = lHipB;
        _basisMap[JointId.FootLeft] = lFootB;

        foreach (JointId j in new[]{ JointId.HipRight,JointId.KneeRight,JointId.AnkleRight })
            _basisMap[j] = rHipB;
        _basisMap[JointId.FootRight] = rFootB;
    }

    // ── 퍼블릭 API ────────────────────────────────────────────────────────────

    /// <summary>main_single.Update()에서 매 프레임 호출</summary>
    public void UpdateTracker(BackgroundData data)
    {
        if (data.NumOfBodies == 0)
        {
            currentBodyTrackingID = 0;
            _hasLock              = false;
            _lockedBodyIndex      = -1;
            return;
        }

        // 처음 감지 → 가장 가까운 사람 Lock
        if (!_hasLock)
        {
            _lockedBodyIndex = FindClosestBodyIndex(data);
            _lockedHipPos    = GetHipPosition(data, _lockedBodyIndex);
            _hasLock         = true;
            Debug.Log($"[TrackerHandler] Lock: bodyIndex={_lockedBodyIndex}");
        }
        else
        {
            // Lock 유지 — 힙 위치가 가장 비슷한 body 선택 (ID 불연속 대응)
            _lockedBodyIndex = FindMostSimilarBodyIndex(data, _lockedHipPos);
            _lockedHipPos    = GetHipPosition(data, _lockedBodyIndex);
        }

        Body skeleton = data.Bodies[_lockedBodyIndex];
        currentBodyTrackingID = skeleton.Id;

        // 관절 데이터 추출
        for (int j = 0; j < (int)JointId.Count; j++)
        {
            var q    = skeleton.JointRotations[j];
            var basis = _basisMap[(JointId)j];
            absoluteJointRotations[j] = Y_180
                * new Quaternion(q.X, q.Y, q.Z, q.W)
                * Quaternion.Inverse(basis);

            var p = skeleton.JointPositions3D[j];
            modelJointPositions[j] = new Vector3(p.X, p.Y, p.Z);
            jointPositions[j] = new Vector3(p.X, -p.Y, p.Z); // Y축 반전
        }
    }

    // ── 내부 헬퍼 ─────────────────────────────────────────────────────────────

    private int FindClosestBodyIndex(BackgroundData data)
    {
        int   best    = 0;
        float minDist = float.MaxValue;
        for (int i = 0; i < (int)data.NumOfBodies; i++)
        {
            float d = GetHipPosition(data, i).magnitude;
            if (d < minDist) { minDist = d; best = i; }
        }
        return best;
    }

    private int FindMostSimilarBodyIndex(BackgroundData data, Vector3 prevHip)
    {
        int   best    = 0;
        float minDiff = float.MaxValue;
        for (int i = 0; i < (int)data.NumOfBodies; i++)
        {
            float d = Vector3.Distance(GetHipPosition(data, i), prevHip);
            if (d < minDiff) { minDiff = d; best = i; }
        }
        return best;
    }

    private Vector3 GetHipPosition(BackgroundData data, int idx)
    {
        var p = data.Bodies[idx].JointPositions3D[(int)JointId.Pelvis];
        return new Vector3(p.X, p.Y, p.Z);
    }

    // ── Gizmo (씬 뷰 디버그) ──────────────────────────────────────────────────
    void OnDrawGizmos()
    {
        if (!drawGizmos || currentBodyTrackingID == 0 || jointPositions == null) return;

        Gizmos.color = Color.green;
        for (int j = 0; j < (int)JointId.Count; j++)
        {
            Vector3 jp = jointPositions[j];
            Gizmos.DrawSphere(jp, 0.02f);

            if (parentJointMap != null &&
                parentJointMap.TryGetValue((JointId)j, out JointId parent) &&
                parent != JointId.Count)
                Gizmos.DrawLine(jp, jointPositions[(int)parent]);
        }
    }
}
