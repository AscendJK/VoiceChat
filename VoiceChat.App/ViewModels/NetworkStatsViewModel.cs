using CommunityToolkit.Mvvm.ComponentModel;

namespace VoiceChat.App.ViewModels;

public partial class NetworkStatsViewModel : ObservableObject
{
    private string _latencyText = "—";
    public string LatencyText
    {
        get => _latencyText;
        set => SetProperty(ref _latencyText, value);
    }

    private double _packetLossRate;
    public double PacketLossRate
    {
        get => _packetLossRate;
        set => SetProperty(ref _packetLossRate, value);
    }

    private long _sentBytesPerSecond;
    public long SentBytesPerSecond
    {
        get => _sentBytesPerSecond;
        set => SetProperty(ref _sentBytesPerSecond, value);
    }

    private long _receivedBytesPerSecond;
    public long ReceivedBytesPerSecond
    {
        get => _receivedBytesPerSecond;
        set => SetProperty(ref _receivedBytesPerSecond, value);
    }

    private long _sentPackets;
    public long SentPackets
    {
        get => _sentPackets;
        set => SetProperty(ref _sentPackets, value);
    }

    private long _receivedPackets;
    public long ReceivedPackets
    {
        get => _receivedPackets;
        set => SetProperty(ref _receivedPackets, value);
    }

    private long _lostPackets;
    public long LostPackets
    {
        get => _lostPackets;
        set => SetProperty(ref _lostPackets, value);
    }

    private bool _isStatsVisible;
    public bool IsStatsVisible
    {
        get => _isStatsVisible;
        set => SetProperty(ref _isStatsVisible, value);
    }

    private long _lastSentBytes;
    private long _lastReceivedBytes;

    public void UpdateStats(long totalSentBytes, long totalReceivedBytes, long totalSentPackets, long totalLostPackets, long totalReceivedPackets, int latencyMs = -1)
    {
        SentBytesPerSecond = (totalSentBytes - _lastSentBytes);
        ReceivedBytesPerSecond = (totalReceivedBytes - _lastReceivedBytes);
        _lastSentBytes = totalSentBytes;
        _lastReceivedBytes = totalReceivedBytes;

        SentPackets = totalSentPackets;
        ReceivedPackets = totalReceivedPackets;
        LostPackets = totalLostPackets;
        LatencyText = latencyMs >= 0 ? $"{latencyMs}" : "—";

        long totalExpected = totalReceivedPackets + totalLostPackets;
        PacketLossRate = totalExpected > 0 ? (double)totalLostPackets / totalExpected * 100 : 0;
    }

    public void Reset()
    {
        LatencyText = "—";
        PacketLossRate = 0;
        SentBytesPerSecond = 0;
        ReceivedBytesPerSecond = 0;
        SentPackets = 0;
        ReceivedPackets = 0;
        LostPackets = 0;
        _lastSentBytes = 0;
        _lastReceivedBytes = 0;
    }
}
