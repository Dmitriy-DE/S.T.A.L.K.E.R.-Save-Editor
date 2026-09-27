namespace StalkerSaveEditor.Desktop.Services;

/// <summary>
/// Verifies the presence and integrity of all UI sound assets and validates player operations.
/// </summary>
public static class GameAudioValidator
{
    public sealed record ValidationResult(
        bool Success,
        int VerifiedSounds,
        IReadOnlyList<string> Errors);

    public static ValidationResult Validate(GameAudioService? service = null)
    {
        service ??= GameAudioService.Instance;
        var errors = new List<string>();
        var verified = 0;

        var events = new[]
        {
            SoundEvent.Click,
            SoundEvent.Tab,
            SoundEvent.Hover,
            SoundEvent.Error,
            SoundEvent.Open,
            SoundEvent.Save
        };

        foreach (var ev in events)
        {
            try
            {
                // Trigger play to test resolution
                service.Play(ev);
                verified++;
            }
            catch (Exception ex)
            {
                errors.Add($"Error testing sound {ev}: {ex.Message}");
            }
        }

        // Test volume bounds and toggling
        var origEnabled = service.IsEnabled;
        var origVolume = service.Volume;

        service.IsEnabled = false;
        service.Volume = 150;
        if (service.Volume != 100)
            errors.Add($"Volume clamping max failed: expected 100, got {service.Volume}");

        service.Volume = -20;
        if (service.Volume != 0)
            errors.Add($"Volume clamping min failed: expected 0, got {service.Volume}");

        // Restore
        service.IsEnabled = origEnabled;
        service.Volume = origVolume;

        return new ValidationResult(errors.Count == 0, verified, errors);
    }
}
