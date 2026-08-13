using System.Windows.Input;
using LiveAudioBoard.App.Services;
using LiveAudioBoard.Core.Models;

namespace LiveAudioBoard.Tests;

public sealed class GlobalHotkeyDefinitionTests
{
    [Fact]
    public void TryParse_NormalizesModifierAndNumberKey()
    {
        var parsed = GlobalHotkeyDefinition.TryParse(
            "alt+ctrl+1",
            out var definition,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal("Ctrl+Alt+1", definition.DisplayName);
        Assert.Equal(0x31u, definition.VirtualKey);
        Assert.True(definition.Modifiers.HasFlag(HotkeyModifiers.Control));
        Assert.True(definition.Modifiers.HasFlag(HotkeyModifiers.Alt));
        Assert.True(definition.Modifiers.HasFlag(HotkeyModifiers.NoRepeat));
    }

    [Fact]
    public void TryCreate_FunctionKeyWithoutModifier_IsAllowed()
    {
        var created = GlobalHotkeyDefinition.TryCreate(
            Key.F8,
            ModifierKeys.None,
            out var definition,
            out var error);

        Assert.True(created, error);
        Assert.Equal("F8", definition.DisplayName);
    }

    [Fact]
    public void TryCreate_UnmodifiedLetter_IsRejected()
    {
        var created = GlobalHotkeyDefinition.TryCreate(
            Key.A,
            ModifierKeys.None,
            out _,
            out var error);

        Assert.False(created);
        Assert.Contains("Ctrl", error);
    }

    [Fact]
    public void Matches_RequiresTheExactModifierCombination()
    {
        GlobalHotkeyDefinition.TryParse(
            "Ctrl+Alt+1",
            out var definition,
            out _);

        Assert.True(definition.Matches(
            0x31,
            HotkeyModifiers.Control | HotkeyModifiers.Alt));
        Assert.False(definition.Matches(0x31, HotkeyModifiers.Control));
        Assert.False(definition.Matches(
            0x31,
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift));
        Assert.False(definition.Matches(
            0x32,
            HotkeyModifiers.Control | HotkeyModifiers.Alt));
    }

    [Fact]
    public void Validator_RejectsEmergencyStopAndExistingClipBinding()
    {
        var targetId = Guid.NewGuid();
        var clips = new[]
        {
            new AudioClip
            {
                Id = Guid.NewGuid(),
                Title = "Air horn",
                Hotkey = "Ctrl+Alt+1"
            },
            new AudioClip
            {
                Id = targetId,
                Title = "Target"
            }
        };
        GlobalHotkeyDefinition.TryParse(
            "Ctrl+Alt+1",
            out var duplicate,
            out _);

        var duplicateValid = HotkeyBindingValidator.TryValidate(
            targetId,
            duplicate,
            clips,
            out var duplicateError);
        var emergencyValid = HotkeyBindingValidator.TryValidate(
            targetId,
            GlobalHotkeyDefinition.EmergencyStop,
            clips,
            out var emergencyError);

        Assert.False(duplicateValid);
        Assert.Contains("Air horn", duplicateError);
        Assert.False(emergencyValid);
        Assert.Contains("紧急停止", emergencyError);
    }
}
