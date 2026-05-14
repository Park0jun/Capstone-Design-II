using System.Collections.Generic;
using UnityEngine;
using Microsoft.Azure.Kinect.BodyTracking;

/// <summary>
/// PuppetAvatar_single??LateUpdate ?댄썑 由ы?寃잜똿??蹂??꾩튂瑜??쎌뼱
/// ?뺢퇋?붾맂 ?쇱쿂 踰≫꽣瑜??щ씪?대뵫 ?덈룄?곕줈 愿由?///
/// ?쇱쿂 愿???쒖꽌 (Python FEATURE_JOINTS = [2,5,6,7,12,13,14]):
///   [0] Chest  [1] LeftUpperArm  [2] LeftLowerArm  [3] LeftHand
///   [4] RightUpperArm  [5] RightLowerArm  [6] RightHand
///   ??shape per window: (windowSize 횞 21)
/// </summary>
public class SkeletonFeatureExtractor : MonoBehaviour
{
    [Header("References")]
    public PuppetAvatar_single puppet;

    [Header("Tracker Reference")]
    [Tooltip("TrackerHandler used by ActionRecognizer to read raw joint positions.")]
    public TrackerHandler_single trackerHandler;

    [Header("Window Settings")]
    [Tooltip("Sliding window size. Keep this aligned with the training window.")]
    public int windowSize = 30;

    [Header("Timer Settings")]
    [Tooltip("Seconds to wait after body detection before inference starts.")]
    public float warmupSeconds = 2f;

    // ?? ?곹깭 ??????????????????????????????????????????????????????????????????
    public enum State { Untracked, Warming, Active }

    [Header("Runtime Status (ReadOnly)")]
    [SerializeField] private State _state            = State.Untracked;
    [SerializeField] private float _warmupTimer      = 0f;
    [SerializeField] private int   _frameBufferCount = 0;

    public State CurrentState => _state;

    // ?? ?대깽?? ?덈룄?곌? 梨꾩썙吏??뚮쭏??諛쒗뻾 (float[] shape = windowSize * 21) ??
    public System.Action<float[]> OnWindowReady;

    // ?? ?쇱쿂 愿???뺤쓽 ????????????????????????????????????????????????????????
    private static readonly HumanBodyBones[] FEATURE_BONES =
    {
        HumanBodyBones.Chest,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.RightHand,
    };

    private const HumanBodyBones HIPS_BONE  = HumanBodyBones.Hips;
    private const HumanBodyBones L_SHOULDER = HumanBodyBones.LeftUpperArm;
    private const HumanBodyBones R_SHOULDER = HumanBodyBones.RightUpperArm;
    private const int FEATURE_JOINTS        = 7;
    private const int FEATURE_DIM           = FEATURE_JOINTS * 3; // 21

    private Queue<float[]> _rawQueue;
    private Queue<float>   _shoulderQueue;
    private Animator       _animator;

    // ?? Unity ?앸챸二쇨린 ????????????????????????????????????????????????????????

    void Awake()
    {
        _rawQueue      = new Queue<float[]>(windowSize + 1);
        _shoulderQueue = new Queue<float>(windowSize + 1);
    }

    void Start()
    {
        ResolveReferences();

        if (puppet == null) { Debug.LogError("[SkeletonFE] PuppetAvatar_single not found."); enabled = false; return; }

        _animator = puppet.GetComponent<Animator>();
        if (_animator == null) { Debug.LogError("[SkeletonFE] Animator not found."); enabled = false; }
    }

    void LateUpdate()
    {
        switch (_state)
        {
            case State.Untracked:
                break;

            case State.Warming:
                _warmupTimer += Time.deltaTime;
                if (_warmupTimer >= warmupSeconds)
                {
                    _state = State.Active;
                    Debug.Log("[SkeletonFE] Warmup complete. Inference can start.");
                }
                break;

            case State.Active:
                if (!puppet.IsRetargetingActive) { OnBodyLost(); return; }
                CollectFrame();
                break;
        }
    }

    // ?? ?몃? API (main_single?먯꽌 ?몄텧) ??????????????????????????????????????

