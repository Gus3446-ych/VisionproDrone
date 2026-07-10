# VisionProDrone 통신 아키텍처 및 명령어 매핑

## 개요

이 앱은 Unity 앱에서 DJI/Ryze Tello 드론으로 UDP 패킷을 직접 보내고, 드론에서 오는 상태/비디오 UDP 패킷을 별도 수신 루프로 처리한다.

핵심 흐름은 다음과 같다.

```text
Vision Pro / Unity UI
  -> WristDroneJoystickPanel 또는 WristDroneJoystickUI
  -> Tello.controllerState 또는 Tello.takeOff()/land()
  -> TelloLib.Tello
  -> UdpUser
  -> Tello Drone 192.168.10.1:8889

Tello Drone
  -> 상태/응답 UDP
  -> TelloLib.Tello client.Receive()
  -> Tello.state / Tello.onUpdate
  -> TextUpdater

Tello Drone
  -> H.264 UDP video, local port 6038
  -> TelloLib.Tello videoServer
  -> Tello.onVideoData
  -> TelloController
  -> TelloVideoTexture
  -> native decoder plugin
  -> Unity Texture2D
```

## 주요 컴포넌트

| 파일 | 역할 |
|---|---|
| `Assets/Scripts/TelloController.cs` | 앱 생명주기에서 드론 연결 시작/종료, 연결 후 초기 설정, 비디오 데이터 전달 |
| `Assets/Scripts/TelloLib/Tello.cs` | Tello UDP 프로토콜 구현, 명령 패킷 생성, 상태/비디오 수신, heartbeat |
| `Assets/Scripts/TelloLib/UDP.cs` | UDP client/listener 래퍼 |
| `Assets/Scripts/WristDroneJoystickPanel.cs` | 프리팹 기반 손목 조이스틱 UI, 직접 손 입력 처리, 버튼/축 명령 연결 |
| `Assets/Scripts/WristDroneJoystickPanelButton.cs` | 프리팹 조이스틱 버튼의 pointer/direct-hand press 처리 |
| `Assets/Scripts/WristDroneJoystickUI.cs` | 런타임 생성형 손목 조이스틱 UI |
| `Assets/Scripts/TextUpdater.cs` | `Tello.state`와 현재 누른 버튼을 TMP 텍스트로 표시 |
| `Assets/Scripts/TelloVideoTexture.cs` | H.264 NAL 데이터를 native decoder에 전달하고 Unity 텍스처에 업로드 |
| `Assets/Plugins/iOS/TelloVideoDecoder.mm` | iOS/visionOS 빌드에 포함되는 native H.264 decoder plugin 구현 |
| `Assets/Plugins/macOS/libTelloVideoDecoder.dylib` | macOS/Editor에서 사용하는 native decoder 동적 라이브러리 |

## 연결 방식

### 1. 연결 시작

`TelloController.Start()`에서 `Tello.startConnecting()`을 호출한다.

Unity Editor에서는 `connectInEditor`가 꺼져 있으면 연결하지 않는다.

```csharp
Tello.startConnecting();
```

### 2. UDP 연결 대상

`Tello.connect()`에서 드론 제어 포트로 UDP client를 연결한다.

```text
Drone IP: 192.168.10.1
Command/State UDP port: 8889
Video local UDP port: 6038
```

초기 연결 패킷:

```text
ASCII "conn_req:" + 0x96 0x17
```

드론에서 `conn_ack`를 받으면:

```text
connectionState = Connected
connected = true
startHeartbeat()
requestIframe()
```

### 3. 연결 후 초기 설정

`TelloController.Tello_onConnection()`에서 Connected 상태가 되면 아래 설정을 보낸다.

| 호출 | 목적 |
|---|---|
| `Tello.queryAttAngle()` | attitude angle 조회 |
| `Tello.setMaxHeight(50)` | 최대 고도 50 설정 |
| `Tello.setPicVidMode(1)` | video mode 설정 |
| `Tello.setVideoBitRate(VideoBitRateAuto)` | 비디오 bitrate auto |
| `Tello.requestIframe()` | I-frame 요청 |

## Heartbeat 및 조이스틱 제어

`Tello.startHeartbeat()`는 연결 상태에서 50ms마다 실행된다.

```text
Every 50 ms:
  Tello.sendControllerUpdate()

Every iFrameRate ticks:
  Tello.requestIframe()
```

