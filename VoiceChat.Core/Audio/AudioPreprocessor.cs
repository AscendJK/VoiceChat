namespace VoiceChat.Core.Audio;

/// <summary>
/// 音频前处理器 - 噪声抑制 + 自动增益控制（单次遍历优化）
/// </summary>
public class AudioPreprocessor
{
    /// <summary>
    /// 噪声门限（RMS低于此值视为静音）
    /// </summary>
    public float NoiseGateThreshold { get; set; } = 0.005f;

    /// <summary>
    /// 是否启用噪声门限
    /// </summary>
    public bool NoiseGateEnabled { get; set; } = true;

    // AGC 目标RMS
    private float _targetRms = 0.15f;

    // AGC 攻击/释放时间
    private float _agcAttack = 0.99f;
    private float _agcRelease = 0.999f;
    private float _currentGain = 1.0f;

    // 噪声估计
    private float _noiseEstimate = 0.0f;
    private readonly float _noiseAlpha = 0.99f;

    // VAD 静音阈值
    private const float VadThreshold = 0.008f;

    // 防止并发回调导致状态损坏
    private readonly object _processLock = new();

    /// <summary>
    /// 检测是否为静音（short[] 版本）
    /// </summary>
    public static bool IsSilent(short[] buffer, int count)
    {
        if (count == 0) return true;

        float sum = 0;
        for (int i = 0; i < count; i++)
        {
            float sample = buffer[i] / 32767f;
            sum += sample * sample;
        }
        float rms = MathF.Sqrt(sum / count);

        return rms < VadThreshold;
    }

    /// <summary>
    /// 处理音频帧（单次遍历：RMS + 噪声门 + AGC 合并）
    /// 返回计算得到的 RMS 值，供调用方复用
    /// </summary>
    public float Process(float[] buffer, int count)
    {
        if (count == 0) return 0f;

        lock (_processLock)
        {
            // 单次遍历：计算 RMS + 应用噪声门 + 应用 AGC
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                sum += buffer[i] * buffer[i];
            }
            float rms = MathF.Sqrt(sum / count);

            // 噪声估计 (使用最小值跟踪)
            if (rms < _noiseEstimate || _noiseEstimate == 0)
            {
                _noiseEstimate = rms;
            }
            else
            {
                _noiseEstimate = _noiseAlpha * _noiseEstimate + (1 - _noiseAlpha) * rms;
            }

            // 计算噪声门系数
            float noiseGateCoeff = 1.0f;
            if (NoiseGateEnabled)
            {
                bool isNoise = rms < NoiseGateThreshold || rms < _noiseEstimate * 1.5f;
                if (isNoise)
                {
                    noiseGateCoeff = 0.3f; // 噪声段衰减到30%
                }
            }

            // 计算 AGC 增益
            float targetGain = _targetRms / MathF.Max(rms, 0.001f);
            targetGain = Math.Clamp(targetGain, 0.5f, 4.0f);

            // 平滑增益变化
            if (targetGain < _currentGain)
            {
                _currentGain = _agcAttack * _currentGain + (1 - _agcAttack) * targetGain;
            }
            else
            {
                _currentGain = _agcRelease * _currentGain + (1 - _agcRelease) * targetGain;
            }

            // 单次遍历：应用噪声门 + AGC + 限幅
            float totalGain = _currentGain * noiseGateCoeff;
            for (int i = 0; i < count; i++)
            {
                float sample = buffer[i] * totalGain;
                // 限幅，防止削波
                buffer[i] = sample > 1.0f ? 1.0f : (sample < -1.0f ? -1.0f : sample);
            }

            return rms;
        }
    }

    /// <summary>
    /// 重置状态
    /// </summary>
    public void Reset()
    {
        _currentGain = 1.0f;
        _noiseEstimate = 0.0f;
    }
}
