using System.Net;
using System.Text;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.Tests;

public sealed class InternetArchiveAudioSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_ReturnsExplicitlyLicensedOriginalAudio()
    {
        using var handler = new ArchiveHandler();
        using var client = new HttpClient(handler);
        var provider = new InternetArchiveAudioSearchProvider(client);

        var page = await provider.SearchAsync("rain", provider.Sources[0]);

        var item = Assert.Single(page.Items);
        Assert.Equal("internet_archive", item.SourceName);
        Assert.Equal("Field Recordings · Forest rain", item.Title);
        Assert.Equal("Recorder", item.Creator);
        Assert.Equal("CC BY 4.0", item.LicenseDisplay);
        Assert.Equal(
            "https://archive.org/download/field_recordings/forest%20rain.wav",
            item.AudioUri.AbsoluteUri);
        Assert.Equal(
            "https://archive.org/details/field_recordings",
            item.LandingPageUri?.AbsoluteUri);
        Assert.Equal(12_500, item.DurationMilliseconds);
        Assert.Equal(2048, item.FileSize);
        Assert.Equal(1, handler.MetadataRequestCount);
    }

    private sealed class ArchiveHandler : HttpMessageHandler
    {
        public int MetadataRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var json = request.RequestUri?.AbsolutePath.StartsWith(
                "/advancedsearch.php",
                StringComparison.OrdinalIgnoreCase) == true
                ? """
                  {
                    "response": {
                      "numFound": 2,
                      "docs": [
                        {
                          "identifier": "field_recordings",
                          "title": "Field Recordings",
                          "creator": ["Recorder"],
                          "licenseurl": "https://creativecommons.org/licenses/by/4.0/"
                        },
                        {
                          "identifier": "restricted_recording",
                          "title": "Restricted Recording",
                          "creator": "Recorder",
                          "licenseurl": "https://creativecommons.org/licenses/by-nd/4.0/"
                        }
                      ]
                    }
                  }
                  """
                : CreateMetadataResponse();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        private string CreateMetadataResponse()
        {
            MetadataRequestCount++;
            return """
                   {
                     "files": [
                       {"name":"cover.jpg","source":"original","size":"1024"},
                       {"name":"forest rain.wav","source":"original","size":"2048","length":"12.5","title":"Forest rain"},
                       {"name":"forest-rain.mp3","source":"derivative","size":"512","length":"12.5"}
                     ]
                   }
                   """;
        }
    }
}
