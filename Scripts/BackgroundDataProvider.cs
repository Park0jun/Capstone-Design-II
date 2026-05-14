using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 백그라운드 스레드 데이터 공급자 추상 베이스
/// - 서브클래스(SkeletalTrackingProvider)가 RunBackgroundThreadAsync()를 구현
/// - 더블 버퍼 스왑으로 메인 스레드와 락 최소화
/// - 에디터 종료 / 앱 종료 시 자동 CancellationToken 취소
/// </summary>
public abstract class BackgroundDataProvider : IDisposable
{
    private BackgroundData         _frameData  = new BackgroundData();
    private bool                   _hasNew     = false;
    private readonly object        _lock       = new object();
    private CancellationTokenSource _cts;

    public bool IsRunning { get; set; } = false;

    protected BackgroundDataProvider(int id)
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.quitting += Dispose;
#endif
        _cts = new CancellationTokenSource();
        Task.Run(() => RunBackgroundThreadAsync(id, _cts.Token));
    }

    protected abstract void RunBackgroundThreadAsync(int id, CancellationToken token);

    /// <summary>백그라운드 스레드에서 호출 — 최신 프레임 데이터를 교환</summary>
    public void SetCurrentFrameData(ref BackgroundData current)
    {
        lock (_lock)
        {
            (current, _frameData) = (_frameData, current);
            _hasNew = true;
        }
    }

    /// <summary>
    /// 메인 스레드에서 호출 — 새 프레임이 있으면 교환 후 true 반환
    /// 새 프레임이 없으면 false (buffer는 변경 없음)
    /// </summary>
    public bool GetCurrentFrameData(ref BackgroundData buffer)
    {
        lock (_lock)
        {
            if (!_hasNew) return false;
            (buffer, _frameData) = (_frameData, buffer);
            _hasNew = false;
            return true;
        }
    }

    public void Dispose()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.quitting -= Dispose;
#endif
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
