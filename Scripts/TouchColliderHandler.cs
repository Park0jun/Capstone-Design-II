using UnityEngine;

/// <summary>
/// 臾쇰━ Collider 湲곕컲 touch ?몃━嫄?(1?쒖쐞)
///
/// ?ㅼ젙:
///   愿?뚭컼 ?꾨컮? ?ㅻⅨ?먮ぉ 蹂몄뿉 遺李?///   SphereCollider (IsTrigger=true) ?④퍡 異붽?
///
/// ???꾪솚 ??SetScene()?쇰줈 ?꾩옱 ???ㅼ젙 媛깆떊
/// ??useColliderTrigger=false ?ъ뿉?쒕뒗 ?먮룞?쇰줈 媛먯? 鍮꾪솢??/// </summary>
public class TouchColliderHandler : MonoBehaviour
{
    [Header("References")]
    public ActionDispatcher dispatcher;
    public ActionRecognizer recognizer;

    [Header("Current Config")]
    [Tooltip("Updated by SceneLoader.SetScene(). Collider detection is disabled when this is empty.")]
    public SceneActionConfig currentConfig;

    [Header("Overlap Probe")]
    [Tooltip("Also detect targets by checking colliders around this hand every physics tick. This catches missed OnTriggerEnter events.")]
    public bool useOverlapProbe = true;
    public float probeRadius = 0.25f;
    public LayerMask probeLayers = ~0;
    public bool logIgnoredHits = false;

    [Header("Runtime (ReadOnly)")]
    [SerializeField] private float  _cooldownTimer = 0f;
    [SerializeField] private string _lastHit       = "-";
    [SerializeField] private bool   _isActive      = false;

    // ?? Unity ?앸챸二쇨린 ????????????????????????????????????????????????????

    void Start()
    {
        // DontDestroyOnLoad ?ㅻ툕?앺듃 ?먮룞 ?먯깋
        // Inspector?먯꽌 鍮꾩뼱?덉뼱??SceneLoader媛 ??濡쒕뱶 ???ъ뿰寃고빐二쇱?留?        // ?뱀떆 紐⑤? ?곹솴???꾪빐 Start?먯꽌???먯깋
        if (dispatcher == null)
            dispatcher = FindAnyObjectByType<ActionDispatcher>();
        if (recognizer == null)
            recognizer = FindAnyObjectByType<ActionRecognizer>();
    }

    void Update()
    {
        if (_cooldownTimer > 0f)
            _cooldownTimer -= Time.deltaTime;
    }

    void FixedUpdate()
    {
        if (!useOverlapProbe) return;
        if (!_isActive || currentConfig == null) return;
        if (_cooldownTimer > 0f) return;

        Collider[] hits = Physics.OverlapSphere(transform.position, probeRadius, probeLayers, QueryTriggerInteraction.Collide);
        foreach (Collider hit in hits)
        {
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
            if (TryDispatchHit(hit, "OverlapSphere")) break;
        }
    }

    // ?? ???꾪솚 API ???????????????????????????????????????????????????????

    /// <summary>???꾪솚 ??SceneLoader?먯꽌 ?몄텧</summary>
    public void SetScene(SceneActionConfig config)
    {
        currentConfig  = config;
        _isActive      = config != null && config.useColliderTrigger;
        _cooldownTimer = 0f;
        Debug.Log($"[TouchCollider] Scene={config?.sceneName} | Active={_isActive}");
    }

    // ?? 異⑸룎 媛먯? ?????????????????????????????????????????????????????????

    void OnTriggerEnter(Collider other)
    {
        if (!_isActive || currentConfig == null) return;
        if (_cooldownTimer > 0f) return;

        TryDispatchHit(other, "OnTriggerEnter");
    }

    void OnTriggerStay(Collider other)
    {
        if (!_isActive || currentConfig == null) return;
        if (_cooldownTimer > 0f) return;

        TryDispatchHit(other, "OnTriggerStay");
    }

    private bool TryDispatchHit(Collider other, string source)
    {
        if (other == null || currentConfig == null) return false;

        string tag = other.tag;
        string tagA = string.IsNullOrWhiteSpace(currentConfig.colliderTagA) ? "" : currentConfig.colliderTagA.Trim();
        string tagB = string.IsNullOrWhiteSpace(currentConfig.colliderTagB) ? "" : currentConfig.colliderTagB.Trim();

        bool isTagA = !string.IsNullOrEmpty(tagA) && tag == tagA;
        bool isTagB = !currentConfig.singleClass && !string.IsNullOrEmpty(tagB) && tag == tagB;

        if (!isTagA && !isTagB)
        {
            if (logIgnoredHits)
                Debug.Log($"[TouchCollider] Ignored {source}: object={other.name} tag={tag} expected={tagA}/{tagB}");
            return false;
        }

        string actionName = isTagA
            ? (!string.IsNullOrEmpty(currentConfig.colliderResultA) ? currentConfig.colliderResultA : currentConfig.classA)
            : (!string.IsNullOrEmpty(currentConfig.colliderResultB) ? currentConfig.colliderResultB : currentConfig.classB);

        _lastHit       = actionName;
        _cooldownTimer = currentConfig.cooldownSeconds;

        Debug.Log($"[TouchCollider] Hit({source}) object={other.name} tag={tag} -> action={actionName} scene={currentConfig.sceneName}");

        recognizer?.NotifyColliderTrigger();
        dispatcher?.OnColliderDetected(currentConfig.sceneId, actionName);
        return true;
    }

    // ?? ?붾쾭洹?GUI ????????????????????????????????????????????????????????

}

