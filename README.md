# Capstone-Design-II
2026-1 캡스톤디자인 II 및 졸업전시

## 0. 프로젝트 세팅

### Azure Kinect SDK 설치

- [ ]  **Windows Azure Kinect SDK 1.4.1** 설치
    - [Azure Kinect SDK 1.4.1 다운로드](https://github.com/microsoft/Azure-Kinect-Sensor-SDK/blob/develop/docs/usage.md)
- [ ]  **Windows Azure Kinect Body Tracking SDK 1.1.2** 설치
    - [Body Tracking SDK 다운로드](https://learn.microsoft.com/ko-kr/previous-versions/azure/kinect-dk/body-sdk-download) (반드시 Sensor SDK 이후에 설치)
- [ ]  **Visual C++ Redistributable for Visual Studio 2015** 설치
    - [Visual C++ Redistributable for Visual Studio 2015 다운로드](https://www.microsoft.com/en-us/download/details.aspx?id=48145)
- [ ]  **Microsoft Azure Kinect Samples** 클론
    - https://github.com/microsoft/Azure-Kinect-Samples
- [ ]  **Unity (URP) 6.4** 프로젝트에서 동작 확인

1. 전체 파이프라인 구조

Azure Kinect (백그라운드 스레드 · 30fps)
    └─ BackgroundData { Bodies[], ColorImage, DepthImage }
         ↓  main_single.Update() 매 프레임
    TrackerHandler_single
    └─ absoluteJointRotations[32] · jointPositions[32]
         ↓                    ↓
    PuppetAvatar        SkeletonFeatureExtractor
    (리타겟팅)           └─ 슬라이딩 윈도우 30프레임
                              float[630] → OnWindowReady
                               ↓  N프레임마다
                      ActionRecognizer (ONNX MLP)
                      └─ IDLE → RAISED(트리거 1회) → COOLDOWN → IDLE
                               ↓
                      ActionDispatcher
                      └─ OnArmRaise.Invoke()
                               ↓  ← 씬 매니저 연결 지점
                      SceneManager.OnPlayerAction()

   <img width="672" height="514" alt="image" src="https://github.com/user-attachments/assets/d362ad74-6deb-4e4a-a199-f5345023447a" />
