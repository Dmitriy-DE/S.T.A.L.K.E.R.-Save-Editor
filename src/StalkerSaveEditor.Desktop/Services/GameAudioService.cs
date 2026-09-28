using System.Diagnostics;
using System.Runtime.InteropServices;
using StalkerSaveEditor.Core.Diagnostics;

namespace StalkerSaveEditor.Desktop.Services;

public enum SoundEvent
{
    Click,
    Tab,
    Hover,
    Error,
    Open,
    Save,
}

/// <summary>
/// Menu sounds and main-menu music of the game whose save is open (port of the Python editor's
/// ui/game_audio.py): the original files ship in Assets/Sounds/game/{soc,clear_sky,cop}, are decoded
/// from OGG once into WAV (volume applied) in the data folder and played by the platform player
/// (winmm on Windows, afplay on macOS, pw-play/paplay/aplay on Linux). Audio never throws.
/// </summary>
public sealed class GameAudioService
{
    internal static readonly string[] Families = ["soc", "clear_sky", "cop"];

    internal static readonly IReadOnlyDictionary<SoundEvent, string> EventFiles = new Dictionary<SoundEvent, string>
    {
        [SoundEvent.Click] = "menu_accept.ogg",
        [SoundEvent.Tab] = "menu_switch.ogg",
        [SoundEvent.Hover] = "menu_select.ogg",
        [SoundEvent.Error] = "menu_decline.ogg",
        [SoundEvent.Open] = "inv_open.ogg",
        [SoundEvent.Save] = "inv_slot.ogg",
    };

    /// <summary>menu_music of each game; Shadow of Chornobyl keeps its stereo theme as two mono halves.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> MusicFiles = new Dictionary<string, string[]>
    {
        ["soc"] = ["wasteland2_l.ogg", "wasteland2_r.ogg"],
        ["clear_sky"] = ["wasteland2.ogg"],
        ["cop"] = ["menu.ogg"],
    };

    private static readonly Lazy<GameAudioService> LazyInstance = new(() => new GameAudioService());
    private readonly object _musicLock = new();
    private readonly string _soundRoot;
    private readonly string _cacheDirectory;
    private string _family = "cop";
    private bool _musicEnabled;
    private Process? _musicProcess;
    private string? _musicFamily;

