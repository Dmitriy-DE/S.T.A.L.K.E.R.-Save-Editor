using System.Globalization;
using System.Security.Cryptography;
using StalkerSaveEditor.Core;
using StalkerSaveEditor.Core.Backups;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Steam;

namespace StalkerSaveEditor.Cli;

internal static class Program
{
    private const string Usage =
        "Usage: StalkerSaveEditor.Cli <version|companion|info|inventory|orphans|set-money|set-stack|move|detach|attach-orphan|raw|dump-record|diff-record|edit> ...";

    internal static int Main(string[] args)
    {
        if (args is ["--steam-native-worker"])
        {
            return SteamNativeWorkerHost.RunAsync().GetAwaiter().GetResult();
        }

        if (args.Length >= 2 && args[0] == "--steam-native-op" && args[1] == "session")
        {
            var appId = ReadAppId(args[2..]);
            return appId is null
                ? 2
                : SteamNativeWorkerHost.RunGameSessionAsync(appId.Value).GetAwaiter().GetResult();
        }

        if (args is ["--steam-native-op", "achievements", "--app-id", var appIdText]
            && int.TryParse(appIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var listAppId)
            && listAppId > 0)
        {
            return SteamAchievementsWorkerHost.RunAchievementsAsync(listAppId).GetAwaiter().GetResult();
        }

        if (args is ["--steam-native-op", "achievement", "--app-id", var setAppIdText, "--name", var apiName, "--achieved", "1"]
            && int.TryParse(setAppIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var unlockAppId)
            && unlockAppId > 0)
        {
            return SteamAchievementsWorkerHost.RunAchievementAsync(unlockAppId, apiName, achieved: true)
                .GetAwaiter().GetResult();
        }

        if (args is ["--steam-native-op", "achievement", "--app-id", var clearAppIdText, "--name", var clearApiName, "--achieved", "0"]
            && int.TryParse(clearAppIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var clearAppId)
            && clearAppId > 0)
        {
            return SteamAchievementsWorkerHost.RunAchievementAsync(clearAppId, clearApiName, achieved: false)
                .GetAwaiter().GetResult();
        }

        try
        {
            return RunCommand(args);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or FormatException or NotSupportedException or
            KeyNotFoundException or OverflowException)
        {
            Console.Error.WriteLine("Error: " + exception.Message);
            return 2;
        }
    }

    private static int RunCommand(string[] args)
    {
        if (args is ["version"])
        {
            Console.WriteLine(ApplicationVersion.Current);
            return 0;
        }

        if (args is ["--help"] or ["-h"] or [])
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        return args[0] switch
        {
            "companion" => Companion(args),
            "info" => ReadCommand(args, ReadMode.Info),
            "inventory" => ReadCommand(args, ReadMode.Inventory),
            "orphans" => ReadCommand(args, ReadMode.Orphans),
            "set-money" => SetMoney(args),
            "set-stack" => SetStack(args),
            "edit" => Edit(args),
            "dump-record" => DumpRecord(args),
            "diff-record" => DiffRecord(args),
            "move" or "detach" or "attach-orphan" or "raw" => UnsupportedWrite(args[0]),
            _ => UnknownCommand(),
        };
    }

    private const string CompanionUsage =
        "Usage: companion <status|install|uninstall> <soc|cs|cop|all> [--game-dir DIR] [--mods DIR]";

    /// <summary>
    /// Companion mod for the installer and scripts. "all" acts on every game that is found and skips
    /// the others (exit 0), so a Setup component never fails on a machine without some of the games.
    /// </summary>
    private static int Companion(string[] args)
    {
        if (args.Length < 3 || args[1] is not ("status" or "install" or "uninstall")) throw new ArgumentException(CompanionUsage);
        string? gameDirectory = null;
        var mods = Path.Combine(AppContext.BaseDirectory, "mods", "companion");
        for (var index = 3; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--game-dir" when index + 1 < args.Length:
                    gameDirectory = args[++index];
                    break;
                case "--mods" when index + 1 < args.Length:
                    mods = args[++index];
                    break;
                default:
                    throw new ArgumentException(CompanionUsage);
            }
        }

        CompanionGame[] games = args[2] switch
        {
            "soc" => [CompanionGame.ShadowOfChernobyl],
            "cs" => [CompanionGame.ClearSky],
            "cop" => [CompanionGame.CallOfPripyat],
            "all" when gameDirectory is null => Enum.GetValues<CompanionGame>(),
            _ => throw new ArgumentException(CompanionUsage),
        };

