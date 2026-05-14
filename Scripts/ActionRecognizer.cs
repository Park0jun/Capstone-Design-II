using UnityEngine;
using Unity.InferenceEngine;

/// <summary>
/// ?щ퀎 ?됰룞 ?몄떇湲?///
/// [??遺꾨━ 援ъ“ ?꾪솚 ??蹂寃쎌젏]
///   - DontDestroyOnLoad: BootScene??諛곗튂, ???꾪솚?먮룄 ?좎?
///   - featureExtractor: public?쇰줈 ?좎?
///     ????濡쒕뱶 ??SceneLoader / StorySceneManager媛 ???ъ쓽 寃껋쑝濡??ъ뿰寃?///   - LoadScene(): 湲곗〈怨??숈씪, ???꾪솚留덈떎 ?몄텧??///
/// ?낅젰 : "skeleton_sequence"  shape (1, 3, 60, 7)
/// 異쒕젰 : "action_logits"      shape (1, 2)  [classA, classB]
/// </summary>
public class ActionRecognizer : MonoBehaviour
{
    // ?? ?낅젰 洹쒓꺽 ?곸닔 ????????????????????????????????????????????????????
    private const int WINDOW_FRAMES = 60;
    private const int NUM_JOINTS    = 7;
    private const int NUM_CHANNELS  = 3;

    private static readonly int[] FEATURE_JOINTS = { 2, 5, 6, 7, 12, 13, 14 };
    private const int SPINE_CHEST_IDX = 0;
    private const int SHOULDER_L_IDX  = 1;
    private const int SHOULDER_R_IDX  = 4;
    private const int WRIST_R_IDX     = 6;

    // ?? Inspector ?????????????????????????????????????????????????????????
    [Header("Initial Config")]
    [Tooltip("Config loaded at startup. Scene changes replace it through LoadScene().")]
    public SceneActionConfig initialConfig;

    [Header("Inference")]
    public int inferenceEveryNFrames = 2;

    [Header("Debug Log")]
    public bool showInferenceLog = true;
    public bool showBufferLog = true;
    [Tooltip("Minimum seconds between ActionRecognizer status logs.")]
    public float inferenceLogIntervalSeconds = 1f;

    [Header("References")]
    [Tooltip("Scene-specific SkeletonFeatureExtractor. Reconnected after scene load.")]
    public SkeletonFeatureExtractor featureExtractor;
    public ActionDispatcher         dispatcher;

    [Header("Runtime Status (ReadOnly)")]
    [SerializeField] private string _currentScene      = "-";
    [SerializeField] private string _lastAction        = "-";
    [SerializeField] private float  _lastConfidenceA   = 0f;
    [SerializeField] private float  _lastConfidenceB   = 0f;
    [SerializeField] private string _triggerStateLabel = "IDLE";
    [SerializeField] private float  _cooldownRemaining = 0f;

    // ?? ?꾩옱 ???ㅼ젙 ??????????????????????????????????????????????????????
    private SceneActionConfig _config;

    // ?? Inference Engine ??????????????????????????????????????????????????
    private Model  _model;
    private Worker _worker;

    // ?? ?꾨젅??踰꾪띁 ???????????????????????????????????????????????????????
    private Vector3[][] _frameBuffer;
    private int         _bufferHead   = 0;
    private int         _bufferCount  = 0;
    private int         _frameCounter = 0;
    private float       _lastInferenceLogTime = -999f;
    private float       _lastBufferLogTime = -999f;

    // ?? ?곹깭 癒몄떊 ?????????????????????????????????????????????????????????
    private enum TriggerState { Idle, Raised, Cooldown }
    private TriggerState _state        = TriggerState.Idle;
    private float        _cooldownTimer = 0f;

