using LiveAudioBoard.Core.Models;
using LiveAudioBoard.Core.Playback;
using Microsoft.EntityFrameworkCore;

namespace LiveAudioBoard.Infrastructure;

internal sealed class AudioLibraryDbContext(DbContextOptions<AudioLibraryDbContext> options)
    : DbContext(options)
{
    public DbSet<AudioClip> AudioClips => Set<AudioClip>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var clip = modelBuilder.Entity<AudioClip>();
        clip.ToTable("AudioClips");
        clip.HasKey(item => item.Id);
        clip.Property(item => item.Id).ValueGeneratedNever();
        clip.Property(item => item.Title).HasMaxLength(260).IsRequired();
        clip.Property(item => item.FilePath).HasMaxLength(2048).IsRequired();
        clip.Property(item => item.ContentSha256).HasMaxLength(64);
        clip.Property(item => item.Category).HasMaxLength(120).IsRequired();
        clip.Property(item => item.DisplayOrder).HasDefaultValue(0);
        clip.Property(item => item.LoopPlayback).HasDefaultValue(false);
        clip.Property(item => item.ExclusivePlayback).HasDefaultValue(false);
        clip.Property(item => item.PlaybackRoute).HasDefaultValue(
            AudioPlaybackRoute.LiveAndMonitor);
        clip.Property(item => item.FadeInMilliseconds).HasDefaultValue(0);
        clip.Property(item => item.FadeOutMilliseconds).HasDefaultValue(0);
        clip.Property(item => item.StartOffsetMilliseconds).HasDefaultValue(0L);
        clip.Property(item => item.EndOffsetMilliseconds).HasDefaultValue(0L);
        clip.Property(item => item.UseRecommendedGain).HasDefaultValue(false);
        clip.Property(item => item.EnablePeakProtection).HasDefaultValue(true);
        clip.Property(item => item.PlaybackCooldownMilliseconds).HasDefaultValue(0);
        clip.Property(item => item.Hotkey).HasMaxLength(120);
        clip.Property(item => item.HotkeyEnabled).HasDefaultValue(true);
        clip.Property(item => item.SourceProvider).HasMaxLength(120);
        clip.Property(item => item.SourceUrl).HasMaxLength(2048);
        clip.Property(item => item.License).HasMaxLength(512);
        clip.HasIndex(item => item.FilePath).IsUnique();
        clip.HasIndex(item => item.ContentSha256).IsUnique();
    }
}
