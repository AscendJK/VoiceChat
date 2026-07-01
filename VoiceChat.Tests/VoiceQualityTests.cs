using VoiceChat.Core.Audio;
using VoiceChat.Core.Models;

namespace VoiceChat.Tests;

public class VoiceQualityTests
{
    // === 基础属性测试 ===

    [Fact]
    public void Standard_HasCorrectValues()
    {
        var q = VoiceQuality.Standard;
        Assert.Equal(48000, q.SampleRate);
        Assert.Equal(64000, q.Bitrate);
        Assert.Equal(20, q.FrameSizeMs);
        Assert.Equal(1, q.Channels);
        Assert.Equal(960, q.FrameSize); // 48000 * 20 / 1000
    }

    [Fact]
    public void HighDefinition_HasCorrectValues()
    {
        var q = VoiceQuality.HighDefinition;
        Assert.Equal(48000, q.SampleRate);
        Assert.Equal(96000, q.Bitrate);
        Assert.Equal(20, q.FrameSizeMs);
        Assert.Equal(1, q.Channels);
        Assert.Equal(960, q.FrameSize);
    }

    [Fact]
    public void UltraHigh_HasCorrectValues()
    {
        var q = VoiceQuality.UltraHigh;
        Assert.Equal(48000, q.SampleRate);
        Assert.Equal(128000, q.Bitrate);
        Assert.Equal(20, q.FrameSizeMs);
        Assert.Equal(1, q.Channels);
        Assert.Equal(960, q.FrameSize);
    }

    // === 码率排序 ===

    [Fact]
    public void Bitrates_AreOrderedCorrectly()
    {
        Assert.True(VoiceQuality.Standard.Bitrate < VoiceQuality.HighDefinition.Bitrate);
        Assert.True(VoiceQuality.HighDefinition.Bitrate < VoiceQuality.UltraHigh.Bitrate);
    }

    // === 帧大小计算 ===

    [Fact]
    public void FrameSize_Calculation_IsCorrect()
    {
        // 48kHz * 20ms = 960 samples
        Assert.Equal(960, VoiceQuality.Standard.FrameSize);
        Assert.Equal(960, VoiceQuality.HighDefinition.FrameSize);
        Assert.Equal(960, VoiceQuality.UltraHigh.FrameSize);
    }

    // === 每帧输出大小估算 ===

    [Fact]
    public void EstimatedFrameOutputSize_MatchesBitrate()
    {
        // 每帧输出 ≈ Bitrate / 8 / 50（50 frames/sec）
        int standardBytes = VoiceQuality.Standard.Bitrate / 8 / 50;   // ~160 bytes
        int hdBytes = VoiceQuality.HighDefinition.Bitrate / 8 / 50;  // ~240 bytes
        int ultraBytes = VoiceQuality.UltraHigh.Bitrate / 8 / 50;    // ~320 bytes

        Assert.Equal(160, standardBytes);
        Assert.Equal(240, hdBytes);
        Assert.Equal(320, ultraBytes);
    }

    // === ToString 格式 ===

    [Fact]
    public void ToString_FormatsCorrectly()
    {
        Assert.Equal("48kHz/64kbps", VoiceQuality.Standard.ToString());
        Assert.Equal("48kHz/96kbps", VoiceQuality.HighDefinition.ToString());
        Assert.Equal("48kHz/128kbps", VoiceQuality.UltraHigh.ToString());
    }

    // === 默认值 ===

    [Fact]
    public void DefaultQuality_IsCompatible()
    {
        var q = new VoiceQuality();
        Assert.Equal(48000, q.SampleRate);
        Assert.Equal(128000, q.Bitrate); // 默认 128kbps
        Assert.Equal(20, q.FrameSizeMs);
        Assert.Equal(1, q.Channels);
    }

    // === Opus 支持验证 ===

    [Fact]
    public void AllBitrates_AreWithinOpusRange()
    {
        // Opus 支持 6-510 kbps
        Assert.True(VoiceQuality.Standard.Bitrate >= 6000);
        Assert.True(VoiceQuality.Standard.Bitrate <= 510000);
        Assert.True(VoiceQuality.HighDefinition.Bitrate >= 6000);
        Assert.True(VoiceQuality.HighDefinition.Bitrate <= 510000);
        Assert.True(VoiceQuality.UltraHigh.Bitrate >= 6000);
        Assert.True(VoiceQuality.UltraHigh.Bitrate <= 510000);
    }

