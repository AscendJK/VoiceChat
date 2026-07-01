# 🎙️ 局域网语音通话

一款轻量级的 Windows 局域网多人语音通话软件，低延迟、低占用、流畅不卡顿。

---

## ✨ 功能特性

| 功能 | 说明 |
| --- | --- |
| 🏠 **创建房间** | 一台电脑创建语音房间，自动向局域网广播，默认房间名带用户名 |
| 🔍 **扫描加入** | 同一局域网内的其他电脑自动扫描发现房间，一键加入 |
| 🗣️ **多人语音** | 房间内所有成员互相实时通话（Opus 编码 + UDP 传输） |
| 🟢 **说话指示** | 每个用户名称前有绿色圆点，说话时亮起，800ms 自动熄灭 |
| 🎤 **静音自己** | 随时开关麦克风 |
| 🔇 **噪声门限** | 可开关的噪声抑制 + 自动增益控制（AGC），阈值可调 |
| 🔊 **设备切换** | 通话中可随时切换麦克风和扬声器设备，无需重启 |
| 👥 **成员列表** | 显示房间内其他成员（不包含自己），支持音量和发言状态显示 |
| 🔇 **静音别人** | 屏蔽指定成员的语音 |
| 🚪 **踢出成员** | 房主可踢出房间内的成员（不能踢自己） |
| 💥 **解散房间** | 房主可一键解散房间，所有成员自动断开 |
| 🎚️ **音质选择** | 创建房间可选标准（64kbps）、高清（96kbps）或超清（128kbps），默认超清 |
| 🚀 **启动闪屏** | 启动时立即出现蓝色闪屏，后台加载完成后自动消失 |
| 🔌 **热插拔** | 通话中切换音频设备自动生效 |

---

## 📊 技术指标

| 指标 | 数值 |
| --- | --- |
| CPU 占用 | 约 2-4%（单核，取决于成员数量） |
| 内存占用 | < 30 MB |
| 端到端延迟 | 30-50 ms |
| 带宽占用 | 8/12/16 KB/s（64/96/128 kbps Opus） |
| 音频格式 | 48kHz / 单声道 / IEEE Float |
| 前处理 | 噪声门限 + 自动增益控制（AGC） |
| 编码器 | Opus (VOIP 模式, PLC 丢包补偿, 复杂度 3) |
| 传输协议 | UDP 语音 + TCP 信令 |
| 心跳 | TCP 心跳 10 秒间隔，超时自动清理 |
| 房间发现 | UDP 广播（每 1000ms），2 秒未更新自动过期 |

---

## 🚀 快速开始

### 系统要求

- **操作系统**: Windows 10/11（64 位）
- **运行时**: .NET 8 Desktop Runtime 或更高版本（.NET 8/9/10 均可）

### 方式一：直接运行 EXE