`iFrameRate = 5`이므로 기본적으로 약 250ms마다 I-frame을 요청한다.

### 축 상태 저장

UI는 직접 UDP 패킷을 보내지 않고 `Tello.controllerState.setAxis(lx, ly, rx, ry)`로 현재 stick 값을 갱신한다.

```csharp
Tello.controllerState.setAxis(lx, ly, rx, ry);
```

이후 heartbeat가 `createJoyPacket(rx, ry, lx, ly, boost)`로 조이스틱 패킷을 만들어 보낸다.

### 축 의미

코드 기준 매핑은 다음과 같다.

| 축 | 의미 | 대표 UI |
|---|---|---|
| `lx` | 오른쪽 stick X, 좌/우 이동 | `LEFT`, `RIGHT`, keyboard `A/D` |
| `ly` | 오른쪽 stick Y, 전/후 이동 | `FORWARD/FWD`, `BACK`, keyboard `W/S` |
| `rx` | 왼쪽 stick X, yaw 좌/우 회전 | `YAW L`, `YAW R`, arrow left/right |
| `ry` | 왼쪽 stick Y, 상승/하강 | `UP`, `DOWN`, arrow up/down |

주의: `WristDroneJoystickPanel.ApplyDroneInput()`은 내부 stick 상태를 아래처럼 Tello 축으로 변환한다.

```csharp
float lx = s_RightStick.x * m_AxisStrength;
float ly = s_RightStick.y * m_AxisStrength;
float rx = s_LeftStick.x * m_AxisStrength;
float ry = s_LeftStick.y * m_AxisStrength;
Tello.controllerState.setAxis(lx, ly, rx, ry);
```

## UI 버튼 매핑

### 프리팹 기반 손목 패널

`Assets/Materials/Prefabs/LeftWristDroneJoystickPanel.prefab` 기준.

`Panel1`은 left stick 역할이고, 상승/하강/회전 제어에 사용된다.

| 버튼 | Panel | Axis | Tello 축 결과 | 기능 |
|---|---:|---:|---|---|
| `UP` | Panel1 | `(0, 1)` | `ry = +1` | 상승 |
| `DOWN` | Panel1 | `(0, -1)` | `ry = -1` | 하강 |
| `YAW L` | Panel1 | `(-1, 0)` | `rx = -1` | 좌회전 |
| `YAW R` | Panel1 | `(1, 0)` | `rx = +1` | 우회전 |
| `STOP` | Panel1 | `(0, 0)` | left stick zero | 왼쪽 stick 정지 |

`Panel2`는 right stick 역할이고, 전후/좌우 이동에 사용된다.

| 버튼 | Panel | Axis | Tello 축 결과 | 기능 |
|---|---:|---:|---|---|
| `FORWARD` | Panel2 | `(0, 1)` | `ly = +1` | 전진 |
| `BACK` | Panel2 | `(0, -1)` | `ly = -1` | 후진 |
| `LEFT` | Panel2 | `(-1, 0)` | `lx = -1` | 좌이동 |
| `RIGHT` | Panel2 | `(1, 0)` | `lx = +1` | 우이동 |

명령 버튼:

| 버튼 | 호출 | 기능 |
|---|---|---|
| `TAKE OFF` | `WristDroneJoystickPanel.TakeOff()` -> `Tello.takeOff()` | 이륙 |
| `LAND` | `WristDroneJoystickPanel.Land()` -> `ClearSticks()` -> `Tello.land()` | 정지 후 착륙 |

`LAND` 오브젝트에는 기존 프리팹상 `Button`과 `WristDroneJoystickPanelButton`이 같이 붙어 있으므로, 런타임에서 `Button`이 붙은 오브젝트의 조이스틱 컴포넌트는 비활성화한다. 이렇게 해야 직접 손 입력 경로에서 `LAND`가 조이스틱 버튼으로 먼저 잡히지 않고 command button으로 처리된다.

### 런타임 생성형 손목 패널

`WristDroneJoystickUI.CreatePanel()` 기준.

왼쪽 패널:

| 버튼 | Axis | 기능 |
|---|---:|---|
| `TAKE OFF` | command | `Tello.takeOff()` |
| `UP` | `(0, 1)` | 상승 |
| `DOWN` | `(0, -1)` | 하강 |
| `YAW L` | `(-1, 0)` | 좌회전 |
| `YAW R` | `(1, 0)` | 우회전 |
| `STOP` | `(0, 0)` | left stick 정지 |

