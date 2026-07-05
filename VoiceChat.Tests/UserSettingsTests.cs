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
            RoomName = "Alice的房间"
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
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
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

        Assert.Equal("Bob", settings.UserName);
        Assert.Equal("Bob的房间", settings.RoomName);
        Assert.Equal(0, settings.QualityIndex);
    }

    [Fact]
    public void QualityIndex_Values()
    {
        var settings = new UserSettings();
        settings.QualityIndex = 0;
        Assert.Equal(0, settings.QualityIndex);

        settings.QualityIndex = 1;
        Assert.Equal(1, settings.QualityIndex);

        settings.QualityIndex = 2;
        Assert.Equal(2, settings.QualityIndex);
    }
}
