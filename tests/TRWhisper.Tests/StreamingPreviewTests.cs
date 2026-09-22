using System;
using System.Threading.Tasks;
using NAudio.Wave;
using TRWhisper.Core.Audio;
using TRWhisper.Core.Config;
using TRWhisper.Core.Speech;
using Xunit;

namespace TRWhisper.Tests
{
    public class StreamingPreviewTests
    {
        [Fact]
        public void GeneralConfig_EnableStreamingPreview_DefaultsToTrue()
        {
            var config = new GeneralConfig();
            Assert.True(config.EnableStreamingPreview);
        }

        [Fact]
        public void WasapiRecorder_InitialSnapshot_IsEmpty()
        {
            using var recorder = new WasapiRecorder();
            var snapshot = recorder.GetRecordedSamplesSnapshot();
            Assert.NotNull(snapshot);
            Assert.Empty(snapshot);
        }

        [Fact]
        public void MonoMixSampleProvider_DownmixesStereoToMonoCorrectly()
        {
            // Stereo sample provider with 2 channels: L=0.8, R=0.4
            var sourceWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(16000, 2);
            var testSource = new TestSampleProvider(sourceWaveFormat, new float[] { 0.8f, 0.4f, -0.6f, 0.2f });

            var monoMixer = new MonoMixSampleProvider(testSource);
            Assert.Equal(1, monoMixer.WaveFormat.Channels);
            Assert.Equal(16000, monoMixer.WaveFormat.SampleRate);

            var readBuffer = new float[2];
            int framesRead = monoMixer.Read(readBuffer, 0, 2);

            Assert.Equal(2, framesRead);
            // Frame 0: (0.8 + 0.4) / 2 = 0.6
            Assert.Equal(0.6f, readBuffer[0], precision: 4);
            // Frame 1: (-0.6 + 0.2) / 2 = -0.2
            Assert.Equal(-0.2f, readBuffer[1], precision: 4);
        }

        [Fact]
        public async Task DefaultITranscriptionEngine_TranscribeLivePreviewAsync_ReturnsEmptyString()
        {
            ITranscriptionEngine dummyEngine = new DummyTestEngine();
            var result = await dummyEngine.TranscribeLivePreviewAsync(new float[] { 0.1f, 0.2f });
            Assert.Equal(string.Empty, result);
        }

        private class DummyTestEngine : ITranscriptionEngine
        {
            public Task<string> TranscribeAsync(string wavFilePath, string language = "tr", System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult("transcribed");
            }
        }

        private class TestSampleProvider : ISampleProvider
        {
            private readonly float[] _samples;
            private int _position;

            public TestSampleProvider(WaveFormat waveFormat, float[] samples)
            {
                WaveFormat = waveFormat;
                _samples = samples;
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int available = _samples.Length - _position;
                int toRead = Math.Min(available, count);
                Array.Copy(_samples, _position, buffer, offset, toRead);
                _position += toRead;
                return toRead;
            }
        }
    }
}