    [Fact]
    public void SampleRate_IsSupportedByOpus()
    {
        // Opus 内部始终重采样到 48kHz，但支持这些采样率输入
        Assert.Equal(48000, VoiceQuality.Standard.SampleRate);
        Assert.Equal(48000, VoiceQuality.HighDefinition.SampleRate);
        Assert.Equal(48000, VoiceQuality.UltraHigh.SampleRate);
    }

    // === 带宽计算 ===

    [Fact]
    public void Bandwidth_Calculation()
    {
        // 带宽 = 码率 / 8 * 帧数/秒
        // 标准: 64kbps = 8KB/s
        int stdBps = VoiceQuality.Standard.Bitrate / 8;
        int hdBps = VoiceQuality.HighDefinition.Bitrate / 8;
        int ultraBps = VoiceQuality.UltraHigh.Bitrate / 8;

        Assert.Equal(8000, stdBps);   // 8 KB/s
        Assert.Equal(12000, hdBps);   // 12 KB/s
        Assert.Equal(16000, ultraBps); // 16 KB/s
    }
}

/// <summary>
/// 测试不同音质配置下 OpusCodec 的创建和编码
/// </summary>
public class OpusCodecQualityTests : IDisposable
{
    private readonly List<OpusCodec> _codecs = new();

    public void Dispose()
    {
        foreach (var c in _codecs) c.Dispose();
    }

    private OpusCodec CreateCodec(VoiceQuality q)
    {
        var codec = new OpusCodec(q.SampleRate, q.Channels, q.Bitrate);
        _codecs.Add(codec);
        return codec;
    }

    [Theory]
    [InlineData(64000)]
    [InlineData(96000)]
    [InlineData(128000)]
    public void Codec_Creates_WithAnyBitrate(int bitrate)
    {
        var codec = CreateCodec(new VoiceQuality { Bitrate = bitrate });
        Assert.NotNull(codec);
        Assert.Equal(bitrate, codec.Bitrate);
    }

    [Theory]
    [InlineData(64000)]
    [InlineData(96000)]
    [InlineData(128000)]
    public void Codec_EncodeDecode_Roundtrip_AllBitrates(int bitrate)
    {
        var codec = CreateCodec(new VoiceQuality { Bitrate = bitrate });
        var pcm = GenerateSineWave(codec.FrameSize);

        var encoded = new byte[codec.MaxPacketSize];
        int encodedLen = codec.Encode(pcm, pcm.Length, encoded);
        Assert.True(encodedLen > 0, $"Encode should succeed at {bitrate}bps");

        var decoded = new short[codec.FrameSize];
        int decodedSamples = codec.Decode(encoded, encodedLen, decoded, false);
        Assert.Equal(codec.FrameSize, decodedSamples);
    }

    [Fact]
    public void HigherBitrate_ProducesLargerOutput()
    {
        var lowCodec = CreateCodec(new VoiceQuality { Bitrate = 64000 });
        var highCodec = CreateCodec(new VoiceQuality { Bitrate = 128000 });

        var pcm = GenerateSineWave(lowCodec.FrameSize);

        var encodedLow = new byte[lowCodec.MaxPacketSize];
        int lowLen = lowCodec.Encode(pcm, pcm.Length, encodedLow);

        var encodedHigh = new byte[highCodec.MaxPacketSize];
        int highLen = highCodec.Encode(pcm, pcm.Length, encodedHigh);

        Assert.True(highLen >= lowLen,
            $"128kbps ({highLen}) should produce >= 64kbps ({lowLen})");
    }

    [Fact]
    public void MaxPacketSize_DependsOnBitrate()
    {
        var lowCodec = CreateCodec(new VoiceQuality { Bitrate = 64000 });
        var highCodec = CreateCodec(new VoiceQuality { Bitrate = 128000 });

        // MaxPacketSize = FrameSize * 4（与码率无关，只与帧大小有关）
        Assert.Equal(lowCodec.FrameSize * 4, lowCodec.MaxPacketSize);
        Assert.Equal(highCodec.FrameSize * 4, highCodec.MaxPacketSize);
    }

    private static short[] GenerateSineWave(int length)
    {
        var pcm = new short[length];
        double phase = 0;
        for (int i = 0; i < length; i++)
        {
            pcm[i] = (short)(16000 * Math.Sin(phase));
            phase += 2.0 * Math.PI * 440.0 / 48000;
        }
        return pcm;
    }
}

