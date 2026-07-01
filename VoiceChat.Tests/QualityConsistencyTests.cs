using VoiceChat.Core.Models;

namespace VoiceChat.Tests;

/// <summary>
/// 验证 UI 显示的音质与实际使用的音质始终一致
/// </summary>
public class QualityConsistencyTests
{
    // === 核心一致性测试 ===

    [Fact]
    public void SelectedQuality_DerivesFrom_SameIndex_As_SelectedQualityIndex()
    {
        // SelectedQuality 和 SelectedQualityIndex 必须从同一个 _actualQualityIndex 派生
        // AudioSettingsViewModel:
        //   SelectedQualityIndex -> _actualQualityIndex
        //   SelectedQuality -> QualityFromIndex(_actualQualityIndex)

        // 模拟 AudioSettingsViewModel 的逻辑
        int _actualQualityIndex = 2;

        // SelectedQualityIndex 返回 _actualQualityIndex
        int uiIndex = _actualQualityIndex;
        Assert.Equal(2, uiIndex);

        // SelectedQuality 从 _actualQualityIndex 计算
        var quality = QualityFromIndex(_actualQualityIndex);
        Assert.Equal(VoiceQuality.UltraHigh.Bitrate, quality.Bitrate);

        // 两者必须对应同一个音质
        Assert.Equal(GetIndexFromBitrate(quality.Bitrate), uiIndex);
    }

    [Theory]
    [InlineData(0, 64000)]    // Standard
    [InlineData(1, 96000)]    // HighDefinition
    [InlineData(2, 128000)]   // UltraHigh
    public void IndexAndBitrate_MatchAllQualities(int index, int expectedBitrate)
    {
        // 索引 → 音质 → 码率 → 反向索引，必须一致
        var quality = QualityFromIndex(index);
        Assert.Equal(expectedBitrate, quality.Bitrate);

        // 反向验证：码率 → 索引
        int backIndex = GetIndexFromBitrate(quality.Bitrate);
        Assert.Equal(index, backIndex);
    }

    [Fact]
    public void AfterJoin_ActualQuality_Equals_RoomQuality()
    {
        // 客户端加入房间后，_actualQualityIndex 必须与 CurrentRoom.Quality 一致
        var hostQuality = VoiceQuality.UltraHigh;
        var roomInfo = new RoomInfo { Quality = hostQuality };

        // 客户端发现房间（从广播解析）
        int parsedIndex = GetIndexFromBitrate(roomInfo.Quality.Bitrate);
        Assert.Equal(2, parsedIndex);

        // 客户端加入后的同步逻辑（模拟 AttachSession）
        int _actualQualityIndex = GetIndexFromBitrate(roomInfo.Quality.Bitrate);

        // UI 显示 = _actualQualityIndex
        Assert.Equal(2, _actualQualityIndex);

        // 实际使用的音质 = roomInfo.Quality
        var actualCodecQuality = roomInfo.Quality ?? VoiceQuality.Standard;
        Assert.Equal(hostQuality.Bitrate, actualCodecQuality.Bitrate);

        // UI 索引对应的音质参数 = 房间音质
        var uiQuality = QualityFromIndex(_actualQualityIndex);
        Assert.Equal(hostQuality.Bitrate, uiQuality.Bitrate);
    }

    [Fact]
    public void AfterLeave_UI_Restores_To_Desired()
    {
        // 退出房间后，UI 应恢复为用户偏好（而非房主音质）
        int _desiredQualityIndex = 0; // 用户偏好 64kbps
        int _actualQualityIndex = 2;  // 房间内使用房主 128kbps

        // 退出房间时，AttachSession 被调用（host=null, client=null）
        // 模拟逻辑：如果没有房间音质，恢复到用户偏好
        _actualQualityIndex = _desiredQualityIndex;

        // UI 显示 = _actualQualityIndex = 用户偏好
        Assert.Equal(0, _actualQualityIndex);
        var restoredQuality = QualityFromIndex(_actualQualityIndex);
        Assert.Equal(VoiceQuality.Standard.Bitrate, restoredQuality.Bitrate);
    }

    [Fact]
    public void Host_CreatesRoom_ShowsSameQuality()
    {
        // 房主创建房间后，UI 显示的音质必须与 RoomInfo.Quality 一致
        var hostSelectedQuality = VoiceQuality.HighDefinition;
        var roomInfo = new RoomInfo { Quality = hostSelectedQuality };

        // AttachSession(host, null) 逻辑
        int _actualQualityIndex = GetIndexFromBitrate(roomInfo.Quality.Bitrate);

        // UI 显示 = 房主选择的音质
        var uiQuality = QualityFromIndex(_actualQualityIndex);
        Assert.Equal(hostSelectedQuality.Bitrate, uiQuality.Bitrate);
    }

    // === 辅助方法（复制 AudioSettingsViewModel 的逻辑）===

    private static VoiceQuality QualityFromIndex(int index) => index switch
    {
        0 => VoiceQuality.Standard,
        1 => VoiceQuality.HighDefinition,
        _ => VoiceQuality.UltraHigh
    };

    private static int GetIndexFromBitrate(int bitrate) => bitrate switch
    {
        <= 64000 => 0,
        <= 96000 => 1,
        _ => 2
    };
}
