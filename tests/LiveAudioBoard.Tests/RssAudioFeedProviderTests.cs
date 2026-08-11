using System.Net;
using System.Text;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.Tests;

public sealed class RssAudioFeedProviderTests
{
    [Fact]
    public async Task LoadAsync_MapsRssEnclosuresAndSkipsNonAudioItems()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"
                 xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd"
                 xmlns:dc="http://purl.org/dc/elements/1.1/"
                 xmlns:cc="http://creativecommons.org/ns#">
              <channel>
                <title>直播音效周刊</title>
                <description>每周开放音效</description>
                <item>
                  <guid>episode-1</guid>
                  <title>Rain &amp; Thunder</title>
                  <dc:creator>Field Recorder</dc:creator>
                  <link>https://example.test/episodes/1</link>
                  <itunes:duration>01:05</itunes:duration>
                  <cc:license rdf:resource="https://creativecommons.org/licenses/by/4.0/"
                              xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" />
                  <enclosure url="https://cdn.example.test/rain.mp3"
                             type="audio/mpeg"
                             length="123456" />
                </item>
                <item>
                  <title>Article only</title>
                  <link>https://example.test/article</link>
                </item>
              </channel>
            </rss>
            """;
        using var client = CreateClient(xml);
        var provider = new RssAudioFeedProvider(client);

        var feed = await provider.LoadAsync(new Uri("https://example.test/feed.xml"));

        Assert.Equal("直播音效周刊", feed.Title);
        Assert.Equal("每周开放音效", feed.Description);
        var item = Assert.Single(feed.Items);
        Assert.Equal("episode-1", item.Id);
        Assert.Equal("Rain & Thunder", item.Title);
        Assert.Equal("Field Recorder", item.Creator);
        Assert.Equal("https://cdn.example.test/rain.mp3", item.AudioUri.AbsoluteUri);
        Assert.Equal("https://example.test/episodes/1", item.LandingPageUri?.AbsoluteUri);
        Assert.Equal(65_000, item.DurationMilliseconds);
        Assert.Equal(123456, item.FileSize);
        Assert.Equal("mpeg", item.FileType);
        Assert.Equal("直播音效周刊", item.SourceDisplayName);
        Assert.Equal("CC BY 4.0", item.LicenseDisplay);
        Assert.Contains("creativecommons.org", item.Attribution);
    }

    [Fact]
    public async Task LoadAsync_MapsAtomRelativeEnclosure()
    {
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Open Audio</title>
              <subtitle>Short sounds</subtitle>
              <entry>
                <id>tag:example.test,2026:sound-1</id>
                <title>Bell</title>
                <author><name>Studio</name></author>
                <link rel="alternate" href="/sounds/1" />
                <link rel="enclosure" href="/media/bell.wav" type="audio/wav" length="4096" />
              </entry>
            </feed>
            """;
        using var client = CreateClient(xml);
        var provider = new RssAudioFeedProvider(client);

        var feed = await provider.LoadAsync(new Uri("https://example.test/audio/feed.atom"));

        var item = Assert.Single(feed.Items);
        Assert.Equal("Bell", item.Title);
        Assert.Equal("Studio", item.Creator);
        Assert.Equal("https://example.test/media/bell.wav", item.AudioUri.AbsoluteUri);
        Assert.Equal("https://example.test/sounds/1", item.LandingPageUri?.AbsoluteUri);
        Assert.Equal("授权见来源", item.LicenseDisplay);
    }

    [Fact]
    public async Task LoadAsync_RejectsDtdContent()
    {
        const string xml = """
            <!DOCTYPE rss [<!ENTITY example "unsafe">]>
            <rss version="2.0"><channel><title>&example;</title></channel></rss>
            """;
        using var client = CreateClient(xml);
        var provider = new RssAudioFeedProvider(client);

        await Assert.ThrowsAsync<System.Xml.XmlException>(() =>
            provider.LoadAsync(new Uri("https://example.test/feed.xml")));
    }

    private static HttpClient CreateClient(string xml) =>
        new(new XmlHandler(xml));

    private sealed class XmlHandler(string xml) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/rss+xml")
            });
    }
}
