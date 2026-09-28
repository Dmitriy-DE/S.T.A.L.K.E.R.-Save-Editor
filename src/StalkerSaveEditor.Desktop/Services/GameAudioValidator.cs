namespace StalkerSaveEditor.Desktop.Services;

/// <summary><c>--test-audio</c>: every game family has all menu sounds and its music, and each file decodes.</summary>
public static class GameAudioValidator
{
    public sealed record ValidationResult(bool Success, int VerifiedSounds, IReadOnlyList<string> Errors);

    public static ValidationResult Validate(string? soundRoot = null, string? cacheDirectory = null)
    {
        var cache = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "se-audio-check-" + Environment.ProcessId);
        var service = new GameAudioService(soundRoot, cache);
        var errors = new List<string>();
        var verified = 0;
        try
        {
            foreach (var family in GameAudioService.Families)
            {
                foreach (var (sound, file) in GameAudioService.EventFiles)
                {
                    Check(family, [file], $"{family}-{sound}");
                }

                Check(family, GameAudioService.MusicFiles[family], $"{family}-music");
            }
        }
        finally
        {
            if (cacheDirectory is null)
            {
                try
                {
                    Directory.Delete(cache, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        return new ValidationResult(errors.Count == 0, verified, errors);

        void Check(string family, string[] files, string name)
        {
            try
            {
                var wav = service.Prepare(family, files, name);
                if (wav is null || new FileInfo(wav).Length <= 44) errors.Add($"{family}: {string.Join('+', files)} missing or empty");
                else verified++;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
            {
                errors.Add($"{family}: {string.Join('+', files)}: {exception.Message}");
            }
        }
    }
}
