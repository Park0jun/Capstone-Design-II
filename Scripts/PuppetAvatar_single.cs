using System.Collections.Generic;
using UnityEngine;
using Microsoft.Azure.Kinect.BodyTracking;

/// <summary>
/// Retargets Azure Kinect body tracking data onto a Unity Humanoid avatar.
/// </summary>
public class PuppetAvatar_single : MonoBehaviour
{
    [Header("Kinect")]
    public TrackerHandler_single KinectDevice;

    [Header("Character Root")]
    public Transform CharacterRootTransform;
    public float OffsetY = 0f;
    public float OffsetZ = 0f;

    [Header("Mirror")]
    [Tooltip("Mirror left/right motion so the visitor and avatar face each other naturally.")]
    public bool enableMirrorMode = true;

    [Header("Position Tracking")]
    [Tooltip("Move the avatar from its placed root position using Kinect pelvis movement.")]
    public bool enablePositionTracking = true;
    [Tooltip("Scale for Kinect pelvis movement applied to the avatar.")]
    public float positionScale = 1f;
    [Tooltip("Keep the avatar rig's initial Hips offset from the placed root position.")]
    public bool preserveInitialHipsOffset = true;
    [Tooltip("If false, Kinect vertical pelvis movement will not change the avatar height.")]
    public bool trackVerticalPosition = false;

    [Header("Runtime (ReadOnly)")]
    public bool isInitialized = false;

    public bool IsRetargetingActive =>
        isInitialized && KinectDevice != null && KinectDevice.currentBodyTrackingID != 0;

    private Animator _animator;
    private Dictionary<JointId, Quaternion> _offsetMap;
    private readonly Dictionary<JointId, Queue<Quaternion>> _rotBuffer = new Dictionary<JointId, Queue<Quaternion>>();
    private const int BufferSize = 20;

    private Vector3 _initPelvisPos = Vector3.zero;
    private bool _initPosSet = false;
    private Vector3 _initialHipsOffset = Vector3.zero;

    private static readonly Dictionary<JointId, JointId> MirrorJointMap = new Dictionary<JointId, JointId>
    {
        { JointId.ClavicleLeft,  JointId.ClavicleRight  }, { JointId.ClavicleRight, JointId.ClavicleLeft  },
        { JointId.ShoulderLeft,  JointId.ShoulderRight  }, { JointId.ShoulderRight, JointId.ShoulderLeft  },
        { JointId.ElbowLeft,     JointId.ElbowRight     }, { JointId.ElbowRight,    JointId.ElbowLeft     },
        { JointId.WristLeft,     JointId.WristRight     }, { JointId.WristRight,    JointId.WristLeft     },
        { JointId.HandLeft,      JointId.HandRight      }, { JointId.HandRight,     JointId.HandLeft      },
        { JointId.HandTipLeft,   JointId.HandTipRight   }, { JointId.HandTipRight,  JointId.HandTipLeft   },
        { JointId.ThumbLeft,     JointId.ThumbRight     }, { JointId.ThumbRight,    JointId.ThumbLeft     },
        { JointId.HipLeft,       JointId.HipRight       }, { JointId.HipRight,      JointId.HipLeft       },
        { JointId.KneeLeft,      JointId.KneeRight      }, { JointId.KneeRight,     JointId.KneeLeft      },
        { JointId.AnkleLeft,     JointId.AnkleRight     }, { JointId.AnkleRight,    JointId.AnkleLeft     },
        { JointId.FootLeft,      JointId.FootRight      }, { JointId.FootRight,     JointId.FootLeft      },
        { JointId.EyeLeft,       JointId.EyeRight       }, { JointId.EyeRight,      JointId.EyeLeft       },
        { JointId.EarLeft,       JointId.EarRight       }, { JointId.EarRight,      JointId.EarLeft       },
    };

