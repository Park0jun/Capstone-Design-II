using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// ?ㅽ넗由????꾩껜 ?먮쫫 愿由ъ옄 (DontDestroyOnLoad)
///
/// [?꾩껜 ?먮쫫]
///   OnMapSceneLoaded(config)          ??SceneLoader ?몄텧
///     ?붴넂 NPC / Avatar ?ъ뿰寃?///     ?붴넂 ???명듃濡??곸긽 ?ъ깮
///     ?붴넂 ?곸긽 醫낅즺 ??ActivatePuppet()   ???ш린??泥섏쓬?쇰줈 Puppet ?쒖꽦??///     ?붴넂 ?명꽣?숈뀡 (?됰룞 ?몄떇 ??NPC 諛섏쓳 ???꾪솚 ?곸긽)
///     ?붴넂 遺꾧린 醫낅즺
///           ?붴넂 SetNextConfig + GoToWaitingScene
///           ?붴넂 (?먮뒗 ?ㅽ넗由????ㅼ쓬 遺꾧린濡?吏곸젒 TransitionTo)
/// </summary>
public class StorySceneManager : MonoBehaviour
{
    // ?? Video UI ??????????????????????????????????????????????????????????
    [Header("Video System")]
    public VideoPlayer videoPlayer;
    public RawImage    videoDisplay;
    public CanvasGroup videoCanvasGroup;
    public Image       fadeCurtain;

    [Header("Fade")]
    public float fadeInDuration  = 1.0f;
    public float fadeOutDuration = 1.5f;

    [Header("Audio Fade")]
    public bool fadeVideoAudio = true;
    [Range(0f, 1f)] public float videoAudioVolume = 1f;

    [Header("Cinematic Bars")]
    public RectTransform topCinematicBar;
    public RectTransform bottomCinematicBar;
    public float cinematicBarHeight = 160f;
    public float cinematicBarDuration = 0.8f;
    public bool hideBarsWhenTransitionVideoStarts = true;

    [Header("NPC Reaction")]
    [Tooltip("Seconds to wait after a result is selected before transition video. NPC animation is triggered first when configured.")]
    public float npcReactionWaitSeconds = 2.0f;

    [Header("References")]
    public SceneLoader      sceneLoader;
    public ActionRecognizer actionRecognizer;

    [Header("Runtime (ReadOnly)")]
    [SerializeField] private string _currentSceneId  = "-";
    [SerializeField] private bool   _isTransitioning = false;

    // ?? ?대? 李몄“ ?????????????????????????????????????????????????????????
    private PuppetAvatar_single      _puppet;
    private SkeletonFeatureExtractor _featureExtractor;
    private MapInteractionController _interactionController;
    private SceneActionConfig        _currentConfig;

    // ?? Unity ?앸챸二쇨린 ????????????????????????????????????????????????????

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (sceneLoader      == null) sceneLoader      = FindAnyObjectByType<SceneLoader>();
        if (actionRecognizer == null) actionRecognizer = FindAnyObjectByType<ActionRecognizer>();

