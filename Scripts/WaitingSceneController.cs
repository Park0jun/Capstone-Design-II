using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using System.Collections;

public class WaitingSceneController : MonoBehaviour
{
    [Header("Waiting Media")]
    [Tooltip("Looping waiting video. If empty, waitingImage is used instead.")]
    public VideoClip waitingVideoClip;

    [Tooltip("Fallback image shown when waitingVideoClip is empty.")]
    public Sprite waitingImage;

    [Header("UI References")]
    public VideoPlayer videoPlayer;
    public RawImage videoDisplay;
    public Image imageDisplay;

    [Header("Waiting UI")]
    public GameObject waitingUI;
    public bool keepVideoOverlayDuringMapLoad = true;
    [Tooltip("If WaitingScene opens while a body is still tracked, wait until that body leaves before accepting the next entry.")]
    public bool requireFreshBodyEntry = true;
    [Tooltip("Delay after a fresh body is detected before loading the next map intro.")]
    public float bodyDetectedDelaySeconds = 2f;

    [Header("Audio Fade")]
    public bool fadeWaitingAudio = true;
    [Range(0f, 1f)] public float waitingAudioVolume = 1f;
    public float waitingAudioFadeOutSeconds = 1f;

    [Header("Runtime (ReadOnly)")]
    [SerializeField] private bool _bodyDetectedHandled = false;
    [SerializeField] private bool _waitingForBodyExit = false;

    private SceneLoader _sceneLoader;
    private main_single _mainSingle;
    private CanvasGroup _videoCanvasGroup;

    void Start()
    {
        ResolveMediaReferences();

        _mainSingle = FindAnyObjectByType<main_single>();
        if (_mainSingle == null)
        {
            Debug.LogError("[WaitingScene] main_single not found");
            return;
        }

        _mainSingle.OnBodyFirstDetected.AddListener(OnBodyDetected);
        _mainSingle.OnBodyLost.AddListener(OnBodyLost);
        StartCoroutine(PlayWaitingMedia());

        if (_mainSingle.IsBodyDetected)
        {
            if (requireFreshBodyEntry)
            {
                _waitingForBodyExit = true;
                Debug.Log("[WaitingScene] Existing body detected on entry. Waiting for body exit before next map.");
            }
            else
            {
                Debug.Log("[WaitingScene] Body already detected on entry. Continue next frame.");
                StartCoroutine(DetectNextFrame());
            }
        }
    }

    void OnDestroy()
    {
        if (_mainSingle != null)
        {
            _mainSingle.OnBodyFirstDetected.RemoveListener(OnBodyDetected);
            _mainSingle.OnBodyLost.RemoveListener(OnBodyLost);
        }
    }

    public void OnWaitingSceneReady(SceneLoader loader)
    {
        _sceneLoader = loader;
        Debug.Log("[WaitingScene] SceneLoader injected");
    }

    public void OnBodyDetected()
    {
        if (_bodyDetectedHandled) return;
        if (_waitingForBodyExit)
        {
            Debug.Log("[WaitingScene] Ignored existing body. Waiting for a fresh entry.");
            return;
        }

        if (_sceneLoader == null)
        {
            Debug.LogWarning("[WaitingScene] SceneLoader is not ready. Waiting briefly.");
            StartCoroutine(WaitForLoaderThenLoad());
            return;
        }

        HandleBodyDetected();
    }

    public void OnBodyLost()
    {
        if (!_waitingForBodyExit) return;

        _waitingForBodyExit = false;
        Debug.Log("[WaitingScene] Body exited. Ready for next visitor.");
    }

    private void HandleBodyDetected()
    {
        if (_bodyDetectedHandled) return;
        _bodyDetectedHandled = true;

        Debug.Log($"[WaitingScene] Body detected. Loading next map scene after {bodyDetectedDelaySeconds:F1}s.");
        StartCoroutine(LoadNextSceneAfterDelay());
    }

    private IEnumerator LoadNextSceneAfterDelay()
    {
        if (bodyDetectedDelaySeconds > 0f)
            yield return new WaitForSeconds(bodyDetectedDelaySeconds);

        if (fadeWaitingAudio && videoPlayer != null && videoPlayer.isPlaying && waitingAudioFadeOutSeconds > 0f)
            yield return StartCoroutine(FadeWaitingVideoAudio(waitingAudioVolume, 0f, waitingAudioFadeOutSeconds));
        else
            SetWaitingVideoAudioVolume(0f);

        if (waitingUI != null) waitingUI.SetActive(false);
        StopWaitingVideoIfReady();
        SetVideoCanvasVisible(keepVideoOverlayDuringMapLoad);

        _sceneLoader.LoadNextScene();
    }

