using VoiceChat.App.ViewModels;

namespace VoiceChat.Tests;

public class NetworkStatsViewModelTests
{
    [Fact]
    public void DefaultValues_AreZero()
    {
        var vm = new NetworkStatsViewModel();
        Assert.Equal(0, vm.Latency);
        Assert.Equal(0, vm.PacketLossRate);
        Assert.Equal(0, vm.SentBytesPerSecond);
        Assert.Equal(0, vm.ReceivedBytesPerSecond);
        Assert.Equal(0, vm.SentPackets);
        Assert.Equal(0, vm.ReceivedPackets);
        Assert.Equal(0, vm.LostPackets);
        Assert.False(vm.IsStatsVisible);
    }

    [Fact]
    public void UpdateStats_CalculatesRates()
    {
        var vm = new NetworkStatsViewModel();

        // 第一次调用：设置基准
        vm.UpdateStats(1000, 2000, 10, 1, 99);

        Assert.Equal(1000, vm.SentBytesPerSecond);
        Assert.Equal(2000, vm.ReceivedBytesPerSecond);
        Assert.Equal(10, vm.SentPackets);
        Assert.Equal(99, vm.ReceivedPackets);
        Assert.Equal(1, vm.LostPackets);

        // 第二次调用：计算差值
        vm.UpdateStats(1500, 2500, 20, 2, 198);

        Assert.Equal(500, vm.SentBytesPerSecond);   // 1500 - 1000
        Assert.Equal(500, vm.ReceivedBytesPerSecond); // 2500 - 2000
        Assert.Equal(20, vm.SentPackets);
        Assert.Equal(198, vm.ReceivedPackets);
        Assert.Equal(2, vm.LostPackets);
    }

    [Fact]
    public void UpdateStats_CalculatesPacketLossRate()
    {
        var vm = new NetworkStatsViewModel();

        // 10% 丢包率：10 lost / (90 received + 10 lost) = 10%
        vm.UpdateStats(0, 0, 0, 10, 90);
        Assert.Equal(10.0, vm.PacketLossRate, 1);

        // 0% 丢包率
        vm.UpdateStats(0, 0, 0, 0, 100);
        Assert.Equal(0.0, vm.PacketLossRate);

        // 50% 丢包率
        vm.UpdateStats(0, 0, 0, 50, 50);
        Assert.Equal(50.0, vm.PacketLossRate, 1);
    }

    [Fact]
    public void UpdateStats_ZeroPackets_NoDivideByZero()
    {
        var vm = new NetworkStatsViewModel();
        vm.UpdateStats(0, 0, 0, 0, 0);
        Assert.Equal(0.0, vm.PacketLossRate);
    }

    [Fact]
    public void Reset_ClearsAll()
    {
        var vm = new NetworkStatsViewModel();
        vm.UpdateStats(1000, 2000, 10, 1, 99);
        vm.Latency = 50;
        vm.IsStatsVisible = true;

        vm.Reset();

        Assert.Equal(0, vm.Latency);
        Assert.Equal(0, vm.PacketLossRate);
        Assert.Equal(0, vm.SentBytesPerSecond);
        Assert.Equal(0, vm.ReceivedBytesPerSecond);
        Assert.Equal(0, vm.SentPackets);
        Assert.Equal(0, vm.ReceivedPackets);
        Assert.Equal(0, vm.LostPackets);
    }

    [Fact]
    public void Reset_ThenUpdateStats_StartsFromZero()
    {
        var vm = new NetworkStatsViewModel();
        vm.UpdateStats(1000, 2000, 10, 1, 99);
        vm.Reset();

        vm.UpdateStats(500, 800, 5, 0, 50);

        Assert.Equal(500, vm.SentBytesPerSecond);
        Assert.Equal(800, vm.ReceivedBytesPerSecond);
    }

    [Fact]
    public void IsStatsVisible_CanBeToggled()
    {
        var vm = new NetworkStatsViewModel();
        Assert.False(vm.IsStatsVisible);

        vm.IsStatsVisible = true;
        Assert.True(vm.IsStatsVisible);

        vm.IsStatsVisible = false;
        Assert.False(vm.IsStatsVisible);
    }

    [Fact]
    public void PropertyChanged_FiredOnUpdate()
    {
        var vm = new NetworkStatsViewModel();
        var changedProps = new List<string>();
        vm.PropertyChanged += (s, e) => changedProps.Add(e.PropertyName!);

        vm.UpdateStats(100, 200, 5, 1, 49);

        Assert.Contains(nameof(NetworkStatsViewModel.SentBytesPerSecond), changedProps);
        Assert.Contains(nameof(NetworkStatsViewModel.ReceivedBytesPerSecond), changedProps);
        Assert.Contains(nameof(NetworkStatsViewModel.SentPackets), changedProps);
        Assert.Contains(nameof(NetworkStatsViewModel.ReceivedPackets), changedProps);
        Assert.Contains(nameof(NetworkStatsViewModel.LostPackets), changedProps);
        Assert.Contains(nameof(NetworkStatsViewModel.PacketLossRate), changedProps);
    }

    [Fact]
    public void UpdateStats_LargeValues_Handled()
    {
        var vm = new NetworkStatsViewModel();
        long maxVal = long.MaxValue / 2;
        vm.UpdateStats(maxVal, maxVal, maxVal / 1000, maxVal / 10000, maxVal / 1000);

        Assert.Equal(maxVal, vm.SentBytesPerSecond);
        Assert.Equal(maxVal, vm.ReceivedBytesPerSecond);
    }

    [Fact]
    public void UpdateStats_NegativeDelta_Handled()
    {
        var vm = new NetworkStatsViewModel();
        vm.UpdateStats(1000, 1000, 10, 0, 100);
        // 模拟计数器重置（负差值）
        vm.UpdateStats(100, 100, 5, 0, 50);

        Assert.Equal(-900, vm.SentBytesPerSecond);
        Assert.Equal(-900, vm.ReceivedBytesPerSecond);
    }
}
