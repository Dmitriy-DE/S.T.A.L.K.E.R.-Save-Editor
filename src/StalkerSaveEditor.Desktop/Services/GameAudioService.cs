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
    private long _musicGeneration;
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
        var name = $"{family}-{sound}";
        // One task per sound at a time: fast hovering must not queue a pile of decode/play tasks.
        if (!_playing.TryAdd(name, 0)) return;
        _ = Task.Run(() =>
        {
            try
            {
                if (Prepare(family, [EventFiles[sound]], name) is { } wav) PlayOnce(wav);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                AppLog.Warn($"sound {sound} not played", exception);
            }
            finally
            {
                _playing.TryRemove(name, out _);
            }
        });
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _playing = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _prepareLocks = new(StringComparer.Ordinal);

    /// <summary>
    /// Decodes the given OGG files of a family into one 16-bit WAV at the current volume (cached, one
    /// file per sound: other volumes are removed); null when missing. Streams in small blocks, so a
    /// several-minute theme never sits in memory whole.
    /// </summary>
    internal string? Prepare(string family, string[] files, string name)
    {
        // Single flight per sound: a second caller waits for the first decode instead of racing on the same files.
        lock (_prepareLocks.GetOrAdd(name, static _ => new object()))
        {
            return PrepareLocked(family, files, name);
        }
    }

    private string? PrepareLocked(string family, string[] files, string name)
    {
        // One reading of the setting for both the file name and the gain: a volume change during the decode must not
        // leave a file named after one volume holding another.
        var volume = Volume;
        var target = Path.Combine(_cacheDirectory, $"{name}-v{volume}.wav");
        if (File.Exists(target)) return target;
        var sources = files.Select(file => Path.Combine(_soundRoot, family, file)).ToArray();
        if (!sources.All(File.Exists)) return null;
        Directory.CreateDirectory(_cacheDirectory);
        var temp = $"{target}.{Guid.NewGuid():N}.tmp";
        var readers = sources.Select(path => new NVorbis.VorbisReader(path)).ToArray();
        try
        {
            WriteWav(temp, readers, volume / 100f);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
        finally
        {
            foreach (var reader in readers) reader.Dispose();
        }

        File.Move(temp, target, overwrite: true);
        foreach (var stale in Directory.EnumerateFiles(_cacheDirectory, $"{name}-v*.wav"))
        {
            if (stale != target) TryDelete(stale);
        }

        return target;
    }

    /// <summary>One reader is written as is; two mono readers (Shadow of Chornobyl's theme) become one stereo stream.</summary>
    private static void WriteWav(string path, NVorbis.VorbisReader[] readers, float gain)
    {
        var stereoPair = readers.Length == 2;
        var channels = stereoPair ? 2 : readers[0].Channels;
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
        output.Write(new byte[44]);
        var inputs = readers.Select(reader => new float[reader.Channels * 4096]).ToArray();
        var pcm = new short[stereoPair ? 2 * 4096 : inputs[0].Length];
        long dataBytes = 0;
        var counts = new int[readers.Length];
        while (true)
        {
            for (var index = 0; index < readers.Length; index++)
            {
                counts[index] = readers[index].ReadSamples(inputs[index], 0, inputs[index].Length);
            }

            int written;
            if (stereoPair)
            {
                written = 2 * Math.Max(counts[0], counts[1]);
                for (var frame = 0; frame < written / 2; frame++)
                {
                    pcm[frame * 2] = ToShort(frame < counts[0] ? inputs[0][frame] : 0f, gain);
                    pcm[frame * 2 + 1] = ToShort(frame < counts[1] ? inputs[1][frame] : 0f, gain);
                }
            }
            else
            {
                written = counts[0];
                for (var index = 0; index < written; index++) pcm[index] = ToShort(inputs[0][index], gain);
            }

            if (written == 0) break;
            output.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pcm.AsSpan(0, written)));
            dataBytes += written * 2L;
        }

        output.Position = 0;
        using var header = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
        header.Write("RIFF"u8);
        header.Write((int)(36 + dataBytes));
        header.Write("WAVEfmt "u8);
        header.Write(16);
        header.Write((short)1);
        header.Write((short)channels);
        header.Write(readers[0].SampleRate);
        header.Write(readers[0].SampleRate * channels * 2);
        header.Write((short)(channels * 2));
        header.Write((short)16);
        header.Write("data"u8);
        header.Write((int)dataBytes);
    }

    private static short ToShort(float sample, float gain) => (short)Math.Clamp(sample * gain * short.MaxValue, short.MinValue, short.MaxValue);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Stops the music when the application closes (the loop would otherwise outlive it).</summary>
    public static void ShutdownIfStarted()
    {
        if (!LazyInstance.IsValueCreated) return;
        var service = LazyInstance.Value;
        lock (service._musicLock)
        {
            service._musicFamily = null;
            service._musicGeneration++;
            service.StopMusicLocked();
        }
    }

    private void UpdateMusic()
    {
        var wanted = _musicEnabled && Volume > 0 ? _family : null;
        long generation;
        lock (_musicLock)
        {
            if (wanted == _musicFamily) return;
            StopMusicLocked();
            _musicFamily = wanted;
            generation = ++_musicGeneration;
        }

        if (wanted is null) return;
        _ = Task.Run(() =>
        {
            try
            {
                if (Prepare(wanted, MusicFiles[wanted], $"{wanted}-music") is not { } wav) return;
                lock (_musicLock)
                {
                    // The request number, not the family name: after A → B → A an older "A" request would otherwise
                    // start a second loop and orphan the first.
                    if (generation != _musicGeneration) return;
                    StopMusicLocked();
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
        if (process is null || process.WaitForExit(5000)) return;
        // Disposing the handle does not stop the player; a stuck one would pile up with every click.
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
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
        // The loop ends by itself when the editor is gone (a crash leaves no music behind).
        var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("while kill -0 \"$2\" 2>/dev/null; do \"$0\" \"$1\" || exit; done");
        info.ArgumentList.Add(player);
        info.ArgumentList.Add(wav);
        info.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
