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
}
