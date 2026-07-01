# VoiceChat - 局域网语音通话

## 项目概述

一个基于 .NET 8 WPF 开发的局域网语音通话应用。支持房间创建、发现、加入，实时语音通信，音频设备切换等功能。

## 技术栈

- **框架**: .NET 8.0+ Windows + WPF（兼容 .NET 8/9/10）
- **音频**: NAudio (WASAPI) + Concentus (Opus 编解码)
- **网络**: TCP 信令 + UDP 语音传输 + UDP 广播发现
- **架构**: MVVM (CommunityToolkit.Mvvm)
- **测试**: xUnit + Coverlet（133 个测试）
- **目标平台**: Windows x64

## 项目结构

```plaintext
VoiceChat/
├── VoiceChat.Core/          # 核心库（音频、网络、会话）
│   ├── Audio/               # 音频采集/播放/编解码
│   ├── Network/             # TCP/UDP 通信
│   ├── Session/             # 房间管理
│   └── Models/              # 数据模型
├── VoiceChat.App/           # WPF 应用
│   ├── ViewModels/          # 视图模型
│   │   ├── MainViewModel.cs         # 主协调器
│   │   ├── AudioSettingsViewModel.cs # 音频设置
│   │   └── RoomSessionViewModel.cs   # 房间会话
│   ├── Converters/          # 值转换器
│   ├── MainWindow.xaml      # 主界面
│   └── App.xaml             # 应用入口
├── VoiceChat.Tests/         # 测试项目
├── publish_slim/            # 精简版发布输出
└── publish_full/            # 完整版发布输出
```

## 构建命令

### 开发构建

```bash
dotnet build -c Release
```

### 发布精简版（单文件 EXE）

```bash
dotnet publish VoiceChat.App -c Release -p:SelfContained=false -p:PublishSingleFile=true -o publish_slim
```

**输出**: `publish_slim/VoiceChat.App.exe` (~1.4MB)
**要求**: 目标机器需安装 .NET 8+ 桌面运行时

### 发布完整版（自包含，无需 .NET）

```bash
dotnet publish VoiceChat.App -c Release -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish_full
```

**输出**: `publish_full/VoiceChat.App.exe` (~150MB)
**要求**: 无需任何运行时，直接运行

## 测试

### 运行测试

```bash
# 运行全部测试
dotnet test VoiceChat.Tests -c Release

# 运行特定类别的测试
dotnet test VoiceChat.Tests -c Release --filter "FullyQualifiedName~RoomHostTests"
dotnet test VoiceChat.Tests -c Release --filter "FullyQualifiedName~UiTests"

# 只运行单元测试（不含 UI 测试）
dotnet test VoiceChat.Tests -c Release --filter "FullyQualifiedName!~UiTests"

# 详细输出
dotnet test VoiceChat.Tests -c Release --verbosity normal
```

### 测试项目结构

```plaintext
VoiceChat.Tests/
├── VoicePacketTests.cs       (7 tests)   - 包序列化/边界/安全
├── OpusCodecTests.cs         (8 tests)   - 编解码/PLC/Dispose
├── AudioPreprocessorTests.cs (8 tests)   - AGC/噪声门/线程安全
├── VoiceReceiverTests.cs     (11 tests)  - 包跟踪/乱序/环绕/统计
├── SignalingTests.cs         (7 tests)   - 连接/密码/广播/多客户端
├── SessionTests.cs           (8 tests)   - 模型/质量配置/消息序列化
├── VoiceQualityTests.cs      (25 tests)  - 音质档位/码率映射/Opus 兼容性
├── RoomHostTests.cs          (11 tests)  - 房间创建/关闭/成员/音频采集
├── RoomClientTests.cs        (5 tests)   - 客户端连接/断开/重连
├── E2ETests.cs               (5 tests)   - 端到端完整流程
├── StressTests.cs            (7 tests)   - 内存/性能/压力
└── UiTests.cs                (7 tests)   - UI 按钮状态（需要发布 EXE）
```

**总计**: 133 个测试

### 测试分类

| 类别 | 文件 | 说明 |
| --- | --- | --- |
| **单元测试** | VoicePacket, OpusCodec, AudioPreprocessor, VoiceQuality | 核心算法和数据结构 |
| **集成测试** | Signaling, VoiceReceiver, RoomHost, RoomClient | 组件间交互 |
| **端到端测试** | E2E | 完整用户流程 |
| **压力测试** | Stress | 长时间运行/高负载 |
| **UI 测试** | Ui | 需要发布的 EXE，使用 UIAutomation |

### 代码变更时必须更新测试

> **重要原则**: 每次修改 VoiceChat.Core 或 VoiceChat.App 代码后，必须检查并更新相应的测试。

**必须更新测试的场景**：

| 变更类型 | 需要更新的测试 |
| --- | --- |
| 新增/修改音频处理逻辑 | AudioPreprocessorTests, OpusCodecTests |
| 新增/修改网络协议 | SignalingTests, VoiceReceiverTests, VoicePacketTests |
| 新增/修改房间逻辑 | RoomHostTests, RoomClientTests |
| 新增/修改 UI 绑定 | UiTests |
| 修改音质配置 | VoiceQualityTests, SessionTests |
| 修改公开 API | 所有引用该 API 的测试 |

**示例流程**：

```bash
# 1. 修改代码
vim VoiceChat.Core/Audio/AudioCapture.cs

# 2. 检查相关测试是否仍然通过
dotnet test VoiceChat.Tests -c Release --filter "FullyQualifiedName~AudioPreprocessorTests"

# 3. 如果测试因 API 变更而失败，更新测试
vim VoiceChat.Tests/AudioPreprocessorTests.cs

# 4. 确保所有测试通过
dotnet test VoiceChat.Tests -c Release
```

## 功能说明

- **创建房间**: 房主创建房间，等待其他用户加入
- **发现房间**: 自动扫描局域网内的房间
- **加入房间**: 选择发现的房间加入
- **语音通信**: 实时 Opus 编码语音传输
- **音频设置**: 麦克风/扬声器切换、噪声门限、音质选择
- **成员管理**: 查看房间成员、静音、踢出

### 音质档位

| 档位 | 码率 | 描述 | 每帧大小 | 带宽 | CPU |
| --- | --- | --- | --- | --- | --- |
| 标准 | 64 kbps | FM 广播级 | ~160 B | 8 KB/s | 低 |
| 高清 | 96 kbps | 优秀语音 | ~240 B | 12 KB/s | 中 |
| **超清（默认）** | **128 kbps** | **人耳透明** | **~320 B** | **16 KB/s** | **中** |

**说明**: 房间内音质由房主决定，所有参与者使用相同音质。客户端的音质选择在加入房间后自动同步为房主的选择。

## 配置

- **信令端口**: TCP 动态分配
- **语音端口**: UDP 动态分配
- **发现端口**: UDP 9999（广播）
- **音频格式**: 48kHz 16-bit 单声道
- **编解码**: Opus (复杂度 3, VOIP 模式)

## 注意事项

- 首次运行可能触发 Windows 防火墙弹窗，需允许网络访问
- 需要音频设备（麦克风/扬声器）才能使用语音功能
- 同一局域网内的设备才能互相发现
- UI 测试需要先发布精简版：`dotnet publish VoiceChat.App -c Release -p:SelfContained=false -p:PublishSingleFile=true -o publish_slim`