        var installer = new CompanionInstaller(mods);
        var failed = false;
        foreach (var game in games)
        {
            var status = installer.GetStatus(game, gameDirectory);
            if (!status.GameFound)
            {
                Console.WriteLine($"{game}: game not found");
                failed |= args[2] != "all";
                continue;
            }

            if (args[1] == "status")
            {
                Console.WriteLine($"{game}: {status.GameDirectory}: " + (status.ModInstalled ? "installed " + status.Version : "not installed") +
                    (status.Issues.Count == 0 ? string.Empty : " (" + string.Join("; ", status.Issues) + ")"));
                continue;
            }

            try
            {
                var result = args[1] == "install" ? installer.Install(game, gameDirectory) : installer.Uninstall(game, gameDirectory);
                Console.WriteLine($"{game}: {args[1]} " + (result.Success ? result.Changed ? "done" : "already up to date" : "failed: " + string.Join("; ", result.Conflicts)));
                failed |= !result.Success;
            }
            catch (CompanionInstallerException exception)
            {
                Console.WriteLine($"{game}: {args[1]} failed: {exception.Message}");
                failed = true;
            }
        }

        return failed ? 1 : 0;
    }

    private static int ReadCommand(string[] args, ReadMode mode)
    {
        if (args.Length < 2 || (mode != ReadMode.Inventory && args.Length != 2) ||
            (mode == ReadMode.Inventory && args.Length > 3))
        {
            throw new ArgumentException("Invalid arguments. " + Usage);
        }

        if (mode == ReadMode.Inventory && args.Length == 3 && args[2] != "--all")
        {
            throw new ArgumentException("Unknown inventory option: " + args[2]);
        }

        var sourcePath = args[1];
        var bytes = File.ReadAllBytes(sourcePath);
        var release = DetectRequired(bytes);
        if (mode != ReadMode.Info) Console.WriteLine("Format: " + release);

        if (release == "stalker2")
        {
            var save = Stalker2SaveReader.FromBytes(bytes);
            if (mode == ReadMode.Info)
            {
                PrintInfo(bytes, release, save.CrcOk ? "CRC: OK" : "CRC: BAD", save.PackedSize,
                    save.UnpackedSize, save.Money, save.OwnedHandles.Count, save.GridHandleCount,
                    save.GridCellCount, save.Inventory.Count, save.Orphans.Count);
                foreach (var warning in save.Warnings) Console.WriteLine("Warning: " + warning);
                return 0;
            }

            if (mode == ReadMode.Inventory)
            {
                Console.WriteLine("POS        TYPE                 KEY                       COUNT   HANDLE");
                foreach (var item in save.Inventory)
                {
                    Console.WriteLine("{0,-10} {1,-20} {2,-25} {3,7}  0x{4:X8}",
                        item.PositionLabel ?? "?", item.Category, item.TypeKey, item.Count, item.Handle);
                }
                return 0;
            }

            Console.WriteLine("TYPE                 KEY     COUNT  RECORDPOS     HANDLE");
            foreach (var item in save.Orphans)
            {
                Console.WriteLine("{0,-20} {1,-7} {2,5}  0x{3:X8}  0x{4:X8}",
                    item.Category, item.TypeKey, item.Count, item.RecordOffset, item.Handle);
            }
            return 0;
        }

        var xray = ReadXRay(bytes, release);
        if (mode == ReadMode.Info)
        {
            Console.WriteLine("Integrity: X-Ray LZO/container OK");
            Console.WriteLine("Format: " + release);
            Console.WriteLine("Packed: " + bytes.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("Raw: " + XRayContainer.FromBytes(bytes).UnpackedSize.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("SHA256: " + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            Console.WriteLine("Money: " + xray.Money.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("Inventory objects: " + xray.Inventory.Count.ToString(CultureInfo.InvariantCulture));
            return 0;
        }

        if (mode == ReadMode.Inventory)
        {
            Console.WriteLine("POS        TYPE                 KEY                       COUNT   HANDLE");
            foreach (var item in xray.Inventory)
            {
                Console.WriteLine("{0,-10} {1,-20} {2,-25} {3,7}  0x{4:X4}",
                    item.PlacementType ?? "inventory", item.Category, item.TypeKey,
                    item.Count?.ToString(CultureInfo.InvariantCulture) ?? "unknown", item.Handle);
            }
            return 0;
        }

        throw new NotSupportedException("Orphan object analysis is not exposed for X-Ray by the confirmed Core reader.");
    }

    private static void PrintInfo(
        byte[] bytes,
        string release,
        string integrity,
        int packedSize,
        int unpackedSize,
        uint money,
        int owned,
        int gridHandles,
        int gridCells,
        int inventory,
        int orphans)
    {
        Console.WriteLine(integrity);
        Console.WriteLine("Format: " + release);
        Console.WriteLine("Packed: " + packedSize.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Raw: " + unpackedSize.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("SHA256: " + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Console.WriteLine("Money: " + money.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Owned handles: " + owned.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Grid handles parsed/total: " + gridHandles.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Grid cells: " + gridCells.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Inventory objects: " + inventory.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Orphans: " + orphans.ToString(CultureInfo.InvariantCulture));
    }

    private static int SetMoney(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("set-money requires SAVE and MONEY.");
        var options = ParseOptions(args, 3);
        var money = ParseUInt(args[2]);
        return PrepareAndExport(args[1], options, plan => new EditPlan(plan, money: money));
    }

    private static int SetStack(string[] args)
    {
        if (args.Length < 4) throw new ArgumentException("set-stack requires SAVE, HANDLE and COUNT.");
        var options = ParseOptions(args, 4);
        var handle = ParseUInt(args[2]);
        var count = ParseUInt(args[3]);
        return PrepareAndExport(args[1], options, plan => new EditPlan(plan, stackCounts: new Dictionary<uint, uint> { [handle] = count }));
    }

    private static int Edit(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("edit requires SAVE.");
        var options = ParseOptions(args, 2);
        if (options.Unsupported.Count > 0)
        {
            throw new NotSupportedException("The requested write kind is not supported by a confirmed Core writer: " +
                string.Join(", ", options.Unsupported));
        }

        if (options.Money is null && options.Stacks.Count == 0 && options.Adds.Count == 0 &&
            options.Durability.Count == 0 && options.Upgrades.Count == 0 && options.Placements.Count == 0)
        {
            throw new InvalidOperationException("No changes requested.");
        }

        return PrepareAndExport(args[1], options, sourceSha => new EditPlan(
            sourceSha,
            money: options.Money,
            stackCounts: options.Stacks,
            adds: options.Adds,
            durability: options.Durability,
            upgrades: options.Upgrades,
            placements: options.Placements));
    }

    private static int PrepareAndExport(
        string sourcePath,
        CommandOptions options,
        Func<string, EditPlan> makePlan)
    {
        var sourceBytes = File.ReadAllBytes(sourcePath);
        var release = DetectRequired(sourceBytes);
        var plan = makePlan(Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant());
        const EditKind readBackVerifiedKinds = EditKind.Money | EditKind.StackCounts;
        if ((plan.EditKinds & ~readBackVerifiedKinds) != EditKind.None)
        {
            throw new NotSupportedException(
                "Edit kinds other than money and stack counts are not supported because EditService.VerifyReadBack does not verify them completely.");
        }

        if (!EditService.CanEdit(release, plan.EditKinds))
        {
            throw new NotSupportedException("The capability registry does not allow this edit for " + release + ".");
        }

        var needsCatalog = plan.Adds.Count > 0 || plan.Upgrades.Count > 0;
        var catalogs = needsCatalog ? CatalogBundleReader.LoadEmbedded()[release] : null;
        var prepared = EditService.PrepareEdit(sourceBytes, plan, release, catalogs);
        var outputPath = options.OutputPath ?? DefaultOutputPath(sourcePath);
        var backupDirectory = options.BackupDirectory ?? DefaultBackupDirectory();
        var receipt = LocalSaveStorage.ExportLocal(sourcePath, outputPath, prepared, backupDirectory);
        var readBack = File.ReadAllBytes(receipt.OutputPath);
        EditService.VerifyReadBack(readBack, release, plan);
        Console.WriteLine("Output: " + receipt.OutputPath);
        Console.WriteLine("Size: " + readBack.Length.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Backup: " + receipt.BackupPath);
        Console.WriteLine("SHA256: " + receipt.OutputSha256);
        if (plan.Money is { } money) Console.WriteLine("Money: " + money.ToString(CultureInfo.InvariantCulture));
        foreach (var (handle, count) in plan.StackCounts)
        {
            Console.WriteLine("Stack 0x{0:X8}: {1}", handle, count);
        }
        foreach (var add in plan.Adds) Console.WriteLine("Add " + add.ItemKey + " x" + add.Quantity);
        foreach (var (handle, condition) in plan.Durability)
        {
            Console.WriteLine("Durability 0x{0:X8}: {1}", handle, condition.ToString("0.####", CultureInfo.InvariantCulture));
        }
        foreach (var (handle, upgrades) in plan.Upgrades)
        {
            Console.WriteLine("Upgrades 0x{0:X4}: {1}", handle, string.Join(",", upgrades));
        }
        foreach (var change in plan.Placements)
        {
            Console.WriteLine("Placement 0x{0:X4}: {1}", change.Handle, change.Type);
        }
        return 0;
    }

    private static int DumpRecord(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("dump-record requires SAVE and HANDLE.");
        var bytes = File.ReadAllBytes(args[1]);
        var release = DetectRequired(bytes);
        var handle = ParseUInt(args[2]);
        var limit = ReadLimit(args, 3, 768);
        var window = GetRecordWindow(bytes, release, handle, limit);
        Console.WriteLine("base=0x{0:X}, bytes={1}", window.Offset, window.Bytes.Length);
        var blob = window.Bytes.AsSpan();
        for (var offset = 0; offset < blob.Length; offset += 16)
        {
            var row = blob.Slice(offset, Math.Min(16, blob.Length - offset));
            Console.WriteLine("+0x{0:X4}  {1}", offset, Convert.ToHexString(row.ToArray()).ToLowerInvariant());
        }
        return 0;
    }

    private static int DiffRecord(string[] args)
    {
        if (args.Length < 4) throw new ArgumentException("diff-record requires SAVE_A, SAVE_B and HANDLE.");
        var firstBytes = File.ReadAllBytes(args[1]);
        var secondBytes = File.ReadAllBytes(args[2]);
        var firstRelease = DetectRequired(firstBytes);
        var secondRelease = DetectRequired(secondBytes);
        if (!string.Equals(firstRelease, secondRelease, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Both files must have the same detected release.");
        }

        var handle = ParseUInt(args[3]);
        var limit = ReadLimit(args, 4, 2048);
        var first = GetRecordWindow(firstBytes, firstRelease, handle, limit).Bytes;
        var second = GetRecordWindow(secondBytes, secondRelease, handle, limit).Bytes;
        var differs = false;
        var length = Math.Max(first.Length, second.Length);
        for (var index = 0; index < length; index++)
        {
            var left = index < first.Length ? first[index] : (byte?)null;
            var right = index < second.Length ? second[index] : (byte?)null;
            if (left == right) continue;
            differs = true;
            Console.WriteLine("+0x{0:X4}: {1}  ->  {2}", index,
                left is null ? "" : left.Value.ToString("x2", CultureInfo.InvariantCulture),
                right is null ? "" : right.Value.ToString("x2", CultureInfo.InvariantCulture));
        }
        if (!differs) Console.WriteLine("No differences in compared record window");
        return 0;
    }

    private static RecordSlice GetRecordWindow(byte[] bytes, string release, uint handle, int limit)
    {
        if (release == "stalker2")
        {
            var save = Stalker2SaveReader.FromBytes(bytes);
            var item = save.Inventory.FirstOrDefault(entry => entry.Handle == handle)
                ?? throw new InvalidOperationException("The requested handle is not in the parsed inventory.");
            var raw = save.Raw.Span;
            var start = item.RecordOffset;
            var end = Math.Min(Math.Min(item.RecordEndGuess, checked(start + limit)), raw.Length);
            if (start < 0 || end < start) throw new InvalidDataException("The object record window is invalid.");
            return new RecordSlice(start, raw.Slice(start, end - start).ToArray());
        }

        var parsed = ReadXRay(bytes, release);
        if (handle is 0 or > ushort.MaxValue) throw new InvalidOperationException("The handle is outside the X-Ray range.");
        var record = parsed.FindObjectRecord((ushort)handle)
            ?? throw new InvalidOperationException("The requested handle is not in the parsed X-Ray object registry.");
        var length = Math.Min(limit, record.Bytes.Length);
        return new RecordSlice(record.Offset, record.Bytes[..length].ToArray());
    }

    private sealed record RecordSlice(int Offset, byte[] Bytes);

    private static int ReadLimit(string[] args, int start, int defaultValue)
    {
        if (start == args.Length) return defaultValue;
        if (start + 2 != args.Length || args[start] != "--limit") throw new ArgumentException("Expected --limit VALUE.");
        var limit = int.Parse(args[start + 1], CultureInfo.InvariantCulture);
        if (limit is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(args), "Record limit must be in 1…65536.");
        return limit;
    }

    private static CommandOptions ParseOptions(string[] args, int start)
    {
        var result = new CommandOptions();
        for (var index = start; index < args.Length; index++)
        {
            var option = args[index];
            string Value()
            {
                if (++index >= args.Length) throw new ArgumentException("Missing value after " + option + ".");
                return args[index];
            }

            switch (option)
            {
                case "-o":
                case "--output":
                    result.OutputPath = Value();
                    break;
                case "--backup-dir":
                    result.BackupDirectory = Value();
                    break;
                case "--money":
                    result.Money = ParseUInt(Value());
                    break;
                case "--stack":
                    AddUnique(result.Stacks, Value(), ParseAssignment);
                    break;
                case "--add":
                    var add = ParseStringAssignment(Value());
                    result.Adds.Add(new ItemAddRequest(add.Key, ParseUInt(add.Value)));
                    break;
                case "--durability":
                    var condition = ParseStringAssignment(Value());
                    result.Durability.Add(ParseUInt(condition.Key), double.Parse(condition.Value, CultureInfo.InvariantCulture));
                    break;
                case "--upgrade":
                    var upgrade = ParseStringAssignment(Value());
                    var keys = upgrade.Value.Length == 0 ? Array.Empty<string>() : upgrade.Value.Split(',');
                    result.Upgrades.Add(checked((ushort)ParseUInt(upgrade.Key)), Array.AsReadOnly(keys));
                    break;
                case "--placement":
                    var placement = ParseStringAssignment(Value());
                    var pieces = placement.Value.Split(':', 2);
                    result.Placements.Add(new XRayPlacementChange(
                        ParseUInt(placement.Key),
                        pieces[0],
                        pieces.Length == 2 && pieces[0] == "slot" ? int.Parse(pieces[1], CultureInfo.InvariantCulture) : null));
                    break;
                case "--move":
                case "--detach":
                case "--attach":
                case "--raw":
                    result.Unsupported.Add(option);
                    _ = Value();
                    break;
                default:
                    throw new ArgumentException("Unknown option: " + option);
            }
        }
        return result;
    }

    private static void AddUnique(Dictionary<uint, uint> destination, string specification, Func<string, KeyValuePair<uint, uint>> parse)
    {
        var pair = parse(specification);
        if (!destination.TryAdd(pair.Key, pair.Value)) throw new ArgumentException("Duplicate handle in edit options.");
    }

    private static KeyValuePair<uint, uint> ParseAssignment(string value)
    {
        var parsed = ParseStringAssignment(value);
        return new KeyValuePair<uint, uint>(ParseUInt(parsed.Key), ParseUInt(parsed.Value));
    }

    private static KeyValuePair<string, string> ParseStringAssignment(string value)
    {
        var separator = value.IndexOf('=');
        if (separator <= 0 || separator == value.Length - 1) throw new FormatException("Expected KEY=VALUE.");
        return new KeyValuePair<string, string>(value[..separator], value[(separator + 1)..]);
    }

    private static uint ParseUInt(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.Parse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        if (value.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToUInt32(value[2..], 2);
        }

        if (value.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToUInt32(value[2..], 8);
        }

        return uint.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static string DetectRequired(ReadOnlySpan<byte> data) =>
        EditService.DetectFormat(data) ?? throw new InvalidDataException("Unknown or unsupported save format.");

    private static XRayTrilogySave ReadXRay(ReadOnlySpan<byte> data, string release) =>
        release.EndsWith("-ee", StringComparison.Ordinal)
            ? XRayEnhancedReader.FromBytes(data)
            : XRayTrilogyReader.FromBytes(data);

    private static string DefaultOutputPath(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(fullPath) + "_edited" + Path.GetExtension(fullPath));
    }

    private static string DefaultBackupDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StalkerSaveEditor", "backups");

    private static int UnsupportedWrite(string command) =>
        throw new NotSupportedException(command + " is not supported by a confirmed Core writer.");

    private static int UnknownCommand()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }

    private static int? ReadAppId(string[] args)
    {
        if (args.Length != 2 || args[0] != "--app-id" ||
            !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var appId) ||
            appId <= 0)
        {
            Console.Error.WriteLine("Usage: StalkerSaveEditor.Cli --steam-native-op session --app-id <positive-id>");
            return null;
        }

        return appId;
    }

    private enum ReadMode
    {
        Info,
        Inventory,
        Orphans,
    }

    private sealed class CommandOptions
    {
        public string? OutputPath { get; set; }
        public string? BackupDirectory { get; set; }
        public uint? Money { get; set; }
        public Dictionary<uint, uint> Stacks { get; } = [];
        public List<ItemAddRequest> Adds { get; } = [];
        public Dictionary<uint, double> Durability { get; } = [];
        public Dictionary<ushort, IReadOnlyList<string>> Upgrades { get; } = [];
        public List<XRayPlacementChange> Placements { get; } = [];
        public List<string> Unsupported { get; } = [];
    }
}
