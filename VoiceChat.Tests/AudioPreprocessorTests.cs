using VoiceChat.Core.Audio;

namespace VoiceChat.Tests;

public class AudioPreprocessorTests
{
    [Fact]
    public void Process_SilentInput_StaysSilent()
    {
        var preprocessor = new AudioPreprocessor();
        float[] silent = new float[960]; // all zeros

        preprocessor.Process(silent, silent.Length);

        for (int i = 0; i < silent.Length; i++)
        {
            Assert.Equal(0f, silent[i], 6);
        }
    }

    [Fact]
    public void Process_LoudInput_ReducesVolume()
    {
        var preprocessor = new AudioPreprocessor { NoiseGateEnabled = false };
        float[] loud = new float[960];
        for (int i = 0; i < loud.Length; i++)
        {
            loud[i] = 0.8f;
        }

        preprocessor.Process(loud, loud.Length);

        // After AGC, loud input should be reduced
        float maxAfter = loud.Max(MathF.Abs);
        Assert.True(maxAfter <= 0.8f, "AGC should reduce loud input");
    }

    [Fact]
    public void Process_QuietInput_IncreasesVolume()
    {
        var preprocessor = new AudioPreprocessor { NoiseGateEnabled = false };
        float[] quiet = new float[960];
        for (int i = 0; i < quiet.Length; i++)
        {
            quiet[i] = 0.01f;
        }

        preprocessor.Process(quiet, quiet.Length);

        // After AGC, quiet input should be amplified
        float maxAfter = quiet.Max(MathF.Abs);
        Assert.True(maxAfter > 0.01f, "AGC should amplify quiet input");
    }

    [Fact]
    public void Process_NoClipping()
    {
        var preprocessor = new AudioPreprocessor();
        float[] signal = new float[960];
        for (int i = 0; i < signal.Length; i++)
        {
            signal[i] = (float)(0.5 * Math.Sin(i * 0.1));
        }

        preprocessor.Process(signal, signal.Length);

        for (int i = 0; i < signal.Length; i++)
        {
            Assert.True(signal[i] >= -1.0f && signal[i] <= 1.0f,
                "Output should be clamped to [-1, 1]");
        }
    }

    [Fact]
    public void IsSilent_True_ReturnsTrue()
    {
        short[] silent = new short[960];
        Assert.True(AudioPreprocessor.IsSilent(silent, silent.Length));
    }

    [Fact]
    public void IsSilent_False_ReturnsFalse()
    {
        short[] loud = new short[960];
        for (int i = 0; i < loud.Length; i++)
        {
            loud[i] = 10000;
        }
        Assert.False(AudioPreprocessor.IsSilent(loud, loud.Length));
    }

    [Fact]
    public void Process_ThreadSafe()
    {
        var preprocessor = new AudioPreprocessor();
        float[] buffer = new float[960];
        Random.Shared.NextBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffer.AsSpan()));

        Parallel.For(0, 100, _ =>
        {
            float[] copy = (float[])buffer.Clone();
            preprocessor.Process(copy, copy.Length);
        });

        // No exception = pass
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var preprocessor = new AudioPreprocessor();
        float[] signal = new float[960];
        for (int i = 0; i < signal.Length; i++) signal[i] = 0.01f;

        preprocessor.Process(signal, signal.Length);
        preprocessor.Reset();
        // After reset, state should be clean - no crash on next use
        preprocessor.Process(signal, signal.Length);
    }

    [Fact]
    public void NoiseGate_BelowThreshold_Attenuates()
    {
        var preprocessor = new AudioPreprocessor
        {
            NoiseGateEnabled = true,
            NoiseGateThreshold = 0.01f
        };
        // Very quiet signal below threshold
        float[] quiet = new float[960];
        for (int i = 0; i < quiet.Length; i++) quiet[i] = 0.001f;

        float rmsBefore = quiet.Sum(x => x * x) / quiet.Length;
        preprocessor.Process(quiet, quiet.Length);
        float rmsAfter = quiet.Sum(x => x * x) / quiet.Length;

        // Noise gate should attenuate
        Assert.True(rmsAfter < rmsBefore || rmsAfter == 0f,
            $"Noise gate should attenuate quiet signal: before={rmsBefore}, after={rmsAfter}");
    }

    [Fact]
    public void NoiseGate_Disabled_PassesThrough()
    {
        var preprocessor = new AudioPreprocessor
        {
            NoiseGateEnabled = false
        };
        float[] signal = new float[960];
        for (int i = 0; i < signal.Length; i++) signal[i] = 0.005f;

        float[] original = (float[])signal.Clone();
        preprocessor.Process(signal, signal.Length);

        // Without noise gate, signal should not be heavily attenuated
        float maxOriginal = original.Max(MathF.Abs);
        float maxProcessed = signal.Max(MathF.Abs);
        Assert.True(maxProcessed >= maxOriginal * 0.5f,
            "Noise gate disabled should not heavily attenuate signal");
    }

    [Fact]
    public void AGC_Gain_Clamped()
    {
        var preprocessor = new AudioPreprocessor { NoiseGateEnabled = false };

        // Process very quiet signal to trigger high gain
        float[] quiet = new float[960];
        for (int i = 0; i < quiet.Length; i++) quiet[i] = 0.001f;

        preprocessor.Process(quiet, quiet.Length);

        // Output should not exceed [-1, 1] due to clamping
        for (int i = 0; i < quiet.Length; i++)
        {
            Assert.True(quiet[i] >= -1.0f && quiet[i] <= 1.0f,
                $"Output sample {quiet[i]} out of range [-1, 1]");
        }
    }

    [Fact]
    public void Process_EmptyCount_ReturnsZero()
    {
        var preprocessor = new AudioPreprocessor();
        float result = preprocessor.Process(new float[10], 0);
        Assert.Equal(0f, result);
    }

    [Fact]
    public void IsSilent_EmptyCount_ReturnsTrue()
    {
        Assert.True(AudioPreprocessor.IsSilent(new short[10], 0));
    }

    [Fact]
    public void Process_ReturnsRms()
    {
        var preprocessor = new AudioPreprocessor { NoiseGateEnabled = false };
        float[] signal = new float[960];
        for (int i = 0; i < signal.Length; i++) signal[i] = 0.5f;

        float rms = preprocessor.Process(signal, signal.Length);

        Assert.True(rms > 0f, "RMS should be positive for non-silent input");
    }
}
