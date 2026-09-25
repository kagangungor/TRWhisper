using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TRWhisper.Core.Config;
using TRWhisper.Core.Diagnostics;
using TRWhisper.Core.Speech;

namespace Benchmark
{
    public class BenchmarkResult
    {
        public string Engine { get; set; } = string.Empty;
        public string Hardware { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string AudioType { get; set; } = string.Empty;
        public double AudioDurationSec { get; set; }
        public double ModelLoadMs { get; set; }
        public double WarmInferenceMs { get; set; }
        public double TotalColdMs { get; set; }
        public long MemoryBytes { get; set; }
        public string Transcript { get; set; } = string.Empty;
    }

    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            // Worker sub-process mode: runs a single test in isolation
            if (args.Length >= 5 && args[0] == "--worker")
            {
                var engineType = args[1];     // "whispernet" or "cli"
                var hw = args[2];             // "cuda" or "cpu"
                var modelKey = args[3];       // "small" or "turbo"
                var audioKey = args[4];       // "short", "long", "silence"

                return await RunWorkerAsync(engineType, hw, modelKey, audioKey);
            }

            // Orchestrator mode: run all benchmark tests and present table
            Console.WriteLine("==================================================================");
            Console.WriteLine("  TRWhisper Performance Benchmark Suite");
            Console.WriteLine("  Hardware: Intel Core i5-12450H + NVIDIA RTX 3050 Laptop GPU (4 GB)");
            Console.WriteLine("==================================================================\n");

            var projectRoot = FindProjectRoot();
            var audioDir = Path.Combine(projectRoot, "tools", "benchmark", "audio");

            if (!File.Exists(Path.Combine(audioDir, "speech_3s.wav")))
            {
                Console.WriteLine("Ses dosyaları bulunamadı! 'python tools/benchmark/prepare_samples.py' çalıştırılıyor...");
                var p = Process.Start("python", "tools/benchmark/prepare_samples.py");
                p.WaitForExit();
            }

            var results = new List<BenchmarkResult>();

            var matrix = new (string engine, string hw, string model)[]
            {
                ("whispernet", "cuda", "small"),
                ("whispernet", "cuda", "turbo"),
                ("whispernet", "cpu", "small"),
                ("whispernet", "cpu", "turbo"),
                ("cli", "cuda", "small"),
                ("cli", "cuda", "turbo"),
                ("cli", "cpu", "small"),
                ("cli", "cpu", "turbo")
            };

            var audios = new[] { "short", "long", "silence" };

            var exePath = Environment.ProcessPath ?? typeof(Program).Assembly.Location;

            foreach (var (engine, hw, model) in matrix)
            {
                Console.WriteLine($"\n>>> [{engine.ToUpperInvariant()} | {hw.ToUpperInvariant()} | {model.ToUpperInvariant()}]");
                foreach (var audio in audios)
                {
                    Console.Write($"    Test ediliyor: {audio.PadRight(8)} ... ");
                    var result = await SpawnWorkerAsync(exePath, engine, hw, model, audio);
                    if (result != null)
                    {
                        results.Add(result);
                        Console.WriteLine($"Tamamlandı! Sıcak={result.WarmInferenceMs:0} ms | Soğuk={result.TotalColdMs:0} ms | Yükleme={result.ModelLoadMs:0} ms | Metin: \"{Truncate(result.Transcript, 35)}\"");
                    }
                    else
                    {
                        Console.WriteLine("HATA!");
                    }
                }
            }

            // Print formatted markdown tables
            PrintMarkdownReport(results);

            return 0;
        }

