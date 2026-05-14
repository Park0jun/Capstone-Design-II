using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public class MapInteractionController : MonoBehaviour
{
    [Header("References")]
    public ActionDispatcher dispatcher;
    public StorySceneManager storySceneManager;
    public ImageGenClient imageGenClient;
    public ActionRecognizer recognizer;
    public TouchColliderHandler colliderHandler;

    [Header("Timer UI (Optional)")]
    public GameObject timerRoot;
    public Image timerFillImage;
    public Text timerText;
    public TextMeshProUGUI timerTextTMP;
    public bool hideTimerWhenInactive = true;
    public bool fillCountsUp = false;

    [Header("Runtime")]
    [SerializeField] private string _sceneId = "-";
    [SerializeField] private string _lastModelAction = "-";
    [SerializeField] private string _finalAction = "-";
    [SerializeField] private bool _isActive = false;
    [SerializeField] private float _elapsed = 0f;

    private SceneActionConfig _config;
    private UnityAction<string, string> _actionListener;

    void Awake()
    {
        _actionListener = OnActionWithDetail;
    }

    void Start()
    {
        ResolveReferences();
        if (dispatcher != null)
            dispatcher.onActionWithDetail.AddListener(_actionListener);
    }

    void OnDestroy()
    {
        if (dispatcher != null)
            dispatcher.onActionWithDetail.RemoveListener(_actionListener);
    }

    void Update()
    {
        if (!_isActive || _config == null) return;

        _elapsed += Time.deltaTime;
        UpdateTimerUI();

        if (_elapsed >= _config.interactionTimeoutSeconds)
            ConfirmFinalAction(_config.unknownActionName);
    }

    public void BeginInteraction(SceneActionConfig config)
    {
        ResolveReferences();

        _config = config;
        _sceneId = config != null ? config.sceneId : "-";
        _lastModelAction = "-";
        _finalAction = "-";
        _elapsed = 0f;
        _isActive = config != null;

        if (recognizer != null) recognizer.enabled = true;
        if (colliderHandler != null && config != null) colliderHandler.SetScene(config);
        SetTimerVisible(_isActive);
        UpdateTimerUI();

        Debug.Log($"[MapInteraction] begin: {_sceneId}");
    }

    public void EndInteraction()
    {
        _isActive = false;
        if (recognizer != null) recognizer.enabled = false;
        SetTimerVisible(false);
        Debug.Log($"[MapInteraction] end: {_sceneId}/{_finalAction}");
    }

    private void OnActionWithDetail(string sceneId, string actionName)
    {
        if (!_isActive || _config == null) return;
        if (sceneId != _config.sceneId) return;
        if (string.IsNullOrEmpty(actionName)) return;

        if (_config.GetReaction(actionName) != null)
        {
            ConfirmFinalAction(actionName);
            return;
        }

        if (actionName == _config.positiveModelActionName)
        {
            _lastModelAction = actionName;
            if (!string.IsNullOrEmpty(_config.modelPositiveResult))
                ConfirmFinalAction(_config.modelPositiveResult);
        }
    }

    private void ConfirmFinalAction(string actionName)
    {
        if (!_isActive || _config == null) return;
        if (string.IsNullOrEmpty(actionName)) actionName = _config.unknownActionName;

        _finalAction = actionName;
        _isActive = false;

        if (recognizer != null) recognizer.enabled = false;
        SetTimerVisible(false);
        imageGenClient?.SendTrigger(_config.sceneId, actionName);
        storySceneManager?.OnFinalActionSelected(_config.sceneId, actionName);

        Debug.Log($"[MapInteraction] final: {_config.sceneId}/{actionName}");
    }

    private void ResolveReferences()
    {
        if (dispatcher == null) dispatcher = FindAnyObjectByType<ActionDispatcher>();
        if (storySceneManager == null) storySceneManager = FindAnyObjectByType<StorySceneManager>();
        if (imageGenClient == null) imageGenClient = FindAnyObjectByType<ImageGenClient>();
        if (recognizer == null) recognizer = FindAnyObjectByType<ActionRecognizer>();
        if (colliderHandler == null) colliderHandler = FindAnyObjectByType<TouchColliderHandler>();
    }

    private void UpdateTimerUI()
    {
        if (_config == null) return;

        float duration = Mathf.Max(_config.interactionTimeoutSeconds, 0.01f);
        float remaining = Mathf.Max(0f, duration - _elapsed);
        float normalized = remaining / duration;

        if (timerFillImage != null)
            timerFillImage.fillAmount = fillCountsUp ? 1f - normalized : normalized;

        if (timerText != null)
            timerText.text = Mathf.CeilToInt(remaining).ToString();

        if (timerTextTMP != null)
            timerTextTMP.text = Mathf.CeilToInt(remaining).ToString();
    }

    private void SetTimerVisible(bool visible)
    {
        if (!hideTimerWhenInactive && !visible) return;
        if (timerRoot != null)
            timerRoot.SetActive(visible);
    }

}

