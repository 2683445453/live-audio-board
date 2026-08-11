using LiveAudioBoard.Core.Downloads;
using LiveAudioBoard.Providers;

namespace LiveAudioBoard.Tests;

public sealed class CompositeAudioSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_RoutesEachSourceToItsOwningProvider()
    {
        var first = new FakeProvider("first");
        var second = new FakeProvider("second");
        var composite = new CompositeAudioSearchProvider([first, second]);

        await composite.SearchAsync("rain", composite.Sources[1]);

        Assert.Equal(0, first.CallCount);
        Assert.Equal(1, second.CallCount);
        Assert.Equal(["first", "second"], composite.Sources.Select(source => source.Id));
    }

    private sealed class FakeProvider(string sourceId) : IAudioSearchProvider
    {
        public int CallCount { get; private set; }

        public string Id => sourceId;

        public string DisplayName => sourceId;

        public IReadOnlyList<AudioSourceSite> Sources { get; } =
            [new(sourceId, sourceId, sourceId)];

        public Task<AudioSearchPage> SearchAsync(
            string query,
            AudioSourceSite source,
            int page = 1,
            int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new AudioSearchPage([], 0, page, 0));
        }
    }
}
