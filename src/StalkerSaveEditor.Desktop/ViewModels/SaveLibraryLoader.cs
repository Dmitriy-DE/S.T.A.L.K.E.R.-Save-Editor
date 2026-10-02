using StalkerSaveEditor.Core.Diagnostics;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Capabilities;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;
using StalkerSaveEditor.Desktop.Services;

namespace StalkerSaveEditor.Desktop.ViewModels;

/// <summary>
/// Finds save files and parses them into <see cref="SaveFileSummary"/> rows (off the UI thread, cached by file
/// identity). Split out of SaveLibraryViewModel: no view state lives here.
/// </summary>
internal static class SaveLibraryLoader
{
    /// <summary>
    /// A parsed save (null: not a save), reused while the file keeps its size, write time, creation time and
    /// <see cref="Probe"/> — a hash of its first and last 4 KiB. A copy tool or a cloud client can put different
    /// bytes in place with the same size and times; the probe catches that for the price of two small reads
    /// (the save header and its checksum trailer are in those blocks).
    /// </summary>
    internal sealed record CachedSave(long Length, DateTime LastWriteUtc, DateTime CreationUtc, SaveFileSummary? Summary, ulong Probe = 0);

    internal static ulong ContentProbe(string path)
    {
        const int Block = 4096;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Block);
            Span<byte> buffer = stackalloc byte[2 * Block];
            var head = stream.ReadAtLeast(buffer[..Block], Block, throwOnEndOfStream: false);
            var tail = 0;
            if (stream.Length > Block)
            {
                stream.Seek(Math.Max(Block, stream.Length - Block), SeekOrigin.Begin);
                tail = stream.ReadAtLeast(buffer.Slice(head, Block), Block, throwOnEndOfStream: false);
            }

            Span<byte> hash = stackalloc byte[32];
            System.Security.Cryptography.SHA256.HashData(buffer[..(head + tail)], hash);
            // Never 0: that value means "no probe was taken".
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(hash) | 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    internal const long HeavySaveBytes = 16L * 1024 * 1024;
    private static readonly SemaphoreSlim HeavySaves = new(2, 2);

    /// <summary>
    /// Lists and parses the save files, newest first, in parallel batches; unchanged files (and files
    /// known not to be saves) come from <paramref name="cache"/>. <paramref name="batchLoaded"/> sees
    /// the saves of each finished batch, so a first load can show the newest ones at once.
    /// </summary>
    internal static (List<SaveFileSummary> Saves, Dictionary<string, CachedSave> Cache) LoadLibrary(
        IReadOnlyList<string> directories,
        IReadOnlyDictionary<string, CachedSave> cache,
        Action<IReadOnlyList<SaveFileSummary>>? batchLoaded = null,
        Func<bool>? superseded = null)
    {
        var files = EnumerateSaveFiles(directories)
            .Select(path => new FileInfo(path))
            .Where(info => info.Exists)
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToArray();
        var entries = new CachedSave[files.Length];
        var parallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 8);
        for (var start = 0; start < files.Length; start += parallelism * 2)
        {
            if (superseded?.Invoke() == true) return ([], new Dictionary<string, CachedSave>(cache, StringComparer.Ordinal));
            var end = Math.Min(files.Length, start + parallelism * 2);
            Parallel.For(start, end, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, index =>
            {
                var info = files[index];
                var probe = ContentProbe(info.FullName);
                if (cache.TryGetValue(info.FullName, out var hit) && hit.Length == info.Length && hit.LastWriteUtc == info.LastWriteTimeUtc &&
                    hit.CreationUtc == info.CreationTimeUtc && hit.Probe == probe && probe != 0)
                {
                    entries[index] = hit;
                    return;
                }

                // Memory, not CPU, limits big saves: a large one needs several times its size while it is unpacked
                // and parsed, so only a couple of those run at once while small saves keep the full parallelism.
                var heavy = info.Length >= HeavySaveBytes;
                if (heavy) HeavySaves.Wait();
                try
                {
                    entries[index] = new CachedSave(info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc, TryReadSave(info.FullName), probe);
                }
                finally
                {
                    if (heavy) HeavySaves.Release();
                }
            });
            batchLoaded?.Invoke(entries[start..end].Select(entry => entry.Summary).OfType<SaveFileSummary>().ToArray());
        }

