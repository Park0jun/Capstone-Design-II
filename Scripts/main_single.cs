using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// ?대떦 ??븷:
///   - Azure Kinect 援щ룞 (SkeletalTrackingProvider)
///   - TrackerHandler_single ???꾨젅???곗씠???꾨떖
///   - Body 媛먯?/?뚯떎 ?대깽??諛쒗뻾 ??WaitingSceneController / StorySceneManager 媛 援щ룆
///   - lastFrameData 瑜??몃?(RGBPreviewViewer ?????몄텧
///
/// [蹂寃쎌젏]
///   - Body 媛먯? ??PuppetAvatar瑜?吏곸젒 ?쒖꽦?뷀븯吏 ?딆쓬
///     ??OnBodyFirstDetected UnityEvent 諛쒗뻾
///     ??WaitingSceneController 媛 援щ룆 ??SceneLoader.LoadNextScene() ?몄텧
///     ???명듃濡??곸긽 醫낅즺 ??StorySceneManager 媛 Puppet ?쒖꽦??///   - PuppetAvatar / SkeletonFeatureExtractor ?먯깋/愿由??쒓굅
///     ??留???濡쒕뱶 ??StorySceneManager 媛 吏곸젒 愿由?/// </summary>
public class main_single : MonoBehaviour
{
    // ?? Inspector ?????????????????????????????????????????????????????????????
    [Header("Tracker")]
    [Tooltip("TrackerHandler_single ?꾨━??(鍮꾩썙?먮㈃ ?먮룞 ?앹꽦)")]
    public GameObject trackerPrefab;

    [Header("Debug")]
    public bool showFrameLog     = false;
    public bool showBodyStateLog = true;
    [Tooltip("Minimum seconds between frame logs when showFrameLog is enabled.")]
    public float frameLogIntervalSeconds = 2f;

    // ?? Body 媛먯? ?대깽????????????????????????????????????????????????????????
    [Header("Body Events")]
    [Tooltip("Body媛 泥섏쓬 媛먯??먯쓣 ??諛쒗뻾 ??WaitingSceneController媛 援щ룆")]
    public UnityEvent OnBodyFirstDetected;

    [Tooltip("Body媛 ?щ씪議뚯쓣 ??諛쒗뻾 ??WaitingSceneController / StorySceneManager媛 援щ룆")]
    public UnityEvent OnBodyLost;

    // ?? ?고???李몄“ ???????????????????????????????????????????????????????????
    [Header("Runtime References (?먮룞 ?ㅼ젙)")]
    public TrackerHandler_single trackerHandler;
    public BackgroundData        lastFrameData = new BackgroundData();

    // ?? ?대? ??????????????????????????????????????????????????????????????????
    private SkeletalTrackingProvider _provider;
    private BackgroundData           _internalFrame = new BackgroundData();
    private bool                     _bodyDetected  = false;
    private float                    _lastFrameLogTime = -999f;

    // ?? Unity ?앸챸二쇨린 ????????????????????????????????????????????????????????

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        Debug.Log("[main_single] Init start.");

        _provider = new SkeletalTrackingProvider(0);
        InitTrackerHandler();