오른쪽 패널:

| 버튼 | Axis | 기능 |
|---|---:|---|
| `LAND` | command | stick zero 정리 후 `Tello.land()` |
| `FWD` | `(0, 1)` | 전진 |
| `BACK` | `(0, -1)` | 후진 |
| `LEFT` | `(-1, 0)` | 좌이동 |
| `RIGHT` | `(1, 0)` | 우이동 |
| `STOP` | `(0, 0)` | right stick 정지 |

## 키보드 매핑

`TelloController.Update()` 기준.

| 입력 | 호출/축 | 기능 |
|---|---|---|
| `T` | `Tello.takeOff()` | 이륙 |
| `L` | `Tello.land()` | 착륙 |
| `UpArrow` | `ry = +1` | 상승 |
| `DownArrow` | `ry = -1` | 하강 |
| `RightArrow` | `rx = +1` | 우회전 |
| `LeftArrow` | `rx = -1` | 좌회전 |
| `W` | `ly = +1` | 전진 |
| `S` | `ly = -1` | 후진 |
| `D` | `lx = +1` | 우이동 |
| `A` | `lx = -1` | 좌이동 |

## Tello 명령어 패킷 매핑

아래 표는 `Tello.cs`의 public command 메서드 기준이다. 대부분 패킷은 `setPacketSequence()`와 `setPacketCRCs()`를 거친 뒤 `client.Send(packet)`으로 전송된다.

`사용 여부`는 현재 코드 검색 기준이다. `사용 중`은 앱 UI, 연결 초기화, heartbeat, 수신 처리 루프에서 직접 호출되는 명령이다. `미사용`은 public method로 존재하지만 현재 호출 경로가 없는 명령이다.

| 메서드 | Command ID bytes | Payload | 사용 여부 | 현재 호출 경로 | 설명 |
|---|---:|---|---|---|---|
| `takeOff()` | `0x54 0x00` | 없음 | 사용 중 | `TelloController` keyboard `T`, `WristDroneJoystickPanel.TakeOff`, `WristDroneJoystickUI.TakeOff`, takeoff slider | 일반 이륙 |
| `throwTakeOff()` | `0x5d 0x00` | 없음 | 미사용 | 없음 | throw takeoff |
| `land()` | `0x55 0x00` | `packet[9] = 0x00` | 사용 중 | `TelloController` keyboard `L`, `WristDroneJoystickPanel.Land`, `WristDroneJoystickUI.Land` | 착륙 |
| `requestIframe()` | `0x25 0x00` | 없음 | 사용 중 | 연결 직후, heartbeat 주기 호출, `TelloController.Tello_onConnection` | 비디오 I-frame 요청 |
| `setMaxHeight(int)` | `0x58 0x00` | height low/high | 사용 중 | `TelloController.Tello_onConnection`에서 `50` 설정 | 최대 고도 설정 |
| `queryAttAngle()` | `0x59 0x10` | query | 사용 중 | 연결 직후, `setAttAngle()` 후 refresh | attitude angle 조회 |
| `queryMaxHeight()` | `0x56 0x10` | query | 미사용 | 없음 | 최대 고도 조회 |
| `setAttAngle(float)` | `0x58 0x10` | float 4 bytes | 미사용 | 없음 | attitude angle 설정 |
| `setEis(int)` | `0x24 0x00` | u8 | 미사용 | 없음 | EIS 설정 |
| `setEIS(int)` | 없음 | 없음 | 미사용 | 없음 | 빈 stub method |
| `doFlip(int)` | `0x5c 0x00` | dir u8 | 미사용 | 없음 | flip |
| `setJpgQuality(int)` | `0x37 0x00` | quality u8 | 미사용 | 없음 | JPG 품질 |
| `setEV(int)` | `0x34 0x00` | `ev - 9` | 미사용 | 연결 후 호출 코드가 주석 처리됨 | 노출 보정 |
| `setVideoBitRate(int)` | `0x20 0x00` | rate u8 | 사용 중 | `TelloController.Tello_onConnection`, `VideoBitRateAuto` | 비디오 bitrate |
| `setVideoDynRate(int)` | `0x21 0x00` | rate u8 | 미사용 | 없음 | 동적 bitrate |
| `setVideoRecord(int)` | `0x32 0x00` | n u8 | 미사용 | 없음 | 녹화 제어 |
| `setPicVidMode(int)` | `0x31 0x00` | `1=video`, `0=photo` | 사용 중 | `TelloController.Tello_onConnection`, video mode `1` | 사진/비디오 모드 |
| `takePicture()` | `0x30 0x00` | 없음 | 미사용 | 없음 | 사진 촬영 |
| `sendAckFileSize()` | `0x62 0x00` | 없음 | 사용 중 | 수신 `cmdId == 98` JPG download start 처리 | 사진 파일 크기 ACK |
| `sendAckFilePiece(...)` | `0x63 0x00` | end flag, file id, piece id | 사용 중 | 수신 `cmdId == 99` JPG chunk 처리 | 사진 chunk ACK |
| `sendAckFileDone(int)` | `0x64 0x00` | size | 사용 중 | JPG 다운로드 완료 시 | 사진 다운로드 완료 ACK |
| `sendAckLog(...)` | `0x50 0x10` | cmd/id | 사용 중 | 수신 `cmdId == 4176` log header 처리 | log packet ACK |
| `sendAckLogConfig(...)` | `0x50 0x10` 기반 | cmd/id/config | 미사용 | 수신 `cmdId == 4178`에서 호출 코드 주석 처리됨 | log config ACK |

