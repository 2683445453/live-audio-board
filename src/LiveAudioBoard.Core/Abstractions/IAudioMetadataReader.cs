using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Core.Abstractions;

public interface IAudioMetadataReader
{
    AudioMetadata Read(string filePath);
}

