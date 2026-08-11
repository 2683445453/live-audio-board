using LiveAudioBoard.Core.Rendering;

namespace LiveAudioBoard.Core.Abstractions;

public interface IAudioClipRenderer
{
    Task<AudioClipRenderResult> RenderAsync(
        AudioClipRenderOptions options,
        CancellationToken cancellationToken = default);
}