    private IEnumerator PlayWaitingMedia()
    {
        ResolveMediaReferences();

        if (waitingVideoClip != null && videoPlayer != null && videoDisplay != null)
        {
            SetVideoCanvasVisible(true);
            videoPlayer.clip = waitingVideoClip;
            videoPlayer.isLooping = true;
            videoPlayer.playOnAwake = false;
            videoPlayer.renderMode = VideoRenderMode.APIOnly;
            ConfigureWaitingVideoAudio();
            SetWaitingVideoAudioVolume(waitingAudioVolume);
            videoPlayer.prepareCompleted += OnVideoPrepared;
            videoPlayer.Prepare();

            while (videoPlayer != null && !videoPlayer.isPrepared)
                yield return null;

            if (videoPlayer != null)
                videoPlayer.Play();
        }
        else if (waitingImage != null && imageDisplay != null)
        {
            SetVideoCanvasVisible(false);
            imageDisplay.sprite = waitingImage;
            imageDisplay.enabled = true;
            if (videoDisplay != null) videoDisplay.enabled = false;
        }
    }

    private void OnVideoPrepared(VideoPlayer vp)
    {
        if (videoDisplay != null)
            videoDisplay.texture = vp.texture;
    }

    private void ResolveMediaReferences()
    {
        if (videoPlayer == null)
            videoPlayer = FindAnyObjectByType<VideoPlayer>();

        if (videoDisplay == null)
            videoDisplay = FindAnyObjectByType<RawImage>();

        if (_videoCanvasGroup == null && videoDisplay != null)
            _videoCanvasGroup = videoDisplay.GetComponentInParent<CanvasGroup>();
    }

    private void SetVideoCanvasVisible(bool visible)
    {
        ResolveMediaReferences();
        if (_videoCanvasGroup == null) return;

        _videoCanvasGroup.alpha = visible ? 1f : 0f;
        _videoCanvasGroup.interactable = false;
        _videoCanvasGroup.blocksRaycasts = false;
    }

    private void StopWaitingVideoIfReady()
    {
        if (videoPlayer == null) return;

        try
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            SetWaitingVideoAudioVolume(0f);
            videoPlayer.Stop();
        }
        catch (MissingReferenceException)
        {
            videoPlayer = null;
        }
        catch (UnassignedReferenceException)
        {
            videoPlayer = null;
        }
    }

    private void ConfigureWaitingVideoAudio()
    {
        if (videoPlayer == null) return;

        ushort trackCount = (ushort)Mathf.Max(1, (int)videoPlayer.controlledAudioTrackCount);
        videoPlayer.controlledAudioTrackCount = trackCount;

        for (ushort i = 0; i < trackCount; i++)
        {
            try
            {
                videoPlayer.EnableAudioTrack(i, true);
                videoPlayer.SetDirectAudioMute(i, false);
            }
            catch (System.Exception)
            {
                // Some platforms or output modes do not expose every track.
            }
        }
    }

    private void SetWaitingVideoAudioVolume(float volume)
    {
        if (videoPlayer == null) return;

        float clamped = Mathf.Clamp01(volume);
        ushort trackCount = (ushort)Mathf.Max(1, (int)videoPlayer.controlledAudioTrackCount);

        for (ushort i = 0; i < trackCount; i++)
        {
            try
            {
                videoPlayer.SetDirectAudioVolume(i, clamped);
            }
            catch (System.Exception)
            {
                // Ignored when direct audio is not used.
            }

            try
            {
                AudioSource source = videoPlayer.GetTargetAudioSource(i);
                if (source != null)
                    source.volume = clamped;
            }
            catch (System.Exception)
            {
                // Ignored when no AudioSource is assigned.
            }
        }
    }

    private IEnumerator FadeWaitingVideoAudio(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            SetWaitingVideoAudioVolume(to);
            yield break;
        }

        float elapsed = 0f;
        SetWaitingVideoAudioVolume(from);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetWaitingVideoAudioVolume(Mathf.Lerp(from, to, elapsed / duration));
            yield return null;
        }
        SetWaitingVideoAudioVolume(to);
    }

    private IEnumerator DetectNextFrame()
    {
        yield return null;
        OnBodyDetected();
    }

    private IEnumerator WaitForLoaderThenLoad()
    {
        float timeout = 3f;
        float elapsed = 0f;

        while (_sceneLoader == null && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (_sceneLoader == null)
        {
            Debug.LogError("[WaitingScene] SceneLoader was not injected in time.");
            yield break;
        }

        HandleBodyDetected();
    }
}