        ResolveVideoReferences();
        if (videoCanvasGroup != null) videoCanvasGroup.alpha = 0f;
        SetFadeCurtainAlpha(0f);
        HideCinematicBarsImmediate();
    }

    void OnDestroy()
    {
        if (videoPlayer != null) videoPlayer.prepareCompleted -= OnVideoPrepared;
    }

    // ?? 留???濡쒕뱶 ?꾨즺 吏꾩엯??(SceneLoader?먯꽌 ?몄텧) ?????????????????????

    /// <summary>
    /// SceneLoader.OnUnitySceneLoaded() ??留???濡쒕뱶 ?꾨즺 ???몄텧.
    /// ?쒖꽌: NPC쨌Avatar ?ъ뿰寃????명듃濡??곸긽 ??Puppet ?쒖꽦?????명꽣?숈뀡 ?쒖옉
    /// </summary>
    public void OnMapSceneLoaded(SceneActionConfig config)
    {
        _currentConfig    = config;
        _currentSceneId  = config.sceneId;
        _isTransitioning = false;
        ResolveVideoReferences();

        // NPC ?ы깘??(?쒓렇 湲곕컲)
        config.ResolveNpcReferences();

        // Avatar ?ы깘??(???ъ뿉 諛곗튂??寃?
        ResolveAvatarReferences();

        if (config.introVideo != null)
            StartCoroutine(PlayIntroThenActivate(config));
        else
        {
            Debug.Log($"[StorySceneManager] No intro video: {config.sceneName}. Start interaction now.");
            ActivatePuppet();
        }
    }

    // ?? Avatar ?ы깘???????????????????????????????????????????????????????

    private void ResolveAvatarReferences()
    {
        _puppet = FindAnyObjectByType<PuppetAvatar_single>();
        if (_puppet == null)
        {
            Debug.LogWarning("[StorySceneManager] PuppetAvatar_single not found.");
            return;
        }

        _featureExtractor = FindAnyObjectByType<SkeletonFeatureExtractor>();
        if (_featureExtractor == null)
        {
            Debug.LogWarning("[StorySceneManager] SkeletonFeatureExtractor not found.");
            return;
        }

        // ActionRecognizer ?????ъ쓽 featureExtractor ?곌껐
        if (actionRecognizer != null)
            actionRecognizer.featureExtractor = _featureExtractor;

        TrackerHandler_single tracker = FindAnyObjectByType<TrackerHandler_single>();
        if (tracker != null)
        {
            _puppet.KinectDevice = tracker;
            _featureExtractor.SetTrackerHandler(tracker);
            Debug.Log($"[StorySceneManager] Tracker connected to avatar: {tracker.name}");
        }
        else
        {
            Debug.LogWarning("[StorySceneManager] TrackerHandler_single not found while connecting avatar.");
        }

        _interactionController = FindAnyObjectByType<MapInteractionController>();

        // Puppet? ?명듃濡??곸긽???앸궇 ?뚭퉴吏 鍮꾪솢???좎?
        _puppet.gameObject.SetActive(false);

        Debug.Log($"[StorySceneManager] Avatar connected: puppet={_puppet.name}, extractor={_featureExtractor.name}");
    }

    // ?? ?명듃濡??곸긽 ??Puppet ?쒖꽦????????????????????????????????????????

    private IEnumerator PlayIntroThenActivate(SceneActionConfig config)
    {
        yield return StartCoroutine(PlayVideo(config.introVideo, fadeInFromTransparent: true));
        Debug.Log($"[StorySceneManager] Intro video finished. Activate puppet: {config.sceneName}");
        ActivatePuppet();
    }

    /// <summary>
    /// ?명듃濡??곸긽 醫낅즺 ???몄텧.
    /// Puppet ?쒖꽦??+ SkeletonFeatureExtractor.OnBodyDetected() ?몄텧.
    /// </summary>
    private void ActivatePuppet()
    {
        if (_puppet == null)
        {
            Debug.LogWarning("[StorySceneManager] ActivatePuppet skipped: puppet is missing.");
            return;
        }

        _puppet.gameObject.SetActive(true);
        _puppet.ResetPositionTracking();
        _featureExtractor?.OnBodyDetected();
        _interactionController?.BeginInteraction(_currentConfig);

        Debug.Log("[StorySceneManager] Puppet activated. Interaction started.");
    }

    // ?? ActionDispatcher onDetected ?곌껐???섑띁 硫붿꽌??????????????????????

    public void OnMap01_SimCheong() => HandleAction("map_01", "push_simCheong");
    public void OnMap01_Sailor()     => HandleAction("map_01", "push_sailor");

    public void OnMap02_Lotus()      => HandleAction("map_02", "select_lotus");
    public void OnMap02_Attack()     => HandleAction("map_02", "attack_simCheong");

    public void OnFinalActionSelected(string sceneId, string actionName) => HandleAction(sceneId, actionName);

    public void OnMap03_1_RunAway()  => HandleAction("map_03_1", "run_away");
    public void OnMap03_2_Heart()    => HandleAction("map_03_2", "heart_dragonKing");

    public void OnMap04_1_Kick()     => HandleAction("map_04_1", "kick_simCheong");
    public void OnMap04_1_Touch()    => HandleAction("map_04_1", "touch_simCheong");
    public void OnMap04_2_Move()     => HandleAction("map_04_2", "move");

    public void OnMap04_3_TouchShin()   => HandleAction("map_04_3", "touch_simCheong");
    public void OnMap04_3_TouchDragon() => HandleAction("map_04_3", "touch_dragonKing");
    public void OnMap04_4_TouchShin()   => HandleAction("map_04_4", "touch_simCheong");
    public void OnMap04_4_TouchDragon() => HandleAction("map_04_4", "touch_dragonKing");

    // ?? ?듭떖 ?먮쫫 ?????????????????????????????????????????????????????????

    private void HandleAction(string sceneId, string actionName)
    {
        if (_isTransitioning)
        {
            Debug.Log($"[StorySceneManager] Ignored input while transitioning: {actionName}");
            return;
        }

        SceneActionConfig config = sceneLoader?.GetCurrentConfig();
        if (config == null) { Debug.LogWarning("[StorySceneManager] Current config is missing."); return; }

        SceneActionConfig.ActionReaction reaction = config.GetReaction(actionName);
        if (reaction == null)
        {
            Debug.LogWarning($"[StorySceneManager] ActionReaction not found: {sceneId}/{actionName}");
            return;
        }

        Debug.Log($"[StorySceneManager] Action selected: {actionName} | NPC={reaction.resolvedNpcObject?.name}");
        _isTransitioning = true;
        StartCoroutine(ActionFlowRoutine(reaction));
    }

    /// <summary>NPC 諛섏쓳 ???꾪솚 ?곸긽 ???ㅼ쓬 ??寃곗젙</summary>
    private IEnumerator ActionFlowRoutine(SceneActionConfig.ActionReaction reaction)
    {
        // Stop accepting new input immediately, but keep the avatar visible until the transition video starts.
        _interactionController?.EndInteraction();

        bool hasNpcReaction =
            reaction.resolvedNpcObject != null &&
            !string.IsNullOrWhiteSpace(reaction.animationTrigger);

        if (hasNpcReaction)
            TriggerNPCAnimation(reaction);

        if (npcReactionWaitSeconds > 0f)
        {
            Debug.Log($"[StorySceneManager] Waiting {npcReactionWaitSeconds:F1}s before transition video.");
            yield return new WaitForSeconds(npcReactionWaitSeconds);
        }

        // ?꾪솚 ?곸긽
        if (reaction.transitionVideo != null)
        {
            yield return StartCoroutine(ShowCinematicBars());
            yield return StartCoroutine(PlayVideo(
                reaction.transitionVideo,
                keepVisibleAfter: true,
                hideCinematicBarsAfterStart: hideBarsWhenTransitionVideoStarts));
        }

        if (_puppet != null) _puppet.gameObject.SetActive(false);
        _featureExtractor?.OnBodyLost();

        // ?ㅼ쓬 ??寃곗젙
        if (reaction.nextSceneConfig != null)
        {
            SceneActionConfig next = reaction.nextSceneConfig;

            if (next.isWaitingPoint)
            {
                // ?? ?湲???寃쎌쑀: ?ㅼ쓬 遺꾧린瑜??덉빟?섍퀬 WaitingScene?쇰줈 ????????
                Debug.Log($"[StorySceneManager] Branch finished. Return to WaitingScene. Next={next.sceneName}");
                sceneLoader.SetNextConfig(next);
                sceneLoader.GoToWaitingScene();
            }
            else
            {
                // ?? ?ㅽ넗由???吏곸젒 ?꾪솚 (?湲??놁씠 諛붾줈 ?ㅼ쓬 遺꾧린濡? ??????????
                Debug.Log($"[StorySceneManager] Direct transition to {next.sceneName}");
                sceneLoader.TransitionTo(next);
            }
        }
        else
        {
            // nextSceneConfig ?놁쓬 = ?ㅽ넗由?醫낅즺
            Debug.Log("[StorySceneManager] Story branch ended. Return to WaitingScene.");
            sceneLoader.SetNextConfig(sceneLoader.FirstConfig);
            sceneLoader.GoToWaitingScene();
        }
    }

    // ?? NPC ?좊땲硫붿씠??????????????????????????????????????????????????????

    private void TriggerNPCAnimation(SceneActionConfig.ActionReaction reaction)
    {
        if (reaction.resolvedNpcObject == null || string.IsNullOrEmpty(reaction.animationTrigger))
        {
            Debug.Log("[StorySceneManager] NPC animation skipped: NPC or trigger is missing.");
            return;
        }

        Animator anim = reaction.resolvedNpcObject.GetComponent<Animator>()
                     ?? reaction.resolvedNpcObject.GetComponentInChildren<Animator>();

        if (anim != null)
        {
            anim.SetTrigger(reaction.animationTrigger);
            Debug.Log($"[StorySceneManager] NPC animation: {reaction.resolvedNpcObject.name} -> {reaction.animationTrigger}");
        }
        else
            Debug.LogWarning($"[StorySceneManager] Animator not found: {reaction.resolvedNpcObject.name}");
    }

    // ?? ?곸긽 ?ъ깮 ?????????????????????????????????????????????????????????

    private IEnumerator PlayVideo(
        VideoClip clip,
        bool keepVisibleAfter = false,
        bool fadeInFromTransparent = false,
        bool hideCinematicBarsAfterStart = false)
    {
        if (clip == null) yield break;

        ResolveVideoReferences();
        if (videoPlayer == null)
        {
            Debug.LogWarning($"[StorySceneManager] VideoPlayer is missing. Skip video: {clip.name}");
            yield break;
        }

        bool overlayWasVisible = videoCanvasGroup != null && videoCanvasGroup.alpha > 0.5f;

        videoPlayer.clip = clip;
        videoPlayer.isLooping = false;
        ConfigureVideoAudio();

        if (videoCanvasGroup != null)
            videoCanvasGroup.alpha = fadeInFromTransparent && !overlayWasVisible ? 0f : 1f;

        if (fadeInFromTransparent && overlayWasVisible)
            SetFadeCurtainAlpha(1f);

        bool shouldFadeInAudio = fadeVideoAudio && fadeInFromTransparent;
        SetVideoAudioVolume(shouldFadeInAudio ? 0f : videoAudioVolume);

        videoPlayer.Prepare();
        while (!videoPlayer.isPrepared) yield return null;

        if (videoDisplay != null) videoDisplay.texture = videoPlayer.texture;

        bool ended = false;
        VideoPlayer.EventHandler onEnd = _ => ended = true;
        videoPlayer.loopPointReached += onEnd;
        videoPlayer.Play();

        if (hideCinematicBarsAfterStart)
        {
            yield return null;
            HideCinematicBarsImmediate();
        }

        if (fadeInFromTransparent && overlayWasVisible)
            yield return StartCoroutine(FadeCurtainAndAudio(1f, 0f, 0f, videoAudioVolume, fadeInDuration, shouldFadeInAudio));
        else if (!overlayWasVisible && (videoCanvasGroup == null || videoCanvasGroup.alpha < 0.99f))
            yield return StartCoroutine(FadeCanvasAndAudio(videoCanvasGroup != null ? videoCanvasGroup.alpha : 0f, 1f, 0f, videoAudioVolume, fadeInDuration, shouldFadeInAudio));

        double duration = GetPreparedVideoDuration(clip);
        float effectiveFadeOut = GetEffectiveFadeOutDuration(duration);

        if (!keepVisibleAfter && effectiveFadeOut > 0f && duration > effectiveFadeOut)
        {
            double fadeStartTime = duration - effectiveFadeOut;
            while (!ended && videoPlayer.time < fadeStartTime)
                yield return null;

            yield return StartCoroutine(FadeCanvasAndAudio(1f, 0f, videoAudioVolume, 0f, effectiveFadeOut, fadeVideoAudio));

            while (!ended)
                yield return null;
        }
        else if (keepVisibleAfter && fadeVideoAudio && effectiveFadeOut > 0f && duration > effectiveFadeOut)
        {
            double fadeStartTime = duration - effectiveFadeOut;
            while (!ended && videoPlayer.time < fadeStartTime)
                yield return null;

            yield return StartCoroutine(FadeVideoAudio(videoAudioVolume, 0f, effectiveFadeOut));

            while (!ended)
                yield return null;
        }
        else
        {
            while (!ended) yield return null;
        }

        videoPlayer.loopPointReached -= onEnd;

        if (keepVisibleAfter)
        {
            if (videoCanvasGroup != null)
                videoCanvasGroup.alpha = 1f;
        }
        else
        {
            if (videoCanvasGroup != null)
                videoCanvasGroup.alpha = 0f;
            SetFadeCurtainAlpha(0f);
            SetVideoAudioVolume(0f);
            videoPlayer.Stop();
        }
    }

    private double GetPreparedVideoDuration(VideoClip clip)
    {
        if (videoPlayer != null && videoPlayer.length > 0.01)
            return videoPlayer.length;

        if (clip != null && clip.length > 0.01)
            return clip.length;

        if (videoPlayer != null && videoPlayer.frameCount > 0 && videoPlayer.frameRate > 0.01)
            return videoPlayer.frameCount / videoPlayer.frameRate;

        return 0.0;
    }

    private float GetEffectiveFadeOutDuration(double duration)
    {
        if (fadeOutDuration <= 0f || duration <= 0.1)
            return 0f;

        return Mathf.Min(fadeOutDuration, Mathf.Max(0.15f, (float)duration * 0.35f));
    }

    private void OnVideoPrepared(VideoPlayer vp)
    {
        if (videoDisplay != null) videoDisplay.texture = vp.texture;
    }

    private void ResolveVideoReferences()
    {
        if (videoPlayer == null)
        {
            videoPlayer = FindAnyObjectByType<VideoPlayer>();
            if (videoPlayer != null)
                videoPlayer.prepareCompleted += OnVideoPrepared;
        }

        if (videoDisplay == null)
            videoDisplay = FindAnyObjectByType<RawImage>();

        if (videoCanvasGroup == null && videoDisplay != null)
            videoCanvasGroup = videoDisplay.GetComponentInParent<CanvasGroup>();

        if (fadeCurtain == null && videoDisplay != null)
            fadeCurtain = CreateFadeCurtain(videoDisplay);

        ResolveCinematicBars();
    }

    private void ResolveCinematicBars()
    {
        if (videoDisplay == null) return;

        Transform parent = GetCinematicBarsParent();
        if (parent == null) return;

        if (topCinematicBar == null)
        {
            Transform existing = parent.Find("CinematicTopBar");
            topCinematicBar = existing != null
                ? existing as RectTransform
                : CreateCinematicBar(parent, "CinematicTopBar", true);
        }

        if (bottomCinematicBar == null)
        {
            Transform existing = parent.Find("CinematicBottomBar");
            bottomCinematicBar = existing != null
                ? existing as RectTransform
                : CreateCinematicBar(parent, "CinematicBottomBar", false);
        }

        if (topCinematicBar != null)
            topCinematicBar.SetAsLastSibling();
        if (bottomCinematicBar != null)
            bottomCinematicBar.SetAsLastSibling();
    }

    private Transform GetCinematicBarsParent()
    {
        Canvas canvas = videoDisplay != null ? videoDisplay.GetComponentInParent<Canvas>() : null;
        if (canvas != null)
            return canvas.transform;

        return videoDisplay != null ? videoDisplay.transform.parent : null;
    }

    private RectTransform CreateCinematicBar(Transform parent, string objectName, bool top)
    {
        GameObject barObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        barObject.transform.SetParent(parent, false);

        RectTransform rect = barObject.GetComponent<RectTransform>();
        ConfigureCinematicBarRect(rect, top);

        Image image = barObject.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;

        return rect;
    }

    private void ConfigureCinematicBarRect(RectTransform rect, bool top)
    {
        if (rect == null) return;

        rect.anchorMin = top ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
        rect.anchorMax = top ? new Vector2(1f, 1f) : new Vector2(1f, 0f);
        rect.pivot = top ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(0f, cinematicBarHeight);
        rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
        rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
    }

    private void SetCinematicBarsPosition(float normalized)
    {
        ResolveCinematicBars();

        float t = Mathf.Clamp01(normalized);
        float hiddenTopY = cinematicBarHeight;
        float hiddenBottomY = -cinematicBarHeight;

        if (topCinematicBar != null)
        {
            ConfigureCinematicBarRect(topCinematicBar, true);
            topCinematicBar.anchoredPosition = new Vector2(0f, Mathf.Lerp(hiddenTopY, 0f, t));
        }

        if (bottomCinematicBar != null)
        {
            ConfigureCinematicBarRect(bottomCinematicBar, false);
            bottomCinematicBar.anchoredPosition = new Vector2(0f, Mathf.Lerp(hiddenBottomY, 0f, t));
        }
    }

    private void HideCinematicBarsImmediate()
    {
        SetCinematicBarsPosition(0f);
    }

    private IEnumerator ShowCinematicBars()
    {
        ResolveCinematicBars();

        if (cinematicBarDuration <= 0f)
        {
            SetCinematicBarsPosition(1f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < cinematicBarDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / cinematicBarDuration);
            SetCinematicBarsPosition(t);
            yield return null;
        }

        SetCinematicBarsPosition(1f);
    }

    private Image CreateFadeCurtain(RawImage targetDisplay)
    {
        Canvas canvas = targetDisplay.GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.transform : targetDisplay.transform.parent;
        if (parent == null) return null;

        GameObject curtainObject = new GameObject("VideoFadeCurtain", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        curtainObject.transform.SetParent(parent, false);
        curtainObject.transform.SetAsLastSibling();

        RectTransform rect = curtainObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = curtainObject.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = false;
        return image;
    }

    private void SetFadeCurtainAlpha(float alpha)
    {
        if (fadeCurtain == null) return;

        Color color = fadeCurtain.color;
        color.r = 0f;
        color.g = 0f;
        color.b = 0f;
        color.a = alpha;
        fadeCurtain.color = color;
    }

    private void ConfigureVideoAudio()
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
                // Some platforms or audio output modes do not expose every track.
            }
        }
    }

    private void SetVideoAudioVolume(float volume)
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
                // Ignored when the current VideoPlayer output mode does not use direct audio.
            }

            try
            {
                AudioSource source = videoPlayer.GetTargetAudioSource(i);
                if (source != null)
                    source.volume = clamped;
            }
            catch (System.Exception)
            {
                // Ignored when no AudioSource is assigned for this track.
            }
        }
    }

    private IEnumerator FadeVideoAudio(float from, float to, float duration)
    {
        if (!fadeVideoAudio)
        {
            SetVideoAudioVolume(to);
            yield break;
        }

        if (duration <= 0f)
        {
            SetVideoAudioVolume(to);
            yield break;
        }

        float elapsed = 0f;
        SetVideoAudioVolume(from);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetVideoAudioVolume(Mathf.Lerp(from, to, elapsed / duration));
            yield return null;
        }
        SetVideoAudioVolume(to);
    }

    private IEnumerator FadeCurtain(float from, float to, float duration)
    {
        if (fadeCurtain == null) yield break;

        if (duration <= 0f)
        {
            SetFadeCurtainAlpha(to);
            yield break;
        }

        float elapsed = 0f;
        SetFadeCurtainAlpha(from);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetFadeCurtainAlpha(Mathf.Lerp(from, to, elapsed / duration));
            yield return null;
        }
        SetFadeCurtainAlpha(to);
    }

    private IEnumerator FadeCurtainAndAudio(float curtainFrom, float curtainTo, float audioFrom, float audioTo, float duration, bool includeAudio)
    {
        if (duration <= 0f)
        {
            SetFadeCurtainAlpha(curtainTo);
            if (includeAudio) SetVideoAudioVolume(audioTo);
            yield break;
        }

        float elapsed = 0f;
        SetFadeCurtainAlpha(curtainFrom);
        if (includeAudio) SetVideoAudioVolume(audioFrom);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            SetFadeCurtainAlpha(Mathf.Lerp(curtainFrom, curtainTo, t));
            if (includeAudio) SetVideoAudioVolume(Mathf.Lerp(audioFrom, audioTo, t));
            yield return null;
        }

        SetFadeCurtainAlpha(curtainTo);
        if (includeAudio) SetVideoAudioVolume(audioTo);
    }

    private IEnumerator FadeCanvasAndAudio(float canvasFrom, float canvasTo, float audioFrom, float audioTo, float duration, bool includeAudio)
    {
        if (videoCanvasGroup == null)
        {
            if (includeAudio)
                yield return StartCoroutine(FadeVideoAudio(audioFrom, audioTo, duration));
            yield break;
        }

        if (duration <= 0f)
        {
            videoCanvasGroup.alpha = canvasTo;
            if (includeAudio) SetVideoAudioVolume(audioTo);
            yield break;
        }

        float elapsed = 0f;
        videoCanvasGroup.alpha = canvasFrom;
        if (includeAudio) SetVideoAudioVolume(audioFrom);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            videoCanvasGroup.alpha = Mathf.Lerp(canvasFrom, canvasTo, t);
            if (includeAudio) SetVideoAudioVolume(Mathf.Lerp(audioFrom, audioTo, t));
            yield return null;
        }

        videoCanvasGroup.alpha = canvasTo;
        if (includeAudio) SetVideoAudioVolume(audioTo);
    }

    private IEnumerator FadeCanvas(float from, float to, float duration)
    {
        if (videoCanvasGroup == null) yield break;
        float elapsed = 0f;
        videoCanvasGroup.alpha = from;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            videoCanvasGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        videoCanvasGroup.alpha = to;
    }

    // ?? ?붾쾭洹?????????????????????????????????????????????????????????????

}


