using VoiceChat.Core.Audio;
using VoiceChat.Core.Models;
using VoiceChat.Core.Session;

namespace VoiceChat.Tests;

public class RoomHostTests : IDisposable
{
    private RoomHost? _host;

    public void Dispose()
    {
        try { _host?.CloseAsync().Wait(TimeSpan.FromSeconds(3)); } catch { }
        try { _host?.Dispose(); } catch { }
        _host = null;
    }

    [Fact]
    public async Task CreateAsync_Success_SetsIsRunning()
    {
        _host = new RoomHost();
        bool ok = await _host.CreateAsync("TestRoom", "HostUser", 0, null, VoiceQuality.Standard);

        Assert.True(ok);
        Assert.True(_host.IsRunning);
        Assert.NotNull(_host.HostMember);
        Assert.Equal("HostUser", _host.HostMember.Name);
        Assert.NotNull(_host.RoomInfo);
        Assert.Equal("TestRoom", _host.RoomInfo.Name);
    }

    [Fact]
    public async Task CreateAsync_Duplicate_ReturnsFalse()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "HostUser");

        bool second = await _host.CreateAsync("TestRoom2", "HostUser2");
        Assert.False(second);
        Assert.True(_host.IsRunning);
    }

    [Fact]
    public async Task CreateAsync_WithPassword_SetsHasPassword()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "HostUser", 0, "secret");

        Assert.True(_host.RoomInfo.HasPassword);
    }

    [Fact]
    public async Task CloseAsync_StopsServer()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "HostUser");

        await _host.CloseAsync();

        Assert.False(_host.IsRunning);
    }

    [Fact]
    public async Task CloseAsync_BeforeCreate_DoesNotThrow()
    {
        _host = new RoomHost();
        await _host.CloseAsync(); // Should not throw
    }

    [Fact]
    public async Task Dispose_AfterCreate_ReleasesResources()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "HostUser");

        _host.Dispose(); // Should not throw
    }

    [Fact]
    public void Dispose_WithoutCreate_DoesNotThrow()
    {
        _host = new RoomHost();
        _host.Dispose(); // Should not throw
    }

    [Fact]
    public async Task Members_InitiallyContainsHost()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "HostUser");

        var members = _host.Members;
        Assert.Single(members);
        Assert.Equal("HostUser", members[0].Name);
        Assert.True(members[0].IsHost);
    }

    [Fact]
    public async Task GetAudioCapture_AfterCreate_ReturnsCapture()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "HostUser");

        var capture = _host.GetAudioCapture();
        Assert.NotNull(capture);
    }

    [Fact]
    public async Task SetUserVolume_Works()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "HostUser");

        _host.SetUserVolume("some-id", 0.5f);
        // No exception = pass
    }

    [Fact]
    public async Task MuteSelf_SetsFlag()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "HostUser");

        _host.MuteSelf(true);
        Assert.True(_host.HostMember.IsMuted);

        // Wait for NAudio to fully stop before restarting
        await Task.Delay(100);
        _host.MuteSelf(false);
        Assert.False(_host.HostMember.IsMuted);
    }

    [Fact]
    public async Task MultipleDispose_DoesNotThrow()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "HostUser");

        _host.Dispose();
        _host.Dispose(); // Second dispose should not throw
    }
}