1. 下载 `publish_slim/VoiceChat.App.exe`
2. **需要安装 .NET 8+ Desktop Runtime**（仅首次）
   - 下载地址：[https://dotnet.microsoft.com/zh-cn/download](https://dotnet.microsoft.com/zh-cn/download)
   - 支持 .NET 8、9、10 及更高版本
   - 安装一次即可，以后更新只需替换 EXE
3. Windows 防火墙弹窗时点击「允许访问」
4. 启动后出现蓝色闪屏，稍等片刻即进入主界面

### 方式二：从源码编译

```bash
# 需要 .NET 8 SDK 或更高版本
cd VoiceChat
dotnet run --project VoiceChat.App
```

### 方式三：打包发布

```bash
# 轻量版（需 .NET 8+ 运行时，约 1.4MB）
dotnet publish VoiceChat.App -c Release -p:SelfContained=false -p:PublishSingleFile=true -o publish_slim

# 完整版（不需运行时，约 150MB）
dotnet publish VoiceChat.App -c Release -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish_full
```

---

## 📖 使用说明

### 创建房间（房主）

1. 打开软件
2. 可选调整 **音质**（标准 64kbps / 高清 96kbps / 超清 128kbps），默认超清
3. 输入 **房间名**（默认"{用户名}的房间"）和 **昵称**
4. 点击 **创建房间**
5. 等待其他成员扫描加入

### 加入房间（成员）

1. 打开软件，点击左侧 **刷新** 扫描局域网内的房间
2. 从列表中选择目标房间
3. 输入 **昵称**
4. 点击 **加入房间**

### 音频设置

- **🎤 麦克风设置**：选择采集设备，查看输入音量，调节噪声门限
- **🔊 扬声器设置**：选择播放设备，支持通话中热切换
- **🔇 噪声门限**：勾选启用，阈值滑块可调节敏感度（默认 0.005）

### 语音控制

| 功能 | 说明 | 谁能用 |
| --- | --- | --- |
| 🎤 静音 / 取消静音 | 开关自己的麦克风 | 所有人 |
| 🟢 说话指示 | 用户名称前绿色圆点表示正在说话 | 所有人可见 |
| 🚪 解散房间 | 踢出所有成员并关闭房间 | 仅房主 |
| 🚪 离开房间 | 退出当前房间 | 仅成员 |
| 踢出 | 踢出指定成员（不能踢自己） | 仅房主 |

### 设备切换

通话中切换 Windows 音频设备后，在软件右侧下拉框选择新设备即可：

- **麦克风**：通话中实时切换
- **扬声器**：通话中实时切换

---

## ⚠️ 注意事项

1. **网络环境**：所有设备必须在同一局域网（同一 WiFi 或有线网络）内
2. **运行时依赖**：轻量版需要安装 .NET 8+ Desktop Runtime（.NET 8/9/10 均可，仅首次，公司电脑可能需要 IT 权限）
3. **防火墙**：首次运行时 Windows 防火墙会弹窗授权，请点击「允许访问」
4. **端口**：软件使用 UDP 9999 端口进行房间广播，TCP 动态端口用于信令，UDP 动态端口用于语音
5. **音频设备**：请确保麦克风和扬声器已正确连接并启用
6. **找不到房间？**：
   - 检查两台电脑是否在同一网段（如都是 192.168.1.x）
   - 检查 Windows 防火墙是否阻止了 UDP 9999 端口
   - 尝试关闭第三方防火墙 / 杀毒软件
   - 点击「刷新扫描」按钮重新扫描

---

## 🏗️ 项目结构

```plaintext
VoiceChat/
├── VoiceChat.Core/                    # 核心类库
│   ├── Audio/                         # 音频采集/播放/前处理/编解码
│   │   ├── AudioCapture.cs            # WASAPI 麦克风采集
│   │   ├── AudioPlayer.cs             # WASAPI 扬声器播放（多路混音）
│   │   ├── AudioPreprocessor.cs       # 噪声门限 + AGC
│   │   └── OpusCodec.cs               # Opus 编解码 + PLC 丢包补偿
│   ├── Models/                        # 数据模型
│   │   ├── RoomInfo.cs                # 房间信息
│   │   ├── RoomMember.cs              # 成员信息
│   │   ├── SignalingMessage.cs        # 信令消息协议
│   │   ├── VoicePacket.cs             # 语音数据包协议
│   │   └── VoiceQuality.cs            # 音质预设（标准/高清/超清）
│   ├── Network/                       # 网络通信
│   │   ├── SignalingServer.cs         # TCP 信令服务器（房主）
│   │   ├── SignalingClient.cs         # TCP 信令客户端（成员）
│   │   ├── VoiceSender.cs             # UDP 语音发送
│   │   ├── VoiceReceiver.cs           # UDP 语音接收（丢包检测/乱序重排）
│   │   ├── UdpRoomDiscoveryServer.cs  # UDP 房间广播（房主）
│   │   └── UdpBroadcasterScanner.cs   # UDP 房间扫描（成员）
│   └── Session/                       # 会话管理
│       ├── RoomHost.cs                # 房主会话
│       └── RoomClient.cs              # 成员会话
├── VoiceChat.App/                     # WPF 桌面界面
│   ├── App.xaml / App.xaml.cs         # 应用程序入口 + 闪屏管理
│   ├── MainWindow.xaml / .cs          # 主界面
│   ├── SplashWindow.xaml / .cs        # 启动闪屏
│   ├── Converters.cs                  # 值转换器（含说话指示颜色转换）
│   ├── ViewModels/
│   │   └── MainViewModel.cs           # 视图模型（MVVM）
│   └── VoiceChat.App.ico              # 应用程序图标
├── VoiceChat.Tests/                   # 测试项目（133 个测试）
│   ├── VoicePacketTests.cs             # 包序列化/边界/安全
│   ├── OpusCodecTests.cs               # 编解码/PLC/Dispose
│   ├── AudioPreprocessorTests.cs       # AGC/噪声门/线程安全
│   ├── VoiceReceiverTests.cs           # 包跟踪/乱序/环绕
│   ├── SignalingTests.cs               # 协议/密码/广播
│   ├── SessionTests.cs                 # 模型/质量配置/消息
│   ├── VoiceQualityTests.cs            # 音质档位/码率映射
│   ├── RoomHostTests.cs                # 房间生命周期
│   ├── RoomClientTests.cs              # 客户端生命周期
│   ├── E2ETests.cs                     # 端到端完整流程
│   ├── StressTests.cs                  # 内存/性能/压力
│   ├── UiTests.cs                      # UI 按钮状态
│   └── PropertyChangedOrderTests.cs    # 绑定时序
├── publish_slim/                       # 精简版发布输出
│   └── VoiceChat.App.exe              # 单文件可执行文件
└── README.md
```

---

## 🧪 测试

```bash
# 运行全部测试
dotnet test VoiceChat.Tests -c Release

# 运行特定类别
dotnet test VoiceChat.Tests -c Release --filter "FullyQualifiedName~RoomHostTests"
dotnet test VoiceChat.Tests -c Release --filter "FullyQualifiedName~UiTests"

# 只运行单元测试（不含 UI 测试）
dotnet test VoiceChat.Tests -c Release --filter "FullyQualifiedName!~UiTests"
```

**注意**: UI 测试需要先发布精简版：

```bash
dotnet publish VoiceChat.App -c Release -p:SelfContained=false -p:PublishSingleFile=true -o publish_slim
```

---

## 🔧 技术架构

```plaintext
┌────────────────┐     TCP 信令     ┌────────────────┐
│   房主端        │◄──────────────►│   成员端         │
│  ┌──────────┐  │  Join/Leave/   │  ┌──────────┐   │
│  │ RoomHost │  │  MemberList    │  │RoomClient│   │
│  └────┬─────┘  │  Heartbeat     │  └────┬─────┘   │
│       │        │                 │       │         │
│  ┌────┴─────┐  │    UDP 语音    │  ┌────┴─────┐   │
│  │Voice     │◄──────────────────►│  │Voice     │   │
│  │Sender    │  │  48kHz Opus     │  │Sender    │   │
│  └──────────┘  │                 │  └──────────┘   │
│  ┌──────────┐  │                 │  ┌──────────┐   │
│  │Voice     │  │                 │  │Voice     │   │
│  │Receiver  │◄──────────────────►│  │Receiver  │   │
│  └──────────┘  │                 │  └──────────┘   │
│  ┌──────────┐  │                 │  ┌──────────┐   │
│  │UDP 广播   │─── UDP 广播 ───────►│UDP 扫描   │   │
│  └──────────┘  │  每1000ms       │  └──────────┘   │
└────────────────┘                 └────────────────┘
```

### 音频处理链

```plaintext
麦克风 → WASAPI → 噪声门限 → AGC → VAD静音检测
    → Opus编码(64/96/128kbps, 复杂度3) → UDP发送
UDP接收 → Opus解码(PLC) → 混音器 → WASAPI → 扬声器
```

### 说话指示器

```plaintext
收到语音包 → OnUserSpeaking(userId)
    → 查找 UI 成员 → MarkSpeaking()
        → 绿色圆点亮起
        → 800ms 定时器复位
```

### 信令协议

所有信令消息使用 JSON 序列化，4 字节小端长度前缀 + UTF-8 编码消息体。

| 消息类型 | 方向 | 说明 |
| --- | --- | --- |
| `JoinRequest` | 成员→服务器 | 加入请求（含用户名 + 语音端口） |
| `JoinResponse` | 服务器→成员 | 加入结果（含成员列表 + 房主信息） |
| `LeaveRequest` | 成员→服务器 | 离开请求 |
| `MemberJoined` | 服务器→全体 | 通知新成员加入 |
| `MemberLeft` | 服务器→全体 | 通知成员离开 |
| `MuteSelf` / `UnmuteSelf` | 成员→服务器→全体 | 静音状态同步 |
| `Heartbeat` / `HeartbeatAck` | 成员↔服务器 | 心跳保活 |
| `RoomDissolved` | 服务器→全体 | 房间已解散 |

---

## 📄 许可证

MIT License

---

## 🙏 致谢

- [NAudio](https://github.com/naudio/NAudio) — Windows 音频库（WASAPI）
- [Concentus](https://github.com/lostromb/concentus) — 纯托管 Opus 编解码器