## 패킷 공통 처리

### Sequence

일반 명령 패킷은 `packet[7]`, `packet[8]`에 sequence를 little-endian으로 기록한다.

```csharp
packet[7] = (byte)(sequence & 0xff);
packet[8] = (byte)((sequence >> 8) & 0xff);
sequence++;
```

### CRC

패킷 전송 전 두 CRC를 계산한다.

```csharp
CRC.calcUCRC(packet, 4);
CRC.calcCrc(packet, packet.Length);
```

## 수신 데이터 매핑

`Tello.startListeners()`의 control receive loop에서 `cmdId = bytes[5] | bytes[6] << 8`로 수신 command id를 읽는다.

| cmdId | 처리 |
|---:|---|
| `26` | Wi-Fi strength 수신 |
| `53` | Light strength 관련, 현재 별도 처리 없음 |
| `86` | FlyData state packet. `Tello.state.set(bytes.Skip(9))` |
| `98` | JPG download start |
| `99` | JPG chunk |
| `100` | JPG 관련, 현재 별도 처리 없음 |
| `4176` | log header, ACK 전송 |
| `4177` | log data, `state.parseLog(...)` |
| `4178` | log config, 현재 대부분 주석 처리 |
| `4182` | max height response |
| `4185` | attitude angle response |

상태 표시 UI는 `TextUpdater`에서 아래 값을 사용한다.

| 표시 | 데이터 |
|---|---|
| `BAT` | `Tello.state.batteryPercentage` |
| `WIFI` | `Tello.state.wifiStrength` |
| `SPD` | `Tello.state.flySpeed` |
| `ALT` | `Tello.state.height` |
| `BTN` | `DroneInputStatus.CurrentPressedButtons` |

## 비디오 수신 구조

비디오는 control UDP와 별도 포트에서 수신한다.

```text
UdpListener videoServer = new UdpListener(6038)
```

수신 패킷에서 H.264 start code를 찾고 NAL 단위로 조립한다.

```text
UDP video packet
  -> FindH264StartCode
  -> ProcessH264VideoPacket
  -> AppendToNalBuffer
  -> FlushNalBuffer
  -> Tello.onVideoData(byte[])
```

`TelloController`는 `Tello.onVideoData`를 받아 `TelloVideoTexture.PutVideoData(data)`로 넘긴다.

`TelloVideoTexture`는 native plugin에 H.264 데이터를 전달하고, 디코딩된 RGBA frame을 `Texture2D`에 업로드한다.

## Plugin 사용 구조

이 프로젝트의 플러그인 사용은 드론 제어 UDP가 아니라 비디오 디코딩 경로에 집중되어 있다. 제어 명령은 C# UDP 코드가 직접 처리하고, H.264 video stream만 native plugin으로 넘겨 디코딩한다.

### Plugin 파일 구성

| 파일 | 플랫폼/역할 |
|---|---|
| `Assets/Plugins/iOS/TelloVideoDecoder.mm` | iOS/visionOS용 Objective-C++ source plugin. `VideoToolbox`를 사용해 H.264 NAL을 디코딩한다. |
| `Assets/Plugins/macOS/libTelloVideoDecoder.dylib` | macOS Editor 또는 macOS 실행 환경에서 `DllImport("TelloVideoDecoder")`로 로드되는 동적 라이브러리 |