    // ?? Unity ?앸챸二쇨린 ????????????????????????????????????????????????????

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // featureExtractor??BootScene???놁쓣 ???덉쓬 ??泥?留???濡쒕뱶 ???ъ뿰寃곕맖
        if (featureExtractor == null)
            featureExtractor = FindAnyObjectByType<SkeletonFeatureExtractor>();
        if (dispatcher == null)
            dispatcher = FindAnyObjectByType<ActionDispatcher>();

        _frameBuffer = new Vector3[WINDOW_FRAMES][];
        for (int i = 0; i < WINDOW_FRAMES; i++)
            _frameBuffer[i] = new Vector3[32];

        if (initialConfig != null)
            LoadScene(initialConfig);
        else
            Debug.LogError("[ActionRecognizer] initialConfig is missing. Assign it in the Inspector.");
    }

    void Update()
    {
        // 荑⑤떎????대㉧
        if (_state == TriggerState.Cooldown)
        {
            _cooldownTimer    += Time.deltaTime;
            float cd           = _config != null ? _config.cooldownSeconds : 2f;
            _cooldownRemaining = Mathf.Max(0f, cd - _cooldownTimer);
            if (_cooldownTimer >= cd)
            {
                _cooldownTimer = 0f; _cooldownRemaining = 0f;
                _state = TriggerState.Idle; _triggerStateLabel = "IDLE";
            }
        }

        // featureExtractor媛 ?ъ뿰寃곕릺吏 ?딆? ???꾪솚 吏곹썑??異붾줎 ?ㅽ궢
        if (featureExtractor == null || !featureExtractor.IsBodyTracked)
        {
            LogBufferStatusIfNeeded("waiting for tracked body");
            return;
        }

        var tracker = featureExtractor.GetTrackerHandler();
        Vector3[] modelJoints = tracker != null && tracker.modelJointPositions != null
            ? tracker.modelJointPositions
            : tracker?.jointPositions;

        if (tracker == null || modelJoints == null || tracker.currentBodyTrackingID == 0)
        {
            LogBufferStatusIfNeeded($"tracker/joints not ready (tracker={(tracker != null)}, joints={(modelJoints != null)}, bodyId={(tracker != null ? tracker.currentBodyTrackingID : 0)})");
            return;
        }

        // 留?踰꾪띁 ?곸옱
        int slot = _bufferHead % WINDOW_FRAMES;
        System.Array.Copy(modelJoints, _frameBuffer[slot],
                          Mathf.Min(32, modelJoints.Length));
        _bufferHead++;
        _bufferCount = Mathf.Min(_bufferCount + 1, WINDOW_FRAMES);

        if (++_frameCounter % inferenceEveryNFrames != 0) return;
        if (_bufferCount < WINDOW_FRAMES)
        {
            LogBufferStatusIfNeeded($"buffering {_bufferCount}/{WINDOW_FRAMES}");
            return;
        }
        if (_worker == null)
        {
            LogBufferStatusIfNeeded("model worker missing");
            return;
        }

        RunInference();
    }

    void OnDestroy() => _worker?.Dispose();

    // ?? ???꾪솚 API ???????????????????????????????????????????????????????

    /// <summary>
    /// ???꾪솚 ??SceneLoader?먯꽌 ?몄텧.
    /// 紐⑤뜽 援먯껜 + 踰꾪띁 珥덇린??+ ?곹깭 由ъ뀑????踰덉뿉 泥섎━.
    /// featureExtractor????濡쒕뱶 ?꾨즺 ??蹂꾨룄濡??ъ뿰寃곕맖.
    /// </summary>
    public void LoadScene(SceneActionConfig config)
    {
        if (config == null) { Debug.LogError("[ActionRecognizer] config is null"); return; }

        _worker?.Dispose();
        _config       = config;
        _model        = null;
        _worker       = null;
        if (config.modelAsset != null)
        {
            _model  = ModelLoader.Load(config.modelAsset);
            _worker = new Worker(_model, BackendType.CPU);
        }
        else
        {
            Debug.LogWarning($"[ActionRecognizer] modelAsset is empty: {config.sceneName}");
        }
        _currentScene = config.sceneName;

        _bufferHead  = 0;
        _bufferCount = 0;
        _state       = TriggerState.Idle;
        _triggerStateLabel = "IDLE";

        featureExtractor = null;

        Debug.Log($"[ActionRecognizer] Loaded: {config.sceneName} | " +
                  $"Model: {(config.modelAsset != null ? config.modelAsset.name : "none")} | " +
                  $"Labels: {config.classA}" +
                  (config.singleClass ? " (single)" : $" / {config.classB}") +
                  $" | Collider: {config.useColliderTrigger}");
    }

    // ?? Collider ?몃━嫄?荑⑤떎??怨듭쑀 ??????????????????????????????????????

    /// <summary>
    /// PushColliderHandler媛 異⑸룎 媛먯? ???몄텧.
    /// AR 紐⑤뜽怨?荑⑤떎?댁쓣 怨듭쑀??以묐났 ?몃━嫄?諛⑹?.
    /// </summary>
    public void NotifyColliderTrigger()
    {
        if (_state == TriggerState.Cooldown) return;
        _state             = TriggerState.Cooldown;
        _triggerStateLabel = "COOLDOWN (Collider)";
        _cooldownTimer     = 0f;
    }

    // ?? ?꾩쿂由?????????????????????????????????????????????????????????????

    private float[] Preprocess(out Vector3[] last20avg)
    {
        Vector3[][] frames = new Vector3[WINDOW_FRAMES][];
        for (int t = 0; t < WINDOW_FRAMES; t++)
        {
            int src = (_bufferHead - WINDOW_FRAMES + t + WINDOW_FRAMES * 100) % WINDOW_FRAMES;
            frames[t] = new Vector3[NUM_JOINTS];
            for (int j = 0; j < NUM_JOINTS; j++)
                frames[t][j] = _frameBuffer[src][FEATURE_JOINTS[j]];
        }

        for (int t = 0; t < WINDOW_FRAMES; t++)
        {
            Vector3 chest = frames[t][SPINE_CHEST_IDX];
            for (int j = 0; j < NUM_JOINTS; j++)
                frames[t][j] -= chest;
        }

        float scaleSum = 0f;
        for (int t = 0; t < WINDOW_FRAMES; t++)
            scaleSum += Vector3.Distance(frames[t][SHOULDER_L_IDX], frames[t][SHOULDER_R_IDX]);
        float scale = Mathf.Max(scaleSum / WINDOW_FRAMES, 1e-6f);
        for (int t = 0; t < WINDOW_FRAMES; t++)
            for (int j = 0; j < NUM_JOINTS; j++)
                frames[t][j] /= scale;

        last20avg = new Vector3[NUM_JOINTS];
        for (int t = WINDOW_FRAMES - 20; t < WINDOW_FRAMES; t++)
            for (int j = 0; j < NUM_JOINTS; j++)
                last20avg[j] += frames[t][j];
        for (int j = 0; j < NUM_JOINTS; j++)
            last20avg[j] /= 20f;

        float[] tensor = new float[NUM_CHANNELS * WINDOW_FRAMES * NUM_JOINTS];
        for (int c = 0; c < NUM_CHANNELS; c++)
            for (int t = 0; t < WINDOW_FRAMES; t++)
                for (int j = 0; j < NUM_JOINTS; j++)
                {
                    int idx = c * (WINDOW_FRAMES * NUM_JOINTS) + t * NUM_JOINTS + j;
                    tensor[idx] = c == 0 ? frames[t][j].x
                                : c == 1 ? frames[t][j].y
                                :          frames[t][j].z;
                }
        return tensor;
    }

    // ?? 異붾줎 ??????????????????????????????????????????????????????????????

    private void RunInference()
    {
        float[] inputData = Preprocess(out Vector3[] last20avg);
        var shape = new TensorShape(1, NUM_CHANNELS, WINDOW_FRAMES, NUM_JOINTS);

        using var inputTensor = new Tensor<float>(shape, inputData);
        _worker.SetInput("skeleton_sequence", inputTensor);
        _worker.Schedule();

        using Tensor<float> outPeek = _worker.PeekOutput("action_logits") as Tensor<float>;
        using Tensor<float> output  = outPeek.ReadbackAndClone();

        float pA = Mathf.Exp(output[0]);
        float pB = Mathf.Exp(output[1]);
        float s  = pA + pB;
        pA /= s; pB /= s;

        _lastConfidenceA = pA;
        _lastConfidenceB = pB;

        string detectedAction = "unknown";
        float  topConf        = 0f;

        if (_config.singleClass)
        {
            if (pA >= _config.confidenceThreshold)
            { detectedAction = _config.classA; topConf = pA; }
        }
        else
        {
            if (pA >= _config.confidenceThreshold && pA >= pB)
            { detectedAction = _config.classA; topConf = pA; }
            else if (pB >= _config.confidenceThreshold && pB > pA)
            { detectedAction = _config.classB; topConf = pB; }
        }

        _lastAction = detectedAction;

        LogInferenceIfNeeded(pA, pB, detectedAction);

        ProcessTrigger(detectedAction, topConf, last20avg);
    }

    private void LogBufferStatusIfNeeded(string message)
    {
        if (!showBufferLog) return;
        if (Time.time - _lastBufferLogTime < inferenceLogIntervalSeconds) return;

        _lastBufferLogTime = Time.time;
        Debug.Log($"[AR] {_config?.sceneId ?? "-"} | {message} | featureExtractor={(featureExtractor != null ? featureExtractor.name : "null")} | worker={(_worker != null)}");
    }

    private void LogInferenceIfNeeded(float pA, float pB, string detectedAction)
    {
        if (!showInferenceLog) return;
        if (Time.time - _lastInferenceLogTime < inferenceLogIntervalSeconds) return;

        _lastInferenceLogTime = Time.time;
        string labelA = _config != null ? _config.classA : "A";
        string labelB = _config != null ? _config.classB : "B";
        string sceneId = _config != null ? _config.sceneId : "-";
        float threshold = _config != null ? _config.confidenceThreshold : 0f;
        Debug.Log($"[AR] {sceneId} inference | {labelA}={pA:F3}, {labelB}={pB:F3}, threshold={threshold:F3} => {detectedAction} | state={_triggerStateLabel} | buffer={_bufferCount}/{WINDOW_FRAMES}");
    }

    // ?? ?곹깭癒몄떊 ??????????????????????????????????????????????????????????

    private void ProcessTrigger(string action, float confidence, Vector3[] last20avg)
    {
        if (action == "unknown")
        {
            if (_state == TriggerState.Raised)
            {
                _state = TriggerState.Cooldown;
                _triggerStateLabel = "COOLDOWN";
                _cooldownTimer = 0f;
            }
            return;
        }

        switch (_state)
        {
            case TriggerState.Idle:
                _state             = TriggerState.Raised;
                _triggerStateLabel = "RAISED";
                Debug.Log($"[ActionRecognizer] Detected {action} conf={confidence:F3} scene={_config?.sceneName}");
                dispatcher?.OnActionDetected(action, confidence, _config?.sceneId, last20avg);
                break;

            case TriggerState.Raised:
                if (confidence < (_config?.confidenceThreshold ?? 0.75f) * 0.6f)
                {
                    _state             = TriggerState.Cooldown;
                    _triggerStateLabel = "COOLDOWN";
                    _cooldownTimer     = 0f;
                }
                break;

            case TriggerState.Cooldown:
                break;
        }
    }

    // ?? ?붾쾭洹?GUI ????????????????????????????????????????????????????????

}


