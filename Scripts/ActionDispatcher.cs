using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 씬별 행동 감지 결과를 UnityEvent로 라우팅
///
/// ActionRecognizer / PushColliderHandler → OnActionDetected / OnColliderDetected
/// → SceneEventGroup 탐색 → 해당 씬·행동의 UnityEvent 발동
///
/// Inspector 설정:
///   Scene Events 배열에 씬별 이벤트 그룹을 등록
///   sceneId는 SceneActionConfig.sceneId와 동일하게 맞춰야 함
/// </summary>
public class ActionDispatcher : MonoBehaviour
{
    void Awake()
    {
        if (onActionWithDetail == null)
            onActionWithDetail = new UnityEvent<string, string>();
    }

    // ── 씬별 이벤트 그룹 ─────────────────────────────────────────────────
    [System.Serializable]
    public class ActionEvent
    {
        [Tooltip("SceneActionConfig.classA 또는 classB 와 동일한 문자열")]
        public string actionName;
        public UnityEvent onDetected;
    }

    [System.Serializable]
    public class SceneEventGroup
    {
        [Tooltip("SceneActionConfig.sceneId 와 동일하게 입력 (예: map_01)")]
        public string sceneId;

        [Tooltip("이 씬에서 발동할 행동별 이벤트")]
        public ActionEvent[] actionEvents;

        [Tooltip("이 씬에서 어떤 행동이든 감지되면 발동")]
        public UnityEvent onAnyAction;
    }

    [Header("씬별 이벤트 설정")]
    public SceneEventGroup[] sceneEvents;

    [Header("공통 이벤트 (씬 무관)")]
    [Tooltip("어떤 씬·행동이든 감지되면 발동")]
    public UnityEvent onAnyActionGlobal;

    [Tooltip("(sceneId, actionName) 포함 이벤트")]
    public UnityEvent<string, string> onActionWithDetail;

    // ── ActionRecognizer에서 호출 ────────────────────────────────────────

    /// <summary>
    /// AR 모델 트리거 (2순위)
    /// actionName : SceneActionConfig.classA 또는 classB
    /// sceneId    : SceneActionConfig.sceneId
    /// last20avg  : 방향 감지용 (필요 시 씬 매니저에서 활용)
    /// </summary>
    public void OnActionDetected(string actionName, float confidence,
                                 string sceneId, Vector3[] last20avg = null)
    {
        Debug.Log($"[ActionDispatcher] AR | sceneId={sceneId} action={actionName} conf={confidence:F3}");

        onAnyActionGlobal?.Invoke();
        onActionWithDetail?.Invoke(sceneId, actionName);

        DispatchToScene(sceneId, actionName);
    }

    // ── PushColliderHandler에서 호출 ─────────────────────────────────────

    /// <summary>
    /// 물리 Collider 트리거 (1순위)
    /// sceneId    : SceneActionConfig.sceneId
    /// actionName : 충돌 대상에 대응하는 classA 또는 classB
    /// </summary>
    public void OnColliderDetected(string sceneId, string actionName)
    {
        Debug.Log($"[ActionDispatcher] Collider | sceneId={sceneId} action={actionName}");

        onAnyActionGlobal?.Invoke();
        onActionWithDetail?.Invoke(sceneId, actionName);

        DispatchToScene(sceneId, actionName);
    }

    // ── 내부 라우팅 ──────────────────────────────────────────────────────

    private void DispatchToScene(string sceneId, string actionName)
    {
        foreach (var group in sceneEvents)
        {
            if (group.sceneId != sceneId) continue;

            group.onAnyAction?.Invoke();

            if (group.actionEvents == null) break;
            foreach (var ae in group.actionEvents)
            {
                if (ae.actionName == actionName)
                {
                    ae.onDetected?.Invoke();
                    break;
                }
            }
            break;
        }
    }

    // ── 하위 호환 (arm_raise) ────────────────────────────────────────────
    [Header("Legacy (arm_raise 하위 호환)")]
    public UnityEvent        OnArmRaise;
    public UnityEvent<float> OnArmRaiseWithConfidence;

    /// <summary>기존 arm_raise 방식 호환용</summary>
    public void OnActionDetected(string actionName, float confidence)
    {
        if (actionName != "arm_raise") return;
        OnArmRaise?.Invoke();
        OnArmRaiseWithConfidence?.Invoke(confidence);
    }
}