### C#에서 plugin 로드 방식

`TelloVideoTexture.cs`는 플랫폼 조건에 따라 같은 native 함수를 다르게 import한다.

```csharp
#if (UNITY_IPHONE || UNITY_IOS || UNITY_VISIONOS || UNITY_WEBGL) && !UNITY_EDITOR
    [DllImport("__Internal")]
#else
    [DllImport("TelloVideoDecoder")]
#endif
```

의미:

| 조건 | 로딩 방식 |
|---|---|
| iOS/visionOS/WebGL device build | `__Internal`: 앱 바이너리에 링크된 native symbol 호출 |
| Editor/macOS 등 | `TelloVideoDecoder`: `Assets/Plugins/macOS/libTelloVideoDecoder.dylib` 로드 |

### Native function 매핑

| C# extern | Native 구현 | 사용 여부 | 역할 |
|---|---|---|---|
| `UnityPluginEnable()` | `TelloVideoDecoder.mm` | 사용 중 | decoder 활성화, 내부 상태 초기화 전 enabled flag 설정 |
| `UnityPluginDisable()` | `TelloVideoDecoder.mm` | 사용 중 | decoder 비활성화, session/frame/state 정리 |
| `SetTextureFromUnity(IntPtr, int, int)` | `TelloVideoDecoder.mm` | 사용 중 | Unity texture 크기 정보를 plugin에 전달 |
| `PutVideoDataFromUnity(byte[], int)` | `TelloVideoDecoder.mm` | 사용 중 | H.264 Annex-B NAL 데이터를 plugin에 전달 |
| `TelloVideoDecoderTryGetFrame(...)` | `TelloVideoDecoder.mm` | 사용 중 | 디코딩된 최신 RGBA frame을 C# buffer로 복사 |
| `TelloVideoDecoderGetStatus(...)` | `TelloVideoDecoder.mm` | 사용 중 | packet/NAL/decode 상태를 가져와 stall debug log에 사용 |
| `GetRenderEventFunc()` | `TelloVideoDecoder.mm` | 제한적 사용 | non-visionOS/non-editor device path에서 `GL.IssuePluginEvent`에 사용. 현재 native 구현은 `nullptr` 반환 |

### VideoToolbox 디코딩 흐름

`TelloVideoDecoder.mm` 내부 구조:

```text
PutVideoDataFromUnity
  -> PutAnnexBData
  -> FindNalRanges
  -> DecodeNal
     -> NAL type 7: SPS 저장
     -> NAL type 8: PPS 저장
     -> SPS/PPS 준비 후 EnsureDecoder
     -> VTDecompressionSessionDecodeFrame
  -> DecompressionOutputCallback
  -> BGRA pixel buffer를 RGBA byte buffer로 변환
  -> TelloVideoDecoderTryGetFrame에서 Unity C# buffer로 복사
  -> Texture2D.LoadRawTextureData
```

plugin은 UDP 수신을 하지 않는다. UDP 수신과 H.264 NAL 조립은 C#의 `Tello.cs`가 담당하고, plugin은 디코딩만 담당한다.

## 현재 구현상 주의점

1. `TelloController.Update()`의 키보드 입력은 매 프레임 `Tello.controllerState.setAxis(...)`를 호출한다. 에디터/테스트 중 UI 조이스틱과 동시에 쓰면 축 상태를 덮어쓸 수 있다.
2. `Tello.land()` 자체는 단발 command packet이다. `LAND` 호출 전에는 stick 값을 zero로 정리하도록 UI 레벨에서 처리한다.
3. `requestIframe()`은 sequence/CRC 재계산 없이 고정 패킷을 전송한다.
4. 비디오는 UDP 기반이라 packet loss가 가능하다. I-frame 요청은 heartbeat에서 주기적으로 보낸다.
5. Unity Editor에서는 `connectInEditor`가 켜져 있어야 연결을 시작한다.
6. live H.264 decoding은 device build 또는 macOS editor 조건부 native plugin 경로에 의존한다.
7. `GetRenderEventFunc()` native 구현은 현재 `nullptr`를 반환한다. visionOS/editor path는 `TryGetFrame` pull 방식으로 동작하므로 영향이 작지만, 다른 native render-event path를 확장하려면 구현이 필요하다.
