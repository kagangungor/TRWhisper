using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TRWhisper.Core.Diagnostics;

namespace TRWhisper.Core.Update
{
    public record UpdateCheckResult(bool IsUpdateAvailable, string CurrentVersion, string LatestVersion, string ReleaseUrl, string ReleaseNotes, string? DownloadUrl)
    {
        /// <summary>Denetim yapılamadı (ağ yok, API hatası, anlaşılamayan yanıt); LatestVersion boş kalır.</summary>
        public bool CheckFailed => string.IsNullOrEmpty(LatestVersion);
    }

    /// <summary>
    /// GitHub Releases'taki son sürümü uygulamanın sürümüyle karşılaştırır. Hiçbir durumda
    /// istisna fırlatmaz: hata günlüğe yazılır ve <see cref="UpdateCheckResult.CheckFailed"/> döner.
    /// </summary>
    public class UpdateCheckerService
    {
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/kagangungor/TRWhisper/releases/latest";
        public const string ReleasesPageUrl = "https://github.com/kagangungor/TRWhisper/releases";

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
        private static readonly HttpClient SharedClient = new();

        private readonly HttpClient _http;
        private readonly Version _currentVersion;

        public UpdateCheckerService(HttpClient? httpClient = null, Version? currentVersion = null)
        {
            _http = httpClient ?? SharedClient;
            _currentVersion = Normalize(currentVersion ?? AssemblyVersion);
        }

        private static Version AssemblyVersion =>
            typeof(UpdateCheckerService).Assembly.GetName().Version ?? new Version(2, 1, 0);

        /// <summary>Uygulamanın sürümü, "2.1.0" biçiminde.</summary>
        public static string CurrentVersionText => Format(Normalize(AssemblyVersion));

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(RequestTimeout);

                using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
                // GitHub API User-Agent'sız isteği 403 ile reddeder.
                request.Headers.UserAgent.ParseAdd("TRWhisper-App");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");

                using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    FileLog.Write($"[UpdateChecker] GitHub yanıtı başarısız: {(int)response.StatusCode}");
                    return Failed();
                }

                var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                return Evaluate(json);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[UpdateChecker] Güncelleme denetlenemedi: {ex.Message}");
                return Failed();
            }
        }

        private UpdateCheckResult Evaluate(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var latest = ParseVersion(GetString(root, "tag_name"));
            if (latest == null)
            {
                FileLog.Write("[UpdateChecker] Yanıtta geçerli bir tag_name yok.");
                return Failed();
            }

            // Kabuk üzerinden açılacağı için yalnızca kendi depomuzun GitHub adresleri kabul edilir.
            var releaseUrl = GetString(root, "html_url");
            if (!IsGitHubUrl(releaseUrl)) releaseUrl = ReleasesPageUrl;

            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var url = GetString(asset, "browser_download_url");
                    if (!IsGitHubUrl(url)) continue;
                    downloadUrl ??= url;
                    if (url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { downloadUrl = url; break; }
                }
            }

            return new UpdateCheckResult(latest > _currentVersion, Format(_currentVersion), Format(latest),
                releaseUrl, GetString(root, "body").Trim(), downloadUrl);
        }

        /// <summary>"v2.2.0", "2.2.0", "v2.2.0-beta" → 2.2.0; anlaşılamazsa null.</summary>
        public static Version? ParseVersion(string? tag)
        {
            var text = (tag ?? "").Trim().TrimStart('v', 'V');
            var suffix = text.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0) text = text[..suffix];
            return Version.TryParse(text, out var version) ? Normalize(version) : null;
        }

        // "2.1" ile "2.1.0" ve "2.1.0.0" eşit sayılsın diye eksik bileşenler 0'a çekilir.
        private static Version Normalize(Version v) =>
            new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

        private static string Format(Version v) => v.Revision > 0 ? v.ToString(4) : v.ToString(3);

        private UpdateCheckResult Failed() =>
            new(false, Format(_currentVersion), "", ReleasesPageUrl, "", null);

        private static string GetString(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";

        private const string RepoPathPrefix = "/kagangungor/TRWhisper/";

        /// <summary>
        /// Yalnızca uygulamanın kendi deposundaki https://github.com adresleri: yanıt başka bir
        /// depoya (ör. benzer adlı sahte bir kuruluma) yönlendirse bile o adres açılmaz.
        /// </summary>
        private static bool IsGitHubUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.IsDefaultPort &&
            uri.AbsolutePath.StartsWith(RepoPathPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
