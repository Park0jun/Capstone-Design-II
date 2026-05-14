using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class SceneLoader : MonoBehaviour
{
    [Header("Scene Configs")]
    public SceneActionConfig[] configs;

    [Header("Scene Names")]
    public string waitingSceneName = "WaitingScene";

    [Header("First Branch")]
    public SceneActionConfig firstConfig;

    [Header("References")]
    public ActionRecognizer recognizer;
    public TouchColliderHandler colliderHandler;
    public StorySceneManager storySceneManager;

    [Header("Runtime (ReadOnly)")]
    [SerializeField] private string _currentSceneName = "-";
    [SerializeField] private string _nextSceneName = "-";
    [SerializeField] private bool _isLoading = false;

    private SceneActionConfig _currentConfig;
    private SceneActionConfig _nextConfig;
    private SceneActionConfig _pendingConfig;

    public SceneActionConfig GetCurrentConfig() => _currentConfig;
    public SceneActionConfig FirstConfig => firstConfig;
    public bool IsLoading => _isLoading;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (recognizer == null) recognizer = FindAnyObjectByType<ActionRecognizer>();
        if (colliderHandler == null) colliderHandler = FindAnyObjectByType<TouchColliderHandler>();
        if (storySceneManager == null) storySceneManager = FindAnyObjectByType<StorySceneManager>();

        SceneManager.sceneLoaded += OnUnitySceneLoaded;

        _nextConfig = firstConfig;
        _nextSceneName = firstConfig != null ? firstConfig.sceneName : "-";

        StartCoroutine(LoadSceneAsync(waitingSceneName));
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnUnitySceneLoaded;
    }

    public void GoToWaitingScene()
    {
        if (_isLoading)
        {
            Debug.LogWarning("[SceneLoader] GoToWaitingScene ignored while loading.");
            return;
        }

        _isLoading = true;
        Debug.Log("[SceneLoader] Loading WaitingScene.");
        StartCoroutine(LoadSceneAsync(waitingSceneName));
    }

    public void LoadNextScene()
    {
        if (_isLoading)
        {
            Debug.LogWarning("[SceneLoader] LoadNextScene ignored while loading.");
            return;
        }

        if (_nextConfig == null)
        {
            Debug.LogError("[SceneLoader] nextConfig is missing. Check firstConfig or SetNextConfig().");
            return;
        }

        if (string.IsNullOrEmpty(_nextConfig.unitySceneName))
        {
            Debug.LogError($"[SceneLoader] unitySceneName is empty: {_nextConfig.sceneName}");
            return;
        }

        _pendingConfig = _nextConfig;
        _isLoading = true;

        Debug.Log($"[SceneLoader] Loading map scene: {_pendingConfig.sceneName} ({_pendingConfig.unitySceneName})");

        recognizer?.LoadScene(_pendingConfig);
        colliderHandler?.SetScene(_pendingConfig);

        StartCoroutine(LoadSceneAsync(_pendingConfig.unitySceneName));
    }

    public void SetNextConfig(SceneActionConfig config)
    {
        _nextConfig = config;
        _nextSceneName = config != null ? config.sceneName : "-";
        Debug.Log($"[SceneLoader] Next config set: {_nextSceneName}");
    }

    public void TransitionTo(SceneActionConfig config)
    {
        if (config == null)
        {
            Debug.LogError("[SceneLoader] config is null.");
            return;
        }

        if (_isLoading)
        {
            Debug.LogWarning("[SceneLoader] TransitionTo ignored while loading.");
            return;
        }

        _pendingConfig = config;
        _isLoading = true;

        recognizer?.LoadScene(config);
        colliderHandler?.SetScene(config);

        StartCoroutine(LoadSceneAsync(config.unitySceneName));
    }

    public void TransitionTo(string sceneId)
    {
        foreach (var cfg in configs)
        {
            if (cfg != null && cfg.sceneId == sceneId)
            {
                TransitionTo(cfg);
                return;
            }
        }

        Debug.LogWarning($"[SceneLoader] sceneId not found: '{sceneId}'");
    }

    public void TransitionTo(int index)
    {
        if (configs == null || index < 0 || index >= configs.Length)
        {
            Debug.LogError($"[SceneLoader] index out of range: {index}");
            return;
        }

        TransitionTo(configs[index]);
    }

    private IEnumerator LoadSceneAsync(string sceneName)
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        while (op != null && !op.isDone)
            yield return null;
    }

    private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _isLoading = false;

        if (scene.name == waitingSceneName)
        {
            _currentConfig = null;
            _currentSceneName = "WaitingScene";
            _pendingConfig = null;

            Debug.Log("[SceneLoader] WaitingScene loaded.");

            var waiting = FindAnyObjectByType<WaitingSceneController>();
            waiting?.OnWaitingSceneReady(this);
            return;
        }

        if (_pendingConfig == null || scene.name != _pendingConfig.unitySceneName)
            return;

        _currentConfig = _pendingConfig;
        _currentSceneName = _currentConfig.sceneName;
        _pendingConfig = null;

        Debug.Log($"[SceneLoader] Map scene loaded: {_currentConfig.sceneName}");

        if (recognizer != null)
        {
            var ext = FindAnyObjectByType<SkeletonFeatureExtractor>();
            if (ext != null)
            {
                recognizer.featureExtractor = ext;
                Debug.Log($"[SceneLoader] SkeletonFeatureExtractor reconnected: {ext.name}");
            }
        }

        var col = FindAnyObjectByType<TouchColliderHandler>();
        if (col != null)
        {
            colliderHandler = col;
            colliderHandler.dispatcher = FindAnyObjectByType<ActionDispatcher>();
            colliderHandler.recognizer = recognizer;
            colliderHandler.SetScene(_currentConfig);
            Debug.Log($"[SceneLoader] TouchColliderHandler reconnected: {col.name}");
        }

        if (storySceneManager == null)
            storySceneManager = FindAnyObjectByType<StorySceneManager>();

        storySceneManager?.OnMapSceneLoaded(_currentConfig);
    }
}