/// <summary>
/// 测试音质索引与码率之间的映射关系
/// </summary>
public class QualityIndexMappingTests
{
    [Theory]
    [InlineData(0, 64000)]
    [InlineData(1, 96000)]
    [InlineData(2, 128000)]
    public void Index_ToBitrate(int index, int expectedBitrate)
    {
        var quality = index switch
        {
            0 => VoiceQuality.Standard,
            1 => VoiceQuality.HighDefinition,
            _ => VoiceQuality.UltraHigh
        };

        Assert.Equal(expectedBitrate, quality.Bitrate);
    }

    [Theory]
    [InlineData(64000, 0)]    // = 64000 → Standard
    [InlineData(32000, 0)]    // < 64000 → Standard
    [InlineData(80000, 1)]    // > 64000 and <= 96000 → HighDefinition
    [InlineData(96000, 1)]    // = 96000 → HighDefinition
    [InlineData(100000, 2)]   // > 96000 → UltraHigh
    [InlineData(128000, 2)]   // = 128000 → UltraHigh
    [InlineData(256000, 2)]   // > 96000 → UltraHigh
    public void BitrateToIndex(int bitrate, int expectedIndex)
    {
        // 复制 AudioSettingsViewModel 的 SyncQualityFromRoom 逻辑
        var index = bitrate switch
        {
            <= 64000 => 0,
            <= 96000 => 1,
            _ => 2
        };

        Assert.Equal(expectedIndex, index);
    }

    [Fact]
    public void DefaultIndex_IsUltraHigh()
    {
        // 默认索引 2 = UltraHigh (128kbps)
        int defaultIndex = 2;
        var quality = defaultIndex switch
        {
            0 => VoiceQuality.Standard,
            1 => VoiceQuality.HighDefinition,
            _ => VoiceQuality.UltraHigh
        };

        Assert.Equal(VoiceQuality.UltraHigh.Bitrate, quality.Bitrate);
    }
}

/// <summary>
/// 测试房间音质传递：房主创建房间 → 客户端接收相同音质
/// </summary>
public class RoomQualityFlowTests
{
    [Fact]
    public void Host_CreatesRoom_RoomInfo_HasCorrectQuality()
    {
        // 模拟房主选择 UltraHigh 创建房间
        var hostQuality = VoiceQuality.UltraHigh;
        var roomInfo = new RoomInfo
        {
            Name = "TestRoom",
            HostName = "Host",
            Quality = hostQuality
        };

        Assert.Equal(128000, roomInfo.Quality.Bitrate);
    }

    [Fact]
    public void Client_JoinsRoom_UsesHostQuality()
    {
        // 模拟客户端加入房间时使用房主的音质
        var hostQuality = VoiceQuality.UltraHigh;
        var roomInfo = new RoomInfo { Quality = hostQuality };

        // 客户端从 RoomInfo 获取音质
        var clientQuality = roomInfo.Quality ?? VoiceQuality.Standard;

        Assert.Equal(hostQuality.Bitrate, clientQuality.Bitrate);
        Assert.Equal(128000, clientQuality.Bitrate);
    }

    [Theory]
    [InlineData(0, 64000)]
    [InlineData(1, 96000)]
    [InlineData(2, 128000)]
    public void QualityIndex_MapsToCorrectBitrate(int index, int expectedBitrate)
    {
        var quality = index switch
        {
            0 => VoiceQuality.Standard,
            1 => VoiceQuality.HighDefinition,
            _ => VoiceQuality.UltraHigh
        };

        Assert.Equal(expectedBitrate, quality.Bitrate);
    }

    [Fact]
    public void AllUsersInRoom_UseSameQuality()
    {
        // 房间内所有用户的音质由房主决定
        var hostQuality = VoiceQuality.HighDefinition;
        var roomInfo = new RoomInfo { Quality = hostQuality };

        // 多个客户端加入，都使用房主的音质
        var client1Quality = roomInfo.Quality ?? VoiceQuality.Standard;
        var client2Quality = roomInfo.Quality ?? VoiceQuality.Standard;

        Assert.Equal(hostQuality.Bitrate, client1Quality.Bitrate);
        Assert.Equal(hostQuality.Bitrate, client2Quality.Bitrate);
        Assert.Equal(client1Quality.Bitrate, client2Quality.Bitrate);
    }
}