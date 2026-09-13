using System;
using System.Collections.Generic;
using System.Linq;

namespace TRWhisper.Core.History
{
    public record TranscriptEntry(
        DateTime Timestamp,
        string Text,
        bool CleanedWithLlm,
        string? RawText = null
    )
    {
        public string ShortPreview => Text.Length > 45 ? Text.Substring(0, 42) + "..." : Text;
    }

    public class TranscriptHistory
    {
        private const int MaxCapacity = 10;
        private readonly List<TranscriptEntry> _entries = new();
        private readonly object _lock = new();

        public event Action? HistoryChanged;

        public void Add(string text, bool cleanedWithLlm, string? rawText = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            lock (_lock)
            {
                var entry = new TranscriptEntry(DateTime.Now, text.Trim(), cleanedWithLlm, rawText?.Trim());
                _entries.Insert(0, entry); // Yeni kayıtlar en başta

                if (_entries.Count > MaxCapacity)
                {
                    _entries.RemoveRange(MaxCapacity, _entries.Count - MaxCapacity);
                }
            }

            HistoryChanged?.Invoke();
        }

        public IReadOnlyList<TranscriptEntry> GetRecentEntries()
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }

            HistoryChanged?.Invoke();
        }
    }
}