        private static string Truncate(string str, int max)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Length <= max ? str : str.Substring(0, max - 3) + "...";
        }

        private static async Task<BenchmarkResult?> SpawnWorkerAsync(string exePath, string engine, string hw, string model, string audio)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--worker {engine} {hw} {model} {audio}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (hw == "cpu")
            {
                psi.EnvironmentVariables["TRWHISPER_FORCE_CPU"] = "1";
            }
            else
            {
                psi.EnvironmentVariables.Remove("TRWHISPER_FORCE_CPU");
            }

            using var p = Process.Start(psi);
            if (p == null) return null;

            var stdout = await p.StandardOutput.ReadToEndAsync();
            var stderr = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();

            if (p.ExitCode != 0)
            {
                Console.WriteLine($"\n[Worker Hata] {stderr}");
                return null;
            }

            var line = stdout.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("{") && l.EndsWith("}"));
            if (string.IsNullOrEmpty(line))
            {
                Console.WriteLine($"\n[JSON Yok] stdout: {stdout}");
                return null;
            }

            return JsonSerializer.Deserialize<BenchmarkResult>(line);
        }

        private static async Task<int> RunWorkerAsync(string engineType, string hw, string modelKey, string audioKey)
        {
            var projectRoot = FindProjectRoot();
            var toolsWhisper = Path.Combine(projectRoot, "tools", "whisper");
            var audioDir = Path.Combine(projectRoot, "tools", "benchmark", "audio");

            var modelFile = modelKey switch
            {
                "small" => "ggml-small.bin",
                "turbo" => "ggml-large-v3-turbo-q5_0.bin",
                _ => throw new ArgumentException("Unknown model: " + modelKey)
            };
            var modelPath = Path.Combine(toolsWhisper, modelFile);
            var vadPath = Path.Combine(toolsWhisper, "ggml-silero-v6.2.0.bin");

            var (audioFile, expectedDur) = audioKey switch
            {
                "short" => ("speech_3s.wav", 3.2),
                "long" => ("speech_10s.wav", 10.6),
                "silence" => ("silence_2s.wav", 2.0),
                _ => throw new ArgumentException("Unknown audio: " + audioKey)
            };
            var audioPath = Path.Combine(audioDir, audioFile);

            var tmpDir = Path.Combine(Path.GetTempPath(), $"trw_bench_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpDir);

            try
            {
                var cfg = new ConfigManager(Path.Combine(tmpDir, "config.json"));
                cfg.Current.Whisper.ModelPath = modelPath;
                cfg.Current.Whisper.VadModelPath = vadPath;
                cfg.Current.Whisper.CliPath = Path.Combine(toolsWhisper, "whisper-cli.exe");
                cfg.Current.Whisper.Threads = 8;
                cfg.Current.General.Language = "tr";

                var res = new BenchmarkResult
                {
                    Engine = engineType == "whispernet" ? "Whisper.net (In-Process)" : "Whisper CLI",
                    Hardware = hw == "cuda" ? "CUDA 13 (RTX 3050)" : "CPU (i5-12450H)",
                    Model = modelKey == "small" ? "Small" : "Large-v3 Turbo",
                    AudioType = audioKey,
                    AudioDurationSec = expectedDur
                };

                if (engineType == "whispernet")
                {
                    using var engine = new WhisperNetEngine(cfg);

                    // 1. Cold load: First transcription includes model load
                    var swCold = Stopwatch.StartNew();
                    var text1 = await engine.TranscribeAsync(audioPath, "tr");
                    swCold.Stop();

                    // Model is now loaded in memory!
                    // 2. Warm inference: Run second transcription (warm)
                    var swWarm = Stopwatch.StartNew();
                    var textWarm = await engine.TranscribeAsync(audioPath, "tr");
                    swWarm.Stop();

                    res.WarmInferenceMs = swWarm.ElapsedMilliseconds;
                    res.TotalColdMs = swCold.ElapsedMilliseconds;
                    res.ModelLoadMs = Math.Max(0, res.TotalColdMs - res.WarmInferenceMs);
                    res.Transcript = textWarm;
                    res.MemoryBytes = Process.GetCurrentProcess().WorkingSet64;
                }
                else
                {
                    // CLI mode: runs whisper-cli.exe directly with -otxt (no pipe redirection to prevent deadlocks)
                    var cliPath = Path.Combine(toolsWhisper, "whisper-cli.exe");
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = cliPath,
                        WorkingDirectory = toolsWhisper,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    startInfo.ArgumentList.Add("-m");
                    startInfo.ArgumentList.Add(modelPath);
                    startInfo.ArgumentList.Add("-f");
                    startInfo.ArgumentList.Add(audioPath);
                    startInfo.ArgumentList.Add("-l");
                    startInfo.ArgumentList.Add("tr");
                    startInfo.ArgumentList.Add("-t");
                    startInfo.ArgumentList.Add("8");
                    startInfo.ArgumentList.Add("-nt");
                    startInfo.ArgumentList.Add("-otxt");
                    if (hw == "cpu")
                    {
                        startInfo.ArgumentList.Add("-ng");
                    }
                    if (File.Exists(vadPath))
                    {
                        startInfo.ArgumentList.Add("--vad");
                        startInfo.ArgumentList.Add("-vm");
                        startInfo.ArgumentList.Add(vadPath);
                    }

                    var sidecarTxt = audioPath + ".txt";
                    try { if (File.Exists(sidecarTxt)) File.Delete(sidecarTxt); } catch { }

                    var sw = Stopwatch.StartNew();
                    using var p = Process.Start(startInfo)!;
                    p.WaitForExit(60000);
                    sw.Stop();

                    var text = "";
                    if (File.Exists(sidecarTxt))
                    {
                        try { text = File.ReadAllText(sidecarTxt).Trim(); } catch { }
                    }

                    res.WarmInferenceMs = sw.ElapsedMilliseconds; // CLI has no warm state, every run reloads
                    res.TotalColdMs = sw.ElapsedMilliseconds;
                    res.ModelLoadMs = 0; // included in every run
                    res.Transcript = text;
                    res.MemoryBytes = 0;
                }

                Console.WriteLine(JsonSerializer.Serialize(res));
                return 0;
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); } catch { }
            }
        }

        private static void PrintMarkdownReport(List<BenchmarkResult> results)
        {
            Console.WriteLine("\n\n==================================================================");
            Console.WriteLine("                   SONUÇ RAPORU (MARKDOWN)");
            Console.WriteLine("==================================================================\n");

            Console.WriteLine("### ⚡ TRWhisper v2.2 Güncel Performans Tablosu");
            Console.WriteLine("Ölçüm Donanımı: Intel Core i5-12450H (12 iş parçacığı) + NVIDIA GeForce RTX 3050 Laptop GPU (4 GB, CUDA 13.4, Flash Attention).\n");

            Console.WriteLine("#### 1. Süreç İçi Whisper.net Motoru (v2.2 Varsayılan — Model Bellekte / Sıcak Dikte)\n");
            Console.WriteLine("| Motor / Donanım | Model | 3,2 sn Konuşma (Sıcak) | 10,6 sn Konuşma (Sıcak) | Boş / Sessizlik (VAD) | İlk Yükleme (Soğuk) |");
            Console.WriteLine("|---|---|---|---|---|---|");

            var netResults = results.Where(r => r.Engine.Contains("Whisper.net")).ToList();

            var groups = netResults.GroupBy(r => new { r.Hardware, r.Model });
            foreach (var g in groups)
            {
                var shortRes = g.FirstOrDefault(r => r.AudioType == "short");
                var longRes = g.FirstOrDefault(r => r.AudioType == "long");
                var silRes = g.FirstOrDefault(r => r.AudioType == "silence");

                var shortWarm = shortRes != null ? FormatSec(shortRes.WarmInferenceMs, 2) : "-";
                var longWarm = longRes != null ? FormatSec(longRes.WarmInferenceMs, 2) : "-";
                var silWarm = silRes != null ? FormatSec(silRes.WarmInferenceMs, 2) : "-";
                var loadTime = shortRes != null ? FormatSec(shortRes.ModelLoadMs, 1) : "-";

                Console.WriteLine($"| {g.Key.Hardware} | {g.Key.Model} | **{shortWarm}** | **{longWarm}** | {silWarm} | {loadTime} |");
            }

            Console.WriteLine("\n#### 2. Model Yükleme Dahil Toplam Süre (Soğuk Başlangıç Karşılaştırması)\n");
            Console.WriteLine("| Motor / Donanım | Model | 3,2 sn Konuşma | 10,6 sn Konuşma |");
            Console.WriteLine("|---|---|---|---|");

            foreach (var g in groups)
            {
                var shortRes = g.FirstOrDefault(r => r.AudioType == "short");
                var longRes = g.FirstOrDefault(r => r.AudioType == "long");

                var shortCold = shortRes != null ? FormatSec(shortRes.TotalColdMs, 1) : "-";
                var longCold = longRes != null ? FormatSec(longRes.TotalColdMs, 1) : "-";

                Console.WriteLine($"| {g.Key.Hardware} | {g.Key.Model} | {shortCold} | {longCold} |");
            }

            Console.WriteLine("\n#### 3. Harici CLI Motoru (whisper-cli.exe — v1.x Eski Yöntem)\n");
            Console.WriteLine("| Motor / Donanım | Model | 3,2 sn Konuşma | 10,6 sn Konuşma | Boş / Sessizlik (VAD) |");
            Console.WriteLine("|---|---|---|---|---|");

            var cliResults = results.Where(r => r.Engine.Contains("CLI")).ToList();
            var cliGroups = cliResults.GroupBy(r => new { r.Hardware, r.Model });
            foreach (var g in cliGroups)
            {
                var shortRes = g.FirstOrDefault(r => r.AudioType == "short");
                var longRes = g.FirstOrDefault(r => r.AudioType == "long");
                var silRes = g.FirstOrDefault(r => r.AudioType == "silence");

                var shortTime = shortRes != null ? FormatSec(shortRes.WarmInferenceMs, 1) : "-";
                var longTime = longRes != null ? FormatSec(longRes.WarmInferenceMs, 1) : "-";
                var silTime = silRes != null ? FormatSec(silRes.WarmInferenceMs, 1) : "-";

                Console.WriteLine($"| {g.Key.Hardware} | {g.Key.Model} | {shortTime} | {longTime} | {silTime} |");
            }

            Console.WriteLine("\n==================================================================");
        }

        private static string FormatSec(double ms, int decimals)
        {
            var sec = ms / 1000.0;
            return sec.ToString(decimals == 1 ? "0.0" : "0.00", System.Globalization.CultureInfo.InvariantCulture) + " s";
        }

        private static string FindProjectRoot()
        {
            var cur = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(cur))
            {
                if (File.Exists(Path.Combine(cur, "README.md")) && Directory.Exists(Path.Combine(cur, "src")))
                {
                    return cur;
                }
                cur = Path.GetDirectoryName(cur);
            }
            return Environment.CurrentDirectory;
        }
    }
}
