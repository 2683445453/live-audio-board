using LiveAudioBoard.Core.Abstractions;
using LiveAudioBoard.Core.Models;
using NAudio.Wave;

namespace LiveAudioBoard.Audio;

public sealed class NaudioAudioMetadataReader : IAudioMetadataReader
{
    public AudioMetadata Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var reader = new AudioFileReader(filePath);
        return new AudioMetadata((long)reader.TotalTime.TotalMilliseconds);
    }
}

