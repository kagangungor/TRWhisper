using System;
using NAudio.Wave;

namespace TRWhisper.Core.Audio
{
    /// <summary>
    /// Çok kanallı sesi kanal ortalamasıyla tek kanala (mono) indirir.
    /// </summary>
    public sealed class MonoMixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _buffer = Array.Empty<float>();

        public MonoMixSampleProvider(ISampleProvider source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var needed = count * _channels;
            if (_buffer.Length < needed) _buffer = new float[needed];

            var read = _source.Read(_buffer, 0, needed);
            var frames = read / _channels;

            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                for (var ch = 0; ch < _channels; ch++)
                    sum += _buffer[frame * _channels + ch];
                buffer[offset + frame] = sum / _channels;
            }

            return frames;
        }
    }
}
