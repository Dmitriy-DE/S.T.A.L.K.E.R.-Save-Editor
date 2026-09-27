using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace StalkerSaveEditor.Desktop.Services;

public enum SoundEvent
{
    Click,
    Tab,
    Hover,
    Error,
    Open,
    Save
}

/// <summary>
/// Cross-platform non-blocking game audio player for S.T.A.L.K.E.R. UI sounds.
/// Plays original trilogy menu sounds and quiet interface cues.
/// </summary>
public sealed class GameAudioService
{
    private static readonly Lazy<GameAudioService> _instance = new(() => new GameAudioService());
    public static GameAudioService Instance => _instance.Value;

    private readonly ConcurrentDictionary<SoundEvent, string?> _cachedSoundFiles = new();
    private readonly string _cacheDir;
    private bool _isEnabled = true;
    private int _volume = 80;

    public GameAudioService()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "stalker_save_editor_audio");
        try
        {
            Directory.CreateDirectory(_cacheDir);
        }
        catch
        {
            // fallback
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public int Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0, 100);
    }

    public void Play(SoundEvent sound)
    {
        if (!_isEnabled || _volume <= 0) return;

        Task.Run(() =>
        {
            try
            {
                PlayInternal(sound);
            }
            catch
            {
                // Audio errors should never crash the application or interfere with UI
            }
        });
    }

    private void PlayInternal(SoundEvent sound)
    {
        var filePath = GetSoundFilePath(sound);
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            PlayWindows(filePath);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            PlayMac(filePath);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            PlayLinux(filePath);
        }
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySound", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_FILENAME = 0x00020000;

    private static void PlayWindows(string filePath)
    {
        try
        {
            PlaySound(filePath, IntPtr.Zero, SND_ASYNC | SND_FILENAME);
        }
        catch
        {
            // Ignore sound errors
        }
    }

    private static void PlayMac(string filePath)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "afplay",
                Arguments = $"\"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            proc?.WaitForExit(1000);
        }
        catch
        {
            // Ignore sound errors
        }
    }

    private static void PlayLinux(string filePath)
    {
        string[] players = ["pw-play", "paplay", "aplay"];
        foreach (var player in players)
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = player,
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                if (proc is not null)
                {
                    proc.WaitForExit(1000);
                    return;
                }
            }
            catch
            {
                // Try next player command
            }
        }
    }

    private string? GetSoundFilePath(SoundEvent sound)
    {
        return _cachedSoundFiles.GetOrAdd(sound, s =>
        {
            var soundName = s switch
            {
                SoundEvent.Click => "click",
                SoundEvent.Tab => "tab",
                SoundEvent.Hover => "hover",
                SoundEvent.Error => "error",
                SoundEvent.Open => "open",
                SoundEvent.Save => "save",
                _ => "click"
            };

            var wavFileName = $"{soundName}.wav";
            var targetPath = Path.Combine(_cacheDir, wavFileName);

            if (File.Exists(targetPath))
                return targetPath;

            // 1. Try reading from embedded resources
            var asm = typeof(GameAudioService).Assembly;
            var resNames = new[]
            {
                $"StalkerSaveEditor.Desktop.Assets.Sounds.{wavFileName}",
                $"StalkerSaveEditor.Desktop.Assets.Sounds.{soundName}.ogg",
            };

            foreach (var resName in resNames)
            {
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream is not null)
                {
                    try
                    {
                        var ext = Path.GetExtension(resName);
                        var outPath = Path.Combine(_cacheDir, $"{soundName}{ext}");
                        using var fileStream = File.Create(outPath);
                        stream.CopyTo(fileStream);
                        return outPath;
                    }
                    catch
                    {
                        // continue
                    }
                }
            }

            // 2. Try file system search paths
            var candidateDirs = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds"),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "StalkerSaveEditor.Desktop", "Assets", "Sounds"),
            };

            foreach (var dir in candidateDirs)
            {
                var localWav = Path.Combine(dir, wavFileName);
                if (File.Exists(localWav)) return localWav;

                var localOgg = Path.Combine(dir, $"{soundName}.ogg");
                if (File.Exists(localOgg)) return localOgg;
            }

            return null;
        });
    }
}