    private void Start()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null)
        {
            Debug.LogError($"[PuppetAvatar] {name}: Animator is missing.");
            enabled = false;
            return;
        }

        if (_animator.avatar == null || !_animator.avatar.isHuman)
        {
            Debug.LogWarning($"[PuppetAvatar] {name}: Humanoid avatar is missing. InitializeAvatar skipped.");
            return;
        }

        if (KinectDevice == null)
        {
            KinectDevice = FindAnyObjectByType<TrackerHandler_single>();
            if (KinectDevice == null)
                Debug.LogWarning($"[PuppetAvatar] {name}: TrackerHandler_single not found.");
            else
                Debug.Log($"[PuppetAvatar] {name}: TrackerHandler_single auto-connected.");
        }

        if (KinectDevice != null)
            InitializeAvatar();
    }

    public void InitializeAvatar()
    {
        if (isInitialized) return;

        _animator = _animator ?? GetComponent<Animator>();
        if (_animator == null || _animator.avatar == null || !_animator.avatar.isHuman)
        {
            Debug.LogError($"[PuppetAvatar] {name}: valid Humanoid avatar is required.");
            return;
        }

        if (CharacterRootTransform == null)
            CharacterRootTransform = transform;

        CaptureInitialHipsOffset();
        BuildOffsetMap();
        isInitialized = true;
        Debug.Log($"[PuppetAvatar] {name}: initialized (Mirror={enableMirrorMode}, PosTrk={enablePositionTracking}, TrackY={trackVerticalPosition})");
    }

    private void LateUpdate()
    {
        if (!isInitialized || KinectDevice == null || _animator == null) return;
        if (KinectDevice.absoluteJointRotations == null) return;

        if (!_initPosSet && KinectDevice.jointPositions != null)
        {
            _initPelvisPos = KinectDevice.jointPositions[(int)JointId.Pelvis];
            _initPosSet = true;
        }

        for (int j = 0; j < (int)JointId.Count; j++)
        {
            JointId jointId = (JointId)j;
            HumanBodyBones hbb = MapJoint(jointId);
            if (hbb == HumanBodyBones.LastBone || !_offsetMap.ContainsKey(jointId)) continue;

            Transform bone = _animator.GetBoneTransform(hbb);
            if (bone == null) continue;

            Quaternion rot = MirrorRot(KinectDevice.absoluteJointRotations[j]);
            Queue<Quaternion> buf = _rotBuffer[jointId];
            if (buf.Count >= BufferSize) buf.Dequeue();
            buf.Enqueue(rot);
            Quaternion smoothed = AverageQuaternion(buf);

            Quaternion off = _offsetMap[jointId];
            bone.rotation = off * Quaternion.Inverse(off) * smoothed * off;

            if (jointId == JointId.Pelvis)
                UpdatePelvisPosition(bone);
        }
    }

    private void UpdatePelvisPosition(Transform hipsBone)
    {
        Transform root = CharacterRootTransform != null ? CharacterRootTransform : transform;
        Vector3 basePos = root.position;
        Vector3 rigOffset = preserveInitialHipsOffset ? _initialHipsOffset : Vector3.zero;

        if (enablePositionTracking && KinectDevice.jointPositions != null && _initPosSet)
        {
            Vector3 delta = (KinectDevice.jointPositions[(int)JointId.Pelvis] - _initPelvisPos) * positionScale;
            if (enableMirrorMode) delta.x = -delta.x;

            float yDelta = trackVerticalPosition ? delta.y : 0f;
            hipsBone.position = basePos + rigOffset + new Vector3(delta.x, yDelta + OffsetY, delta.z - OffsetZ);
        }
        else
        {
            hipsBone.position = basePos + rigOffset + new Vector3(0f, OffsetY, -OffsetZ);
        }
    }

    public void ToggleMirrorMode()
    {
        enableMirrorMode = !enableMirrorMode;
        if (isInitialized) BuildOffsetMap();
    }

    public void ResetPositionTracking()
    {
        CaptureInitialHipsOffset();
        _initPosSet = false;
        _initPelvisPos = Vector3.zero;
    }

    public void ResetInitialization()
    {
        isInitialized = false;
        _offsetMap?.Clear();
        _rotBuffer.Clear();
    }

    private void CaptureInitialHipsOffset()
    {
        if (_animator == null) _animator = GetComponent<Animator>();
        Transform root = CharacterRootTransform != null ? CharacterRootTransform : transform;
        Transform hips = _animator != null ? _animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        _initialHipsOffset = hips != null && root != null ? hips.position - root.position : Vector3.zero;
    }

    private HumanBodyBones MapJoint(JointId joint)
    {
        JointId mapped = (enableMirrorMode && MirrorJointMap.TryGetValue(joint, out JointId mirror)) ? mirror : joint;
        return mapped switch
        {
            JointId.Pelvis        => HumanBodyBones.Hips,
            JointId.SpineNavel    => HumanBodyBones.Spine,
            JointId.SpineChest    => HumanBodyBones.Chest,
            JointId.Neck          => HumanBodyBones.Neck,
            JointId.Head          => HumanBodyBones.Head,
            JointId.HipLeft       => HumanBodyBones.LeftUpperLeg,
            JointId.KneeLeft      => HumanBodyBones.LeftLowerLeg,
            JointId.AnkleLeft     => HumanBodyBones.LeftFoot,
            JointId.FootLeft      => HumanBodyBones.LeftToes,
            JointId.HipRight      => HumanBodyBones.RightUpperLeg,
            JointId.KneeRight     => HumanBodyBones.RightLowerLeg,
            JointId.AnkleRight    => HumanBodyBones.RightFoot,
            JointId.FootRight     => HumanBodyBones.RightToes,
            JointId.ClavicleLeft  => HumanBodyBones.LeftShoulder,
            JointId.ShoulderLeft  => HumanBodyBones.LeftUpperArm,
            JointId.ElbowLeft     => HumanBodyBones.LeftLowerArm,
            JointId.WristLeft     => HumanBodyBones.LeftHand,
            JointId.ClavicleRight => HumanBodyBones.RightShoulder,
            JointId.ShoulderRight => HumanBodyBones.RightUpperArm,
            JointId.ElbowRight    => HumanBodyBones.RightLowerArm,
            JointId.WristRight    => HumanBodyBones.RightHand,
            _                     => HumanBodyBones.LastBone,
        };
    }

    private void BuildOffsetMap()
    {
        _offsetMap = new Dictionary<JointId, Quaternion>();
        _rotBuffer.Clear();
        Transform root = CharacterRootTransform != null ? CharacterRootTransform : transform;
        int count = 0;

        for (int i = 0; i < (int)JointId.Count; i++)
        {
            JointId jointId = (JointId)i;
            HumanBodyBones hbb = MapJoint(jointId);
            if (hbb == HumanBodyBones.LastBone) continue;

            Transform boneTf = _animator.GetBoneTransform(hbb);
            if (boneTf == null) continue;

            Quaternion abs = GetSkeletonBone(_animator, boneTf.name).rotation;
            Transform cur = boneTf;
            while (cur != null && !ReferenceEquals(cur, root))
            {
                cur = cur.parent;
                if (cur != null)
                    abs = GetSkeletonBone(_animator, cur.name).rotation * abs;
            }

            _offsetMap[jointId] = abs;
            _rotBuffer[jointId] = new Queue<Quaternion>(BufferSize);
            count++;
        }

        Debug.Log($"[PuppetAvatar] Offset map: {count} joints.");
    }

    private static SkeletonBone GetSkeletonBone(Animator animator, string boneName)
    {
        SkeletonBone[] skeleton = animator.avatar.humanDescription.skeleton;
        foreach (SkeletonBone sb in skeleton)
        {
            if (sb.name == boneName || sb.name == boneName + "(Clone)")
                return sb;
        }

        return new SkeletonBone();
    }

    private Quaternion MirrorRot(Quaternion q)
    {
        return enableMirrorMode ? new Quaternion(-q.x, q.y, q.z, -q.w) : q;
    }

    private static Quaternion AverageQuaternion(IEnumerable<Quaternion> qs)
    {
        List<Quaternion> arr = new List<Quaternion>(qs);
        if (arr.Count == 0) return Quaternion.identity;

        Quaternion avg = arr[0];
        for (int i = 1; i < arr.Count; i++)
            avg = Quaternion.Slerp(avg, arr[i], 1f / (i + 1f));

        return avg;
    }
}