        var next = new Dictionary<string, CachedSave>(StringComparer.Ordinal);
        for (var index = 0; index < files.Length; index++) next[files[index].FullName] = entries[index];
        return (entries.Select(entry => entry.Summary).OfType<SaveFileSummary>().ToList(), next);
    }

    internal static IEnumerable<string> EnumerateSaveFiles(IEnumerable<string> directories)
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            string[] files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    MaxRecursionDepth = 4,
                }).Where(IsSupportedSaveFile).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                AppLog.Warn($"save folder not listed: {exception.GetType().Name}: {exception.Message}");
                continue;
            }

            foreach (var file in files)
            {
                if (IsBackupArtifact(file)) continue;
                var fullPath = Path.GetFullPath(file);
                if (seen.Add(fullPath)) yield return fullPath;
            }
        }
    }

    internal static SaveFileSummary? TryReadSave(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > 512L * 1024 * 1024) return null;
            var bytes = File.ReadAllBytes(path);
            var sourceSha256 = Sha256(bytes);

            try
            {
                return FromXRay(XRayTrilogyReader.FromBytes(bytes), path, sourceSha256, info.Length, info.LastWriteTime);
            }
            catch (XRayFormatException) { }

            try
            {
                return FromXRay(XRayEnhancedReader.FromBytes(bytes), path, sourceSha256, info.Length, info.LastWriteTime);
            }
            catch (XRayFormatException) { }

            try
            {
                return FromStalker2(Stalker2SaveReader.FromBytes(bytes), path, sourceSha256, info.Length, info.LastWriteTime);
            }
            catch (Stalker2FormatException exception)
            {
                AppLog.Warn($"not recognised {Path.GetFileName(path)} ({info.Length} bytes): {exception.Message}");
                return null;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Not a save the readers know (a damaged file, a foreign format): logged, listed nowhere.
            AppLog.Warn($"skipped {Path.GetFileName(path)}: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    internal static (bool Writable, string? Reason) CheckCapability(string releaseId, string capability)
    {
        if (!EditService.CanEdit(releaseId))
        {
            var isS2 = string.Equals(releaseId, "stalker2", StringComparison.OrdinalIgnoreCase);
            var reason = isS2
                ? L.T("Запись S.T.A.L.K.E.R. 2 выключена в UI до верификации мутаций в живой игре.")
                : L.T("Запись для формата {0} выключена в UI в целях безопасности.", releaseId);
            return (false, reason);
        }

        try
        {
            var support = CapabilityRegistry.Get(releaseId, capability);
            return (support.Writable, support.Writable ? null : (support.Reason ?? L.T("Операция не поддерживается данным форматом")));
        }
        catch (KeyNotFoundException)
        {
            return (false, L.T("Операция не поддерживается данным форматом"));
        }
    }

    internal static bool HasCapability(string releaseId, string capability) =>
        CheckCapability(releaseId, capability).Writable;

    internal static SaveFileSummary FromXRay(
        XRayTrilogySave save,
        string path,
        string sourceSha256,
        long fileSize,
        DateTime lastModified)
    {
        var formatId = save.FormatId;
        var (canEditMoney, moneyReason) = CheckCapability(formatId, "edit_money");
        var (canEditStacks, stacksReason) = CheckCapability(formatId, "edit_stacks");
        var (canEditDurability, durabilityReason) = CheckCapability(formatId, "edit_durability");
        var (canEditPlacement, placementReason) = CheckCapability(formatId, "edit_placement");
        var (canEditUpgrades, upgradesReason) = CheckCapability(formatId, "edit_upgrades");
        var (canEditRelations, relationsReason) = CheckCapability(formatId, "edit_relations");
        var (canEditPlayerFaction, playerFactionReason) = CheckCapability(formatId, "edit_player_faction");
        var canEditFactions = canEditRelations || canEditPlayerFaction;
        var factionReason = canEditFactions ? null : (relationsReason ?? playerFactionReason);
        var (canMoveItems, moveReason) = CheckCapability(formatId, "move_items");
        var canEditStashes = canMoveItems && save.Stashes.Count > 0;
        var stashesReason = !canMoveItems ? moveReason : (save.Stashes.Count == 0 ? L.T("В сохранении нет тайников") : null);
        var (canAddItems, addReason) = CheckCapability(formatId, "add_items");
        var (canRemoveItems, removeReason) = CheckCapability(formatId, "remove_items");

        var catalog = SaveNaming.TryCatalog(formatId, out var bundle) ? bundle : null;
        var upgradeCatalog = catalog?.Upgrades;

        var inventory = save.Inventory.Select(item =>
        {
            var localizedName = SaveNaming.ItemName(formatId, item.TypeKey);
            var availableUpgrades = upgradeCatalog?.ForItem(item.TypeKey);
            return new InventoryLineViewModel(
                localizedName,
                item.TypeKey,
                item.Handle,
                item.Category,
                item.Count,
                canEditStacks && item.EditableCount,
                item.Condition,
                canEditDurability && item.ConditionEditable,
                item.PlacementType,
                canEditPlacement && item.PlacementEditable,
                item.Upgrades,
                canEditUpgrades,
                availableUpgrades,
                countDisabledReason: stacksReason,
                conditionDisabledReason: durabilityReason,
                placementDisabledReason: placementReason,
                upgradesDisabledReason: upgradesReason,
                baseSlot: item.PlacementBaseSlot,
                releaseId: formatId);
        });

        var stashes = save.Stashes.Select(s => new StashViewModel(
            s.Handle,
            PlaceNames.Stash(formatId, s.Name, s.Handle),
            PlaceNames.LevelOfObject(formatId, s.Name) ?? (s.Level is null ? null : L.T(s.Level)),
            s.Items.Select(i => new StashItemViewModel(
                i.Handle,
                i.TypeKey,
                SaveNaming.ItemName(formatId, i.TypeKey),
                i.Count ?? 1,
                canEdit: canEditStashes,
                disabledReason: stashesReason)),
            objectName: s.Name));

        var factionRelations = new List<FactionRelationViewModel>();
        var factionCatalog = catalog?.Factions;
        if (factionCatalog is not null)
        {
            foreach (var relation in save.FactionRelations)
            {
                var factionDef = factionCatalog.Factions.FirstOrDefault(f => f.NumericId == relation.CommunityIndex);
                var commKey = factionDef?.Key ?? $"faction_{relation.CommunityIndex}";
                var localizedFaction = SaveNaming.OfficialNames.Resolve(formatId, "factions", commKey, SaveNaming.NamesLanguage)
                    ?? (factionDef?.DisplayName is { } known ? L.T(known) : L.T("Группировка № {0}", relation.CommunityIndex));
                factionRelations.Add(new FactionRelationViewModel(commKey, localizedFaction, relation.Value, canEditFactions, factionReason));
            }
        }

        string? playerFaction = null;
        if (save.PlayerFactionIndex.HasValue && factionCatalog is not null)
        {
            var def = factionCatalog.Factions.FirstOrDefault(f => f.NumericId == save.PlayerFactionIndex.Value);
            playerFaction = SaveNaming.OfficialNames.Resolve(formatId, "factions", def?.Key, SaveNaming.NamesLanguage)
                ?? def?.DisplayName
                ?? def?.Key;
        }

        // Real level changers from X-Ray registry (read-only per AGENTS.md)
        var destinations = XRayRelocation.ReadDestinations(save);
        var transitions = save.LevelChangers.Select(lc =>
        {
            destinations.TryGetValue(lc.Handle, out var destination);
            return new TransitionViewModel(
                lc.Handle,
                lc.Name,
                lc.NameReplace,
                lc.ParentId,
                lc.ObjectVersion,
                sourceLevel: PlaceNames.LevelOfObject(formatId, lc.NameReplace),
                destLevel: destination is null ? null : PlaceNames.Level(formatId, destination.DestLevelName),
                destPoint: destination?.DestLevelPointName);
        }).ToList();

        return new SaveFileSummary(
            path,
            ReleaseName(formatId),
            formatId,
            sourceSha256,
            save.Money,
            canEditMoney,
            inventory,
            fileSize,
            lastModified,
            save.ActorName,
            save.ActorHealth,
            save.ActorRank,
            save.ActorReputation,
            save.GameTime,
            save.TimeFactor,
            playerFaction,
            canEditFactions,
            canEditUpgrades,
            canEditDurability,
            canEditPlacement,
            canEditStashes,
            canAddItems,
            canRemoveItems,
            crcOk: true,
            stashes: stashes,
            transitions: transitions,
            factionRelations: factionRelations,
            moneyDisabledReason: moneyReason,
            factionDisabledReason: factionReason,
            upgradesDisabledReason: upgradesReason,
            durabilityDisabledReason: durabilityReason,
            placementDisabledReason: placementReason,
            stashesDisabledReason: stashesReason,
            addItemsDisabledReason: addReason,
            removeItemsDisabledReason: removeReason)
        {
            Progress = XRayProgressReader.Read(save),
            Weather = XRayWeatherReader.Read(save),
            RelocationAnchors = XRayRelocation.IsSupported(formatId)
                ? XRayRelocation.ReadAnchors(save).Select(anchor => new RelocationAnchorViewModel(anchor, PlaceNames.Level(formatId, anchor.DestinationLevel))).ToArray()
                : [],
            ActorLocation = XRayRelocation.IsSupported(formatId) ? XRayRelocation.ReadActorLocation(save) : null,
        };
    }

    internal static SaveFileSummary FromStalker2(
        Stalker2Save save,
        string path,
        string sourceSha256,
        long fileSize,
        DateTime lastModified)
    {
        const string formatId = "stalker2";
        // A save in the layout of game 1.0.x is shown, never edited (Core refuses to write it as well).
        (bool Allowed, string? Reason) Capability(string capability) => save.IsLegacy
            ? (false, L.T("Сохранение записано игрой версии 1.0.x: только чтение. Загрузите его в текущей версии игры и сохраните заново."))
            : CheckCapability(formatId, capability);
        var (canEditMoney, moneyReason) = Capability("edit_money");
        var (canEditStacks, stacksReason) = Capability("edit_stacks");
        var (canEditDurability, durabilityReason) = Capability("edit_durability");
        var (canEditPlacement, placementReason) = Capability("edit_placement");
        var (canEditUpgrades, upgradesReason) = Capability("edit_upgrades");
        var (canEditRelations, relationsReason) = Capability("edit_relations");
        var (canEditPlayerFaction, playerFactionReason) = Capability("edit_player_faction");
        var canEditFaction = canEditRelations || canEditPlayerFaction;
        var factionReason = canEditFaction ? null : (relationsReason ?? playerFactionReason);
        var (canEditStashes, stashesReason) = Capability("move_items");
        var (canAddItems, addReason) = Capability("add_items");
        var (canRemoveItems, removeReason) = Capability("remove_items");

        var catalog = SaveNaming.TryCatalog(formatId, out var bundle) ? bundle.Items : null;
        var s2Items = Stalker2ItemCatalog.LoadEmbedded();
        var language = SaveNaming.NamesLanguage;
        var inventory = save.Inventory.Select(item => new InventoryLineViewModel(
            s2Items.Name(item.DisplayName, language) ?? item.DisplayName ?? catalog?.Resolve(item.TypeKey)?.DisplayName ?? item.TypeKey,
            item.TypeKey,
            item.Handle,
            item.Category,
            item.Count,
            canEditCount: canEditStacks,
            item.Condition,
            canEditCondition: canEditDurability,
            item.Storage,
            canEditPlacement: canEditPlacement,
            upgrades: item.Upgrades,
            canEditUpgrades: canEditUpgrades,
            availableUpgrades: null,
            countDisabledReason: stacksReason,
            conditionDisabledReason: durabilityReason,
            placementDisabledReason: placementReason,
            upgradesDisabledReason: upgradesReason,
            releaseId: formatId,
            iconKey: item.DisplayName ?? item.TypeKey));

        return new SaveFileSummary(
            path,
            ReleaseName(formatId),
            formatId,
            sourceSha256,
            save.Money,
            canEditMoney,
            inventory: inventory,
            fileSizeBytes: fileSize,
            lastModified: lastModified,
            canEditFaction: canEditFaction,
            canEditUpgrades: canEditUpgrades,
            canEditDurability: canEditDurability,
            canEditPlacement: canEditPlacement,
            canEditStashes: canEditStashes,
            canAddItems: canAddItems,
            canRemoveItems: canRemoveItems,
            crcOk: save.StoredCrc32 == save.ComputedCrc32,
            moneyDisabledReason: moneyReason,
            factionDisabledReason: factionReason,
            upgradesDisabledReason: upgradesReason,
            durabilityDisabledReason: durabilityReason,
            placementDisabledReason: placementReason,
            stashesDisabledReason: stashesReason,
            addItemsDisabledReason: addReason,
            removeItemsDisabledReason: removeReason);
    }

    internal static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    internal static bool IsBackupArtifact(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        return stem.EndsWith("_ORIGINAL", StringComparison.OrdinalIgnoreCase) ||
            stem.EndsWith("_EDITED", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSupportedSaveFile(string path) =>
        Path.GetExtension(path) is { } extension &&
        (extension.Equals(".sav", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".scop", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".scs", StringComparison.OrdinalIgnoreCase));

    internal static string ReleaseName(string releaseId) => releaseId switch
    {
        "stalker-soc" => L.T("Тень Чернобыля"),
        "stalker-soc-ee" => L.T("Тень Чернобыля (Enhanced Edition)"),
        "stalker-cs" => L.T("Чистое Небо"),
        "stalker-cs-ee" => L.T("Чистое Небо (Enhanced Edition)"),
        "stalker-cop" => L.T("Зов Припяти"),
        "stalker-cop-ee" => L.T("Зов Припяти (Enhanced Edition)"),
        "stalker2" => L.T("S.T.A.L.K.E.R. 2: Сердце Чернобыля"),
        _ => releaseId,
    };
}
