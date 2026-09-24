using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Audio
{
    public interface ISoundFeedbackService
    {
        void PlayStartSound();
        void PlaySuccessSound();
        void PlayCancelSound();

        /// <summary>Ayarlar penceresi için: ayar kapalı olsa da verilen düzeyde başarı sesini çalar.</summary>
        void PlayTestSound(int volume);
    }

    /// <summary>
    /// Dikte durumları için kısa, yumuşak sesli geri bildirim (earcon). Tonlar dosyadan değil,
    /// kodda sinüs + attack/decay zarfıyla üretilir. Çalma arka planda yapılır ve hatalar
    /// yutulur: ses sorunu dikte akışını asla durdurmaz.
    /// </summary>
    public sealed class SoundFeedbackService : ISoundFeedbackService, IDisposable
    {
        /// <summary>Ses kartlarının yerel hızı; 44.1 kHz'lik tampon sistem karıştırıcısında yeniden örneklenir.</summary>
        public const int SampleRate = 48000;

        /// <summary>%100 ses düzeyindeki tepe genliği; bilerek düşük tutulur (zarif, ürkütmeyen).</summary>
        private const float PeakAmplitude = 0.3f;

        /// <summary>
        /// Baştaki sessizlik: boşta uyuyan ses cihazı açılırken ilk onlarca ms'yi yutar.
        /// Bu pay olmadan kısa bir tınının başı kesilir ("kesik" duyulur).
        /// </summary>
        public const int LeadSilenceMs = 50;

        // Nota frekansları (eşit tampere): D5, A5, A4, E4.
        private const double D5 = 587.33, A5 = 880.00, A4 = 440.00, E4 = 329.63;

        // Tınılar bir kez, tam genlikte üretilir; çalarken yalnızca ses düzeyiyle ölçeklenir.
        // Başlama: tek yumuşak "tık"; başarı: yükselen beşli aralık; iptal: alçalan dörtlü aralık.
        private static readonly float[] StartTone = Render(0.85f, (0, D5, 55));
        private static readonly float[] SuccessTone = Render(1.0f, (0, D5, 60), (75, A5, 70));
        private static readonly float[] CancelTone = Render(0.8f, (0, A4, 50), (80, E4, 60));

        private readonly ConfigManager _configManager;
        private readonly Action<float[]> _player;
        private volatile bool _disposed;

        /// <param name="player">Testler için çalıcı; verilmezse NAudio ile arka planda çalınır.</param>
        public SoundFeedbackService(ConfigManager configManager, Action<float[]>? player = null)
        {
            _configManager = configManager;
            _player = player ?? (samples => Task.Run(() => PlayWithNAudio(samples)));
        }

        public void PlayStartSound() => PlayIfEnabled(StartTone);
        public void PlaySuccessSound() => PlayIfEnabled(SuccessTone);
        public void PlayCancelSound() => PlayIfEnabled(CancelTone);
        public void PlayTestSound(int volume) => Play(SuccessTone, volume);

        /// <summary>Ayar değerini (0..100) genlik çarpanına çevirir; aralık dışı değerler kırpılır.</summary>
        public static float VolumeToGain(int volume) => Math.Clamp(volume, 0, 100) / 100f;

        private void PlayIfEnabled(float[] tone)
        {
            var audio = _configManager.Current.Audio;
            if (!audio.EnableSoundFeedback) return;
            Play(tone, audio.SoundFeedbackVolume);
        }

        private void Play(float[] tone, int volume)
        {
            if (_disposed) return;

            var gain = VolumeToGain(volume);
            if (gain <= 0f) return;

            try
            {
                var samples = new float[tone.Length];
                for (int i = 0; i < tone.Length; i++) samples[i] = tone[i] * gain;
                _player(samples);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[SoundFeedbackService] Ses çalınamadı: {ex.Message}");
            }
        }

        /// <summary>
        /// Notaları "pluck" (marimba/cam benzeri) tınıyla çizer, hafif bir oda yankısı ekler ve
        /// tepeyi <see cref="PeakAmplitude"/> × <paramref name="level"/> düzeyine getirir.
        /// Not: (başlangıç ms, frekans, sönüm sabiti ms).
        /// </summary>
        private static float[] Render(float level, params (int StartMs, double Hz, double DecayMs)[] notes)
        {
            int lead = Samples(LeadSilenceMs);
            int end = notes.Max(n => lead + Samples(n.StartMs) + Samples(n.DecayMs * 7)); // e^-7 ≈ 0.001
            var dry = new double[end];

            foreach (var (startMs, hz, decayMs) in notes)
                AddPluck(dry, lead + Samples(startMs), hz, decayMs);

            // Oda yankısı: gecikmeli, alçak geçiren filtreli birkaç kopya. Kuru sinüsü "bip"ten
            // ayıran en büyük fark budur; tını boşlukta sönüyormuş gibi duyulur.
            var taps = new (int Delay, double Gain)[] { (Samples(29), 0.22), (Samples(61), 0.12), (Samples(97), 0.06) };
            int length = end + taps[^1].Delay;
            var wet = new double[length];
            double lowpass = 0;
            for (int i = 0; i < length; i++)
            {
                double echo = 0;
                foreach (var (delay, gain) in taps)
                    if (i - delay >= 0 && i - delay < end) echo += dry[i - delay] * gain;
                lowpass += 0.35 * (echo - lowpass);
                wet[i] = (i < end ? dry[i] : 0) + lowpass;
            }

            double peak = wet.Max(Math.Abs);
            double scale = peak > 0 ? PeakAmplitude * level / peak : 0;
            int fade = Samples(10);
            var samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                double tail = Math.Min(1.0, (length - 1 - i) / (double)fade); // son 10 ms sıfıra iner
                samples[i] = (float)(wet[i] * scale * tail);
            }

            return samples;
        }

        /// <summary>
        /// Temel frekans + çabuk sönen üst kısmi tonlar: vuruş anında parlak, sonra yumuşak.
        /// 3 ms'lik yükseltilmiş kosinüs girişi tık sesini önler.
        /// </summary>
        private static void AddPluck(double[] buffer, int offset, double hz, double decayMs)
        {
            double tau = decayMs / 1000.0;
            int attack = Samples(3);
            for (int i = 0; offset + i < buffer.Length; i++)
            {
                double t = (double)i / SampleRate;
                double env = Math.Exp(-t / tau) * (i < attack ? 0.5 - 0.5 * Math.Cos(Math.PI * i / attack) : 1.0);
                double w = 2 * Math.PI * hz * t;
                buffer[offset + i] += env * (
                    Math.Sin(w) +
                    0.28 * Math.Sin(2 * w) * Math.Exp(-t / (tau * 0.5)) +
                    0.08 * Math.Sin(3 * w) * Math.Exp(-t / (tau * 0.35)) +
                    0.05 * Math.Sin(4.2 * w) * Math.Exp(-t / (tau * 0.2)));
            }
        }

        private static int Samples(double ms) => (int)(SampleRate * ms / 1000);

        private void PlayWithNAudio(float[] samples)
        {
            if (_disposed) return;

            // 16-bit PCM: her ses sürücüsünün desteklediği biçim (IEEE float bazı cihazlarda açılmaz).
            var pcm = new short[samples.Length];
            for (int i = 0; i < samples.Length; i++) pcm[i] = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            var bytes = new byte[pcm.Length * sizeof(short)];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            var stream = new RawSourceWaveStream(new MemoryStream(bytes), new WaveFormat(SampleRate, 16, 1));

            // Varsayılan 300 ms gecikme bir "tık" geri bildirimi için fazla uzun.
            // Çok kısa tamponlar (2 x 30 ms) iş parçacığı gecikmesinde boşalıp kesintiye yol açabilir.
            var output = new WaveOutEvent { DesiredLatency = 90, NumberOfBuffers = 3 };
            try
            {
                output.PlaybackStopped += (_, _) =>
                {
                    output.Dispose();
                    stream.Dispose();
                };
                output.Init(stream);
                output.Play();
            }
            catch (Exception ex)
            {
                // Ses cihazı yok/meşgul: sessizce vazgeç, kaynakları bırak.
                output.Dispose();
                stream.Dispose();
                FileLog.Write($"[SoundFeedbackService] Ses çalınamadı: {ex.Message}");
            }
        }

        public void Dispose() => _disposed = true;
    }
}
