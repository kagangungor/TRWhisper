using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TRWhisper.Core.Audio;
using TRWhisper.Core.Config;
using Xunit;

namespace TRWhisper.Tests
{
    public class SoundFeedbackServiceTests : IDisposable
    {
        private readonly string _tempConfigFile;
        private readonly ConfigManager _configManager;
        private readonly List<float[]> _played = new();
        private readonly SoundFeedbackService _service;

        public SoundFeedbackServiceTests()
        {
            _tempConfigFile = Path.Combine(Path.GetTempPath(), $"trwhisper_test_cfg_{Guid.NewGuid():N}.json");
            _configManager = new ConfigManager(_tempConfigFile);
            _service = new SoundFeedbackService(_configManager, samples => _played.Add(samples));
        }

        public void Dispose()
        {
            _service.Dispose();
            try { File.Delete(_tempConfigFile); } catch { }
        }

        private void Enable(int volume = 50)
        {
            _configManager.Current.Audio.EnableSoundFeedback = true;
            _configManager.Current.Audio.SoundFeedbackVolume = volume;
        }

        private void PlayAll()
        {
            _service.PlayStartSound();
            _service.PlaySuccessSound();
            _service.PlayCancelSound();
        }

        [Fact]
        public void Defaults_AreDisabledAtHalfVolume()
        {
            var audio = new AudioConfig();

            Assert.False(audio.EnableSoundFeedback);
            Assert.Equal(50, audio.SoundFeedbackVolume);
        }

        [Fact]
        public void Disabled_PlaysNothing()
        {
            PlayAll();

            Assert.Empty(_played);
        }

        [Fact]
        public void Enabled_PlaysThreeDistinctShortCues()
        {
            Enable();

            PlayAll();

            Assert.Equal(3, _played.Count);
            Assert.Equal(3, _played.Select(p => p.Length).Distinct().Count());
            foreach (var cue in _played)
                Assert.InRange(cue.Length * 1000 / SoundFeedbackService.SampleRate, 150, 800);
        }

        [Fact]
        public void Cues_StartWithSilencePadding_SoDeviceWakeUpDoesNotClipThem()
        {
            Enable(volume: 100);

            PlayAll();

            int lead = SoundFeedbackService.SampleRate * SoundFeedbackService.LeadSilenceMs / 1000;
            foreach (var cue in _played)
                Assert.All(cue.Take(lead), s => Assert.Equal(0f, s));
        }

        [Fact]
        public void Enabled_ZeroVolume_PlaysNothing()
        {
            Enable(volume: 0);

            PlayAll();

            Assert.Empty(_played);
        }

        [Theory]
        [InlineData(-20, 0f)]
        [InlineData(0, 0f)]
        [InlineData(50, 0.5f)]
        [InlineData(100, 1f)]
        [InlineData(250, 1f)]
        public void VolumeToGain_ClampsToZeroOneRange(int volume, float expected)
            => Assert.Equal(expected, SoundFeedbackService.VolumeToGain(volume));

        [Fact]
        public void Volume_ScalesAmplitudeAndStaysSubtle()
        {
            Enable(volume: 100);
            _service.PlaySuccessSound();
            _configManager.Current.Audio.SoundFeedbackVolume = 50;
            _service.PlaySuccessSound();

            var fullPeak = _played[0].Max(Math.Abs);
            var halfPeak = _played[1].Max(Math.Abs);

            Assert.InRange(fullPeak, 0.1f, 0.35f);
            Assert.Equal(fullPeak / 2, halfPeak, 3);
        }

        [Fact]
        public void Cues_StartAndEndNearSilence_NoClicks()
        {
            Enable(volume: 100);
            PlayAll();

            foreach (var samples in _played)
            {
                Assert.True(Math.Abs(samples[0]) < 0.01f);
                Assert.True(Math.Abs(samples[^1]) < 0.01f);
            }
        }

        [Fact]
        public void PlayerFailure_IsSwallowed()
        {
            Enable();
            using var service = new SoundFeedbackService(_configManager, _ => throw new InvalidOperationException("ses cihazı yok"));

            var ex = Record.Exception(() =>
            {
                service.PlayStartSound();
                service.PlaySuccessSound();
                service.PlayCancelSound();
                service.PlayTestSound(80);
            });

            Assert.Null(ex);
        }

        [Fact]
        public void TestSound_PlaysEvenWhenDisabled()
        {
            _service.PlayTestSound(70);

            Assert.Single(_played);
        }

        [Fact]
        public void AfterDispose_PlaysNothing()
        {
            Enable();
            _service.Dispose();

            PlayAll();

            Assert.Empty(_played);
        }
    }
}
