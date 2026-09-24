using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TRWhisper.Core.Update;
using Xunit;

namespace TRWhisper.Tests
{
    public class UpdateCheckerTests
    {
        /// <summary>Ağa çıkmadan sabit yanıt döndüren işleyici; gönderilen isteği de saklar.</summary>
        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
            public HttpRequestMessage? LastRequest { get; private set; }

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                LastRequest = request;
                return Task.FromResult(_respond(request));
            }
        }

        private static (UpdateCheckerService Service, FakeHandler Handler) Create(string json, string current = "2.1.0",
            HttpStatusCode status = HttpStatusCode.OK)
        {
            var handler = new FakeHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(json) });
            return (new UpdateCheckerService(new HttpClient(handler), Version.Parse(current)), handler);
        }

        private const string ReleaseJson = """
            {
              "tag_name": "v2.2.0",
              "html_url": "https://github.com/kagangungor/TRWhisper/releases/tag/v2.2.0",
              "body": "  Yeni özellikler  ",
              "assets": [
                { "browser_download_url": "https://github.com/kagangungor/TRWhisper/releases/download/v2.2.0/notes.zip" },
                { "browser_download_url": "https://github.com/kagangungor/TRWhisper/releases/download/v2.2.0/TRWhisper-Setup.exe" }
              ]
            }
            """;

        [Fact]
        public async Task NewerRelease_IsUpdateAvailable()
        {
            var (service, handler) = Create(ReleaseJson);
            var result = await service.CheckForUpdatesAsync();

            Assert.True(result.IsUpdateAvailable);
            Assert.False(result.CheckFailed);
            Assert.Equal("2.1.0", result.CurrentVersion);
            Assert.Equal("2.2.0", result.LatestVersion);
            Assert.Equal("https://github.com/kagangungor/TRWhisper/releases/tag/v2.2.0", result.ReleaseUrl);
            Assert.Equal("Yeni özellikler", result.ReleaseNotes);
            Assert.EndsWith("TRWhisper-Setup.exe", result.DownloadUrl);

            Assert.Equal(UpdateCheckerService.LatestReleaseApiUrl, handler.LastRequest!.RequestUri!.ToString());
            Assert.Contains("TRWhisper-App", handler.LastRequest.Headers.UserAgent.ToString());
        }

        [Theory]
        [InlineData("2.1.0", "2.1.0")]
        [InlineData("v2.1.0", "2.1.0.0")]   // dört bileşenli derleme sürümüyle de eşit
        [InlineData("v2.0.9", "2.1.0")]
        public async Task SameOrOlderRelease_IsNotUpdate(string tag, string current)
        {
            var (service, _) = Create($$"""{ "tag_name": "{{tag}}" }""", current);
            var result = await service.CheckForUpdatesAsync();

            Assert.False(result.IsUpdateAvailable);
            Assert.False(result.CheckFailed);
        }

        [Theory]
        [InlineData("v2.1.1", "2.1.1")]
        [InlineData("V2.1.1", "2.1.1")]
        [InlineData(" 2.1.1 ", "2.1.1")]
        [InlineData("v2.2.0-beta", "2.2.0")]
        [InlineData("v2.2", "2.2.0")]
        public void ParseVersion_StripsPrefixAndSuffix(string tag, string expected)
        {
            Assert.Equal(expected, UpdateCheckerService.ParseVersion(tag)?.ToString(3));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("latest")]
        [InlineData("v")]
        public void ParseVersion_InvalidReturnsNull(string? tag)
        {
            Assert.Null(UpdateCheckerService.ParseVersion(tag));
        }

        [Theory]
        [InlineData("")]
        [InlineData("bozuk json {")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("""{ "tag_name": 5 }""")]
        [InlineData("""{ "tag_name": "son-surum" }""")]
        public async Task InvalidResponse_ReturnsFailedResultWithoutThrowing(string json)
        {
            var (service, _) = Create(json);
            var result = await service.CheckForUpdatesAsync();

            Assert.True(result.CheckFailed);
            Assert.False(result.IsUpdateAvailable);
            Assert.Equal("2.1.0", result.CurrentVersion);
        }

        [Fact]
        public async Task HttpError_ReturnsFailedResult()
        {
            var (service, _) = Create("""{ "message": "rate limit" }""", status: HttpStatusCode.Forbidden);
            Assert.True((await service.CheckForUpdatesAsync()).CheckFailed);
        }

        [Fact]
        public async Task NetworkFailure_ReturnsFailedResult()
        {
            var handler = new FakeHandler(_ => throw new HttpRequestException("İnternet yok"));
            var service = new UpdateCheckerService(new HttpClient(handler), new Version(2, 1, 0));
            Assert.True((await service.CheckForUpdatesAsync()).CheckFailed);
        }

        [Fact]
        public async Task NonGitHubUrls_AreReplacedOrDropped()
        {
            var (service, _) = Create("""
                {
                  "tag_name": "v3.0.0",
                  "html_url": "file:///C:/Windows/System32/calc.exe",
                  "assets": [ { "browser_download_url": "http://evil.example/setup.exe" } ]
                }
                """);
            var result = await service.CheckForUpdatesAsync();

            Assert.True(result.IsUpdateAvailable);
            Assert.Equal(UpdateCheckerService.ReleasesPageUrl, result.ReleaseUrl);
            Assert.Null(result.DownloadUrl);
        }

        [Fact]
        public async Task GitHubUrlsOfOtherRepositories_AreReplacedOrDropped()
        {
            var (service, _) = Create("""
                {
                  "tag_name": "v3.0.0",
                  "html_url": "https://github.com/saldirgan/TRWhisper-Guncel/releases/tag/v3.0.0",
                  "assets": [
                    { "browser_download_url": "https://github.com/saldirgan/TRWhisper-Guncel/releases/download/v3.0.0/TRWhisper-Setup.exe" },
                    { "browser_download_url": "https://github.com/kagangungor/TRWhisperX/releases/download/v3.0.0/TRWhisper-Setup.exe" },
                    { "browser_download_url": "https://github.com:8443/kagangungor/TRWhisper/releases/download/v3.0.0/TRWhisper-Setup.exe" }
                  ]
                }
                """);
            var result = await service.CheckForUpdatesAsync();

            Assert.Equal(UpdateCheckerService.ReleasesPageUrl, result.ReleaseUrl);
            Assert.Null(result.DownloadUrl);
        }
    }
}