    public void OnBodyDetected()
    {
        ResolveReferences();
        if (_state != State.Untracked) return;
        _state       = State.Warming;
        _warmupTimer = 0f;
        _rawQueue.Clear();
        _shoulderQueue.Clear();
        _frameBufferCount = 0;
        Debug.Log($"[SkeletonFE] Body detected. Warmup for {warmupSeconds}s.");
    }

    public void OnBodyLost()
    {
        _state       = State.Untracked;
        _warmupTimer = 0f;
        _rawQueue.Clear();
        _shoulderQueue.Clear();
        _frameBufferCount = 0;
        Debug.Log("[SkeletonFE] Body lost. Reset feature buffer.");
    }

    // ?? ActionRecognizer 釉뚮┸吏 ???????????????????????????????????????????????

    /// <summary>ActionRecognizer媛 32愿???먮낯 ?곗씠?곗뿉 ?묎렐?섍린 ?꾪븳 釉뚮┸吏</summary>
    public TrackerHandler_single GetTrackerHandler()
    {
        ResolveReferences();
        return trackerHandler;
    }

    /// <summary>?꾩옱 body媛 ?몃옒??以묒씤吏 ?щ? (ActionRecognizer?먯꽌 ?ъ슜)</summary>
    public bool IsBodyTracked => _state == State.Active;

    public void SetTrackerHandler(TrackerHandler_single handler)
    {
        trackerHandler = handler;
        if (puppet != null && puppet.KinectDevice == null)
            puppet.KinectDevice = handler;
    }

    private void ResolveReferences()
    {
        if (puppet == null)
            puppet = GetComponent<PuppetAvatar_single>() ?? FindAnyObjectByType<PuppetAvatar_single>();

        if (trackerHandler == null && puppet != null && puppet.KinectDevice != null)
            trackerHandler = puppet.KinectDevice;

        if (trackerHandler == null)
            trackerHandler = FindAnyObjectByType<TrackerHandler_single>();

        if (puppet != null && puppet.KinectDevice == null && trackerHandler != null)
            puppet.KinectDevice = trackerHandler;
    }

    // ?? ?꾨젅???섏쭛 ???????????????????????????????????????????????????????????

    private void CollectFrame()
    {
        if (_animator == null) return;

        Transform hipsT = _animator.GetBoneTransform(HIPS_BONE);
        Transform lshT  = _animator.GetBoneTransform(L_SHOULDER);
        Transform rshT  = _animator.GetBoneTransform(R_SHOULDER);
        if (hipsT == null || lshT == null || rshT == null) return;

        Vector3 hipsPos = hipsT.position;

        float[] raw = new float[FEATURE_DIM];
        for (int i = 0; i < FEATURE_BONES.Length; i++)
        {
            Transform bt = _animator.GetBoneTransform(FEATURE_BONES[i]);
            Vector3 rel  = bt != null ? bt.position - hipsPos : Vector3.zero;
            raw[i * 3]     = rel.x;
            raw[i * 3 + 1] = rel.y;
            raw[i * 3 + 2] = rel.z;
        }

        float sw = Mathf.Max(Vector3.Distance(lshT.position, rshT.position), 0.1f);

        _rawQueue.Enqueue(raw);
        _shoulderQueue.Enqueue(sw);
        if (_rawQueue.Count > windowSize) { _rawQueue.Dequeue(); _shoulderQueue.Dequeue(); }

        _frameBufferCount = _rawQueue.Count;

        if (_rawQueue.Count == windowSize)
            OnWindowReady?.Invoke(BuildWindow());
    }

    /// <summary>
    /// 2?④퀎: ?덈룄???꾩껜 ?닿묠 ?덈퉬 ?됯퇏?쇰줈 ?ㅼ????뺢퇋??    /// (Python: scale = shoulder_width.mean())
    /// </summary>
    private float[] BuildWindow()
    {
        float total = 0f;
        foreach (float w in _shoulderQueue) total += w;
        float scale = Mathf.Max(total / _shoulderQueue.Count, 0.1f);

        float[] result = new float[windowSize * FEATURE_DIM];
        int idx = 0;
        foreach (float[] frame in _rawQueue)
            for (int j = 0; j < FEATURE_DIM; j++)
                result[idx++] = frame[j] / scale;

        return result;
    }
}
