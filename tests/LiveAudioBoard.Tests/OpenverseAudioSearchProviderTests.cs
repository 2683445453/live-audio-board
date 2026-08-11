using System.Net;
using System.Text;
using LiveAudioBoard.Core.Downloads;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.Tests;

public sealed class OpenverseAudioSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_MapsOpenLicenseAudioAndSourceMetadata()
    {
        const string json = """
            {
              "result_count": 42,
              "page_count": 9,
              "page": 2,
              "results": [
                {
                  "id": "audio-1",
                  "title": "Rain &amp; Thunder.wav",
                  "creator": "Field Recorder",
                  "source": "freesound",
                  "provider": "freesound",
                  "license": "by",
                  "license_version": "4.0",
                  "license_url": "https://creativecommons.org/licenses/by/4.0/",
                  "url": "https://cdn.example.test/rain.mp3",
                  "foreign_landing_url": "https://freesound.org/s/1",
                  "duration": 65234,
                  "filesize": 123456,
                  "filetype": "mp3",
                  "attribution": "Rain by Field Recorder, CC BY 4.0"
                }
              ]
            }
            """;
        var handler = new CapturingHandler(json);
        using var client = new HttpClient(handler);
        var provider = new OpenverseAudioSearchProvider(client);
        var source = provider.Sources.Single(item => item.Id == "freesound");

        var result = await provider.SearchAsync("rain sound", source, page: 2, pageSize: 5);

        var item = Assert.Single(result.Items);
        Assert.Equal(42, result.TotalResults);
        Assert.Equal(2, result.Page);
        Assert.Equal(9, result.PageCount);
        Assert.Equal("Rain & Thunder.wav", item.Title);
        Assert.Equal("Field Recorder", item.CreatorDisplay);
        Assert.Equal("Freesound", item.SourceDisplayName);
        Assert.Equal("CC BY 4.0", item.LicenseDisplay);
        Assert.Equal("1:05", item.DurationText);
        Assert.Equal("https://cdn.example.test/rain.mp3", item.AudioUri.AbsoluteUri);
        Assert.NotNull(handler.RequestUri);
        Assert.Contains("source=freesound", handler.RequestUri.Query);
        Assert.Contains("page=2", handler.RequestUri.Query);
        Assert.Contains("page_size=5", handler.RequestUri.Query);
        Assert.Contains("license=cc0%2Cpdm%2Cby", handler.RequestUri.Query);
    }

    [Fact]
    public async Task SearchAsync_WithUnknownSource_RejectsRequestBeforeNetworkCall()
    {
        var handler = new CapturingHandler("{}");
        using var client = new HttpClient(handler);
        var provider = new OpenverseAudioSearchProvider(client);

        await Assert.ThrowsAsync<ArgumentException>(() => provider.SearchAsync(
            "rain",
            new AudioSourceSite("unknown", "Unknown", "")));
        Assert.Null(handler.RequestUri);
    }

    private sealed class CapturingHandler(string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