        Debug.Log("[main_single] Init complete.");
    }

    void Update()
    {
        if (!_provider.IsRunning) return;
        if (!_provider.GetCurrentFrameData(ref _internalFrame)) return;

        CopyFrameData(_internalFrame, lastFrameData);

        if (showFrameLog && Time.time - _lastFrameLogTime >= frameLogIntervalSeconds)
        {
            _lastFrameLogTime = Time.time;
            Debug.Log($"[main_single] Frame bodies={lastFrameData.NumOfBodies} ts={lastFrameData.TimestampInMs:F0}ms");
        }

        if (trackerHandler != null && lastFrameData.NumOfBodies > 0)
            trackerHandler.UpdateTracker(lastFrameData);

        // ?? Body 媛먯? ?곹깭 蹂??????????????????????????????????????????????????
        bool tracked = lastFrameData.NumOfBodies > 0;

        if (!_bodyDetected && tracked)
        {
            _bodyDetected = true;
            if (showBodyStateLog) Debug.Log("[main_single] Body detected. Invoke OnBodyFirstDetected.");
            // Puppet ?쒖꽦?붾뒗 ?섏? ?딆쓬 ???명듃濡??곸긽 ??StorySceneManager媛 泥섎━
            OnBodyFirstDetected?.Invoke();
        }
        else if (_bodyDetected && !tracked)
        {
            _bodyDetected = false;
            if (showBodyStateLog) Debug.Log("[main_single] Body lost. Invoke OnBodyLost.");
            OnBodyLost?.Invoke();
        }
    }

    void OnApplicationQuit()
    {
        _provider?.Dispose();
        Debug.Log("[main_single] SkeletalTrackingProvider disposed.");
    }

    // ?? 怨듦컻 API ??????????????????????????????????????????????????????????????

    /// <summary>?꾩옱 Body 媛먯? ?щ?</summary>
    public bool IsBodyDetected => _bodyDetected;

    // ?? 珥덇린???ы띁 ??????????????????????????????????????????????????????????

    private void InitTrackerHandler()
    {
        if (trackerPrefab != null)
        {
            var obj = Instantiate(trackerPrefab);
            obj.name       = "TrackerHandler_single";
            trackerHandler = obj.GetComponent<TrackerHandler_single>()
                          ?? obj.AddComponent<TrackerHandler_single>();
            DontDestroyOnLoad(obj);
        }
        else
        {
            trackerHandler = FindAnyObjectByType<TrackerHandler_single>();
            if (trackerHandler == null)
            {
                var obj = new GameObject("TrackerHandler_single");
                trackerHandler = obj.AddComponent<TrackerHandler_single>();
                DontDestroyOnLoad(obj);
            }
        }
        Debug.Log("[main_single] TrackerHandler ready.");
    }

    // ?? ?꾨젅???곗씠??蹂듭궗 ????????????????????????????????????????????????????

    private void CopyFrameData(BackgroundData src, BackgroundData dst)
    {
        if (src == null || dst == null) return;

        dst.TimestampInMs    = src.TimestampInMs;
        dst.NumOfBodies      = src.NumOfBodies;
        dst.DepthImageWidth  = src.DepthImageWidth;
        dst.DepthImageHeight = src.DepthImageHeight;
        dst.DepthImageSize   = src.DepthImageSize;
        dst.ColorImageWidth  = src.ColorImageWidth;
        dst.ColorImageHeight = src.ColorImageHeight;
        dst.ColorImageSize   = src.ColorImageSize;

        if (src.DepthImageSize > 0 && src.DepthImage != null && dst.DepthImage != null)
            System.Array.Copy(src.DepthImage, dst.DepthImage,
                System.Math.Min(src.DepthImageSize, dst.DepthImage.Length));

        if (src.ColorImageSize > 0 && src.ColorImage != null && dst.ColorImage != null)
            System.Array.Copy(src.ColorImage, dst.ColorImage,
                System.Math.Min(src.ColorImageSize, dst.ColorImage.Length));

        if (src.Bodies != null && dst.Bodies != null)
        {
            int count = System.Math.Min((int)src.NumOfBodies, dst.Bodies.Length);
            for (int i = 0; i < count; i++)
            {
                dst.Bodies[i].Id     = src.Bodies[i].Id;
                dst.Bodies[i].Length = src.Bodies[i].Length;

                if (src.Bodies[i].JointPositions3D != null && dst.Bodies[i].JointPositions3D != null)
                    System.Array.Copy(src.Bodies[i].JointPositions3D,
                        dst.Bodies[i].JointPositions3D,
                        System.Math.Min(src.Bodies[i].Length, dst.Bodies[i].JointPositions3D.Length));

                if (src.Bodies[i].JointRotations != null && dst.Bodies[i].JointRotations != null)
                    System.Array.Copy(src.Bodies[i].JointRotations,
                        dst.Bodies[i].JointRotations,
                        System.Math.Min(src.Bodies[i].Length, dst.Bodies[i].JointRotations.Length));

                if (src.Bodies[i].JointPrecisions != null && dst.Bodies[i].JointPrecisions != null)
                    System.Array.Copy(src.Bodies[i].JointPrecisions,
                        dst.Bodies[i].JointPrecisions,
                        System.Math.Min(src.Bodies[i].Length, dst.Bodies[i].JointPrecisions.Length));
            }
        }
    }

    // ?? ?붾쾭洹?GUI ????????????????????????????????????????????????????????????

}

