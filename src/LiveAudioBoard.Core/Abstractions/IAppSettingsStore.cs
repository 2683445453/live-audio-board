using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Core.Abstractions;

public interface IAppSettingsStore
{
    string SettingsPath { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
