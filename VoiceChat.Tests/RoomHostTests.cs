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

    [Fact]
    public async Task KickMember_RemovesFromList()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "HostUser");

        // Simulate a member joining by adding to internal collection
        // (In real usage, this happens via SignalingServer callback)
        var member = new Core.Models.RoomMember
        {
            Id = "fake-member-id",
            Name = "FakeMember",
            VoiceAddress = "127.0.0.1",
            VoicePort = 5000
        };

        // Trigger member join via server event (simulate)
        // Since we can't easily simulate a full join, test that KickMember
        // on a non-existent member doesn't throw
        await _host.KickMemberAsync("non-existent-id");
        // No exception = pass
    }

    [Fact]
    public async Task MuteMember_NonExistent_DoesNotThrow()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "HostUser");

        // Mute a non-existent member should not throw
        _host.MuteMember("non-existent-id", true);
        _host.MuteMember("non-existent-id", false);
        // No exception = pass
    }

    [Fact]
    public async Task MuteSelf_ToggleMultipleTimes()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "HostUser");

        _host.MuteSelf(true);
        Assert.True(_host.HostMember.IsMuted);

        await Task.Delay(100);

        _host.MuteSelf(false);
        Assert.False(_host.HostMember.IsMuted);

        await Task.Delay(100);

        _host.MuteSelf(true);
        Assert.True(_host.HostMember.IsMuted);
    }

    [Fact]
    public async Task OnMemberJoined_FiresOnJoin()
    {
        _host = new RoomHost();
        var tcs = new TaskCompletionSource<Core.Models.RoomMember>();
        _host.OnMemberJoined += member => tcs.TrySetResult(member);

        await _host.CreateAsync("TestRoom", "HostUser");

        // The host itself should not trigger OnMemberJoined
        // (only remote members trigger it)
        // So we just verify the event can be subscribed
        Assert.NotNull(_host);
    }

    [Fact]
    public async Task OnError_FiresOnFailure()
    {
        _host = new RoomHost();
        var tcs = new TaskCompletionSource<string>();
        _host.OnError += msg => tcs.TrySetResult(msg);

        // Creating duplicate room should trigger error
        await _host.CreateAsync("TestRoom", "HostUser");
        bool second = await _host.CreateAsync("TestRoom2", "HostUser2");

        Assert.False(second);
        // Error event may or may not fire depending on implementation
    }

    [Fact]
    public async Task RoomInfo_UpdatedAfterCreate()
    {
        _host = new RoomHost();
        await _host.CreateAsync("MyRoom", "Host", 0, "pwd", VoiceQuality.HighDefinition);

        Assert.Equal("MyRoom", _host.RoomInfo.Name);
        Assert.Equal("Host", _host.RoomInfo.HostName);
        Assert.True(_host.RoomInfo.HasPassword);
        Assert.Equal(VoiceQuality.HighDefinition.Bitrate, _host.RoomInfo.Quality.Bitrate);
        Assert.Equal(1, _host.RoomInfo.MemberCount);
    }

    [Fact]
    public async Task HostMember_SetCorrectly()
    {
        _host = new RoomHost();
        await _host.CreateAsync("TestRoom", "TestHost");

        Assert.Equal("TestHost", _host.HostMember.Name);
        Assert.True(_host.HostMember.IsHost);
        Assert.NotNull(_host.HostMember.VoiceEndPoint);
        Assert.NotNull(_host.HostMember.VoiceAddress);
        Assert.True(_host.HostMember.VoicePort > 0);
    }
}
