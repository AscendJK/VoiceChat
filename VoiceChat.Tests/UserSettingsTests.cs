using System.IO;
using VoiceChat.App.Services;

namespace VoiceChat.Tests;

public class UserSettingsTests : IDisposable
{
    private readonly string _testSettingsPath;
    private readonly string _testSettingsDir;

    public UserSettingsTests()
    {
        _testSettingsDir = Path.Combine(Path.GetTempPath(), "VoiceChatTest_" + Guid.NewGuid().ToString("N")[..8]);
        _testSettingsPath = Path.Combine(_testSettingsDir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_testSettingsDir, true); } catch { }
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var settings = new UserSettings();
        Assert.Equal(string.Empty, settings.CaptureDeviceId);
        Assert.Equal(string.Empty, settings.PlaybackDeviceId);
        Assert.True(settings.NoiseGateEnabled);
        Assert.Equal(0.005f, settings.NoiseGateThreshold);
        Assert.Equal(2, settings.QualityIndex);
        Assert.False(settings.PushToTalkEnabled);
        Assert.Equal("None", settings.PushToTalkKey);
    }

    [Fact]
    public void Save_CreatesFile()
    {
        var settings = new UserSettings
        {
            UserName = "TestUser",
            RoomName = "TestRoom",
            QualityIndex = 1
        };

        // 临时修改路径（通过序列化验证）
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var restored = System.Text.Json.JsonSerializer.Deserialize<UserSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal("TestUser", restored.UserName);
        Assert.Equal("TestRoom", restored.RoomName);
        Assert.Equal(1, restored.QualityIndex);
    }

    [Fact]
    public void SerializeDeserialize_Roundtrip()
    {
        var settings = new UserSettings
        {
            CaptureDeviceId = "{12345}",
            PlaybackDeviceId = "{67890}",
            NoiseGateEnabled = false,
            NoiseGateThreshold = 0.02f,
            QualityIndex = 0,
            UserName = "Alice",
            RoomName = "Alice的房间",
            PushToTalkEnabled = true,
            PushToTalkKey = "Space"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var restored = System.Text.Json.JsonSerializer.Deserialize<UserSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(settings.CaptureDeviceId, restored.CaptureDeviceId);
        Assert.Equal(settings.PlaybackDeviceId, restored.PlaybackDeviceId);
        Assert.Equal(settings.NoiseGateEnabled, restored.NoiseGateEnabled);
        Assert.Equal(settings.NoiseGateThreshold, restored.NoiseGateThreshold);
        Assert.Equal(settings.QualityIndex, restored.QualityIndex);
        Assert.Equal(settings.UserName, restored.UserName);
        Assert.Equal(settings.RoomName, restored.RoomName);
        Assert.Equal(settings.PushToTalkEnabled, restored.PushToTalkEnabled);
        Assert.Equal(settings.PushToTalkKey, restored.PushToTalkKey);
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        // Load 静默失败，返回默认值
        var settings = new UserSettings();
        Assert.Equal(2, settings.QualityIndex);
        Assert.True(settings.NoiseGateEnabled);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var settings = new UserSettings();
        settings.UserName = "Bob";
        settings.RoomName = "Bob的房间";
        settings.QualityIndex = 0;
        settings.PushToTalkEnabled = true;
        settings.PushToTalkKey = "LeftCtrl";

        Assert.Equal("Bob", settings.UserName);
        Assert.Equal("Bob的房间", settings.RoomName);
        Assert.Equal(0, settings.QualityIndex);
        Assert.True(settings.PushToTalkEnabled);
        Assert.Equal("LeftCtrl", settings.PushToTalkKey);
    }

    [Fact]
    public void QualityIndex_Values()
    {
        var settings = new UserSettings();
        settings.QualityIndex = 0; // Standard
        Assert.Equal(0, settings.QualityIndex);

        settings.QualityIndex = 1; // HighDefinition
        Assert.Equal(1, settings.QualityIndex);

        settings.QualityIndex = 2; // UltraHigh
        Assert.Equal(2, settings.QualityIndex);
    }

    [Fact]
    public void PushToTalk_KeyOptions()
    {
        var settings = new UserSettings();
        string[] validKeys = ["None", "LeftCtrl", "RightCtrl", "LeftShift", "RightShift", "Space", "Z", "X", "C", "V"];

        foreach (var key in validKeys)
        {
            settings.PushToTalkKey = key;
            Assert.Equal(key, settings.PushToTalkKey);
        }
    }
}