    public GameAudioService(string? soundRoot = null, string? cacheDirectory = null)
    {
        _soundRoot = soundRoot ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "game");
        _cacheDirectory = cacheDirectory ?? Path.Combine(AppPaths.DataDirectory, "audio");
    }

    public static GameAudioService Instance => LazyInstance.Value;

    public bool IsEnabled { get; private set; } = true;
    public int Volume { get; private set; } = 80;

    /// <summary>Game family for a release id: stalker-soc → soc, stalker-cs(-ee) → clear_sky, everything else → cop.</summary>
    public static string FamilyOf(string? releaseId) => releaseId switch
    {
        not null when releaseId.StartsWith("stalker-soc", StringComparison.Ordinal) => "soc",
        not null when releaseId.StartsWith("stalker-cs", StringComparison.Ordinal) => "clear_sky",
        _ => "cop",
    };

    public void Apply(bool enabled, int volume, bool music)
    {
        IsEnabled = enabled;
        Volume = Math.Clamp(volume, 0, 100);
        _musicEnabled = music && enabled;
        UpdateMusic();
    }

    /// <summary>Switches sounds (and music) to the game of the opened save.</summary>
    public void UseGame(string? releaseId)
    {
        _family = FamilyOf(releaseId);
        UpdateMusic();
    }

    public void Play(SoundEvent sound)
    {
        if (!IsEnabled || Volume == 0) return;
        var family = _family;
        _ = Task.Run(() =>
        {
            try
            {
                if (Prepare(family, [EventFiles[sound]], $"{family}-{sound}") is { } wav) PlayOnce(wav);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                AppLog.Warn($"sound {sound} not played", exception);
            }
        });
    }

    /// <summary>Decodes the given OGG files of a family into one 16-bit WAV at the current volume (cached); null when missing.</summary>
    internal string? Prepare(string family, string[] files, string name)
    {
        var target = Path.Combine(_cacheDirectory, $"{name}-v{Volume}.wav");
        if (File.Exists(target)) return target;
        var sources = files.Select(file => Path.Combine(_soundRoot, family, file)).ToArray();
        if (!sources.All(File.Exists)) return null;
        var channels = sources.Select(Decode).ToArray();
        var gain = Volume / 100f;
        short[] pcm;
        int channelCount, sampleRate = channels[0].SampleRate;
        if (channels.Length == 2)
        {
            // Two mono halves → one stereo stream.
            var length = Math.Max(channels[0].Samples.Length, channels[1].Samples.Length);
            pcm = new short[length * 2];
            for (var index = 0; index < length; index++)
            {
                pcm[index * 2] = ToShort(index < channels[0].Samples.Length ? channels[0].Samples[index] : 0f, gain);
                pcm[index * 2 + 1] = ToShort(index < channels[1].Samples.Length ? channels[1].Samples[index] : 0f, gain);
            }

            channelCount = 2;
        }
        else
        {
            pcm = channels[0].Samples.Select(sample => ToShort(sample, gain)).ToArray();
            channelCount = channels[0].Channels;
        }

        Directory.CreateDirectory(_cacheDirectory);
        var temp = target + ".tmp";
        WriteWav(temp, pcm, channelCount, sampleRate);
        File.Move(temp, target, overwrite: true);
        return target;
    }

    private static (float[] Samples, int Channels, int SampleRate) Decode(string path)
    {
        using var reader = new NVorbis.VorbisReader(path);
        var samples = new List<float>((int)Math.Min(reader.TotalSamples * reader.Channels, 50_000_000));
        var buffer = new float[reader.Channels * 4096];
        int read;
        while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0) samples.AddRange(buffer.AsSpan(0, read));
        return ([.. samples], reader.Channels, reader.SampleRate);
    }

    private static short ToShort(float sample, float gain) => (short)Math.Clamp(sample * gain * short.MaxValue, short.MinValue, short.MaxValue);

    internal static void WriteWav(string path, short[] pcm, int channels, int sampleRate)
    {
        using var writer = new BinaryWriter(File.Create(path));
        var dataBytes = pcm.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in pcm) writer.Write(sample);
    }

    private void UpdateMusic()
    {
        var wanted = _musicEnabled && Volume > 0 ? _family : null;
        lock (_musicLock)
        {
            if (wanted == _musicFamily) return;
            StopMusicLocked();
            _musicFamily = wanted;
        }

        if (wanted is null) return;
        _ = Task.Run(() =>
        {
            try
            {
                if (Prepare(wanted, MusicFiles[wanted], $"{wanted}-music") is not { } wav) return;
                lock (_musicLock)
                {
                    if (_musicFamily != wanted) return;
                    _musicProcess = StartLoop(wav);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                AppLog.Warn("menu music not started", exception);
            }
        });
    }

    private void StopMusicLocked()
    {
        if (OperatingSystem.IsWindows())
        {
            _ = PlaySound(null, IntPtr.Zero, 0);
        }

        try
        {
            if (_musicProcess is { HasExited: false }) _musicProcess.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        _musicProcess?.Dispose();
        _musicProcess = null;
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint flags);

    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndLoop = 0x0008;
    private const uint SndNoStop = 0x0010;
    private const uint SndFilename = 0x00020000;

    private static void PlayOnce(string wav)
    {
        if (OperatingSystem.IsWindows())
        {
            // winmm plays one sound at a time; do not cut the music loop for a click.
            _ = PlaySound(wav, IntPtr.Zero, SndAsync | SndFilename | SndNoDefault | SndNoStop);
            return;
        }

        using var process = Start(wav);
        process?.WaitForExit(5000);
    }

    /// <summary>The music loop: winmm SND_LOOP on Windows, a looping shell on macOS/Linux (killed on stop).</summary>
    private static Process? StartLoop(string wav)
    {
        if (OperatingSystem.IsWindows())
        {
            _ = PlaySound(wav, IntPtr.Zero, SndAsync | SndFilename | SndNoDefault | SndLoop);
            return null;
        }

        var player = OperatingSystem.IsMacOS() ? "afplay" : LinuxPlayer();
        if (player is null) return null;
        var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("while :; do \"$0\" \"$1\" || exit; done");
        info.ArgumentList.Add(player);
        info.ArgumentList.Add(wav);
        return Process.Start(info);
    }

    private static Process? Start(string wav)
    {
        var player = OperatingSystem.IsMacOS() ? "afplay" : LinuxPlayer();
        if (player is null) return null;
        var info = new ProcessStartInfo(player) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(wav);
        return Process.Start(info);
    }

    private static string? _linuxPlayer;

    private static string? LinuxPlayer() => _linuxPlayer ??= new[] { "pw-play", "paplay", "aplay" }
        .FirstOrDefault(name => (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin")
            .Split(Path.PathSeparator)
            .Any(directory => File.Exists(Path.Combine(directory, name))));
}
