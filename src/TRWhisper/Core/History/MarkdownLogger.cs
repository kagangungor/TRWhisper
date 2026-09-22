using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TRWhisper.Core.Config;

namespace TRWhisper.Core.History
{
    public class MarkdownLogger
    {
        private readonly ConfigManager _configManager;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public MarkdownLogger(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        public async Task LogTranscriptAsync(string transcript, bool cleanedWithLlm, string? rawTranscript = null)
        {
            if (string.IsNullOrWhiteSpace(transcript)) return;
            if (!_configManager.Current.General.EnableHistoryLogging) return;

            await _semaphore.WaitAsync();
            try
            {
                var dir = _configManager.Current.General.ResolvedLogDirectory;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var now = DateTime.Now;
                var fileName = $"{now:yyyy-MM}.md";
                var filePath = Path.Combine(dir, fileName);

                var sb = new StringBuilder();

                // Dosya ilk defa oluşturuluyorsa başlık ekle
                if (!File.Exists(filePath))
                {
                    sb.AppendLine($"# TRWhisper Dikte Günlüğü - {now:MMMM yyyy}");
                    sb.AppendLine();
                    sb.AppendLine("> Bu dosya TRWhisper tarafından yerel olarak tutulmaktadır. Herhangi bir harici sunucuya iletilmez.");
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }

                var modeLabel = cleanedWithLlm ? " [LLM Temizlendi]" : "";
                sb.AppendLine($"### 🕒 {now:yyyy-MM-dd HH:mm:ss}{modeLabel}");
                sb.AppendLine();
                sb.AppendLine(transcript.Trim());
                sb.AppendLine();

                if (cleanedWithLlm && !string.IsNullOrWhiteSpace(rawTranscript) && rawTranscript != transcript)
                {
                    sb.AppendLine("<details>");
                    sb.AppendLine("<summary>Ham Transkript</summary>");
                    sb.AppendLine();
                    sb.AppendLine($"> {rawTranscript.Trim()}");
                    sb.AppendLine("</details>");
                    sb.AppendLine();
                }

                sb.AppendLine("---");
                sb.AppendLine();

                await File.AppendAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MarkdownLogger] Günlük yazma hatası: {ex.Message}");
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
