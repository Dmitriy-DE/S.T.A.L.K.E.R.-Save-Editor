using System.Text;
using System.Text.Json;
using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Companion;

public sealed class CompanionInstallerTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "companion-installer");

    private static readonly string ModSourceRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../mods/companion"));

    [Fact]
    public void Steam_discovery_installation_is_idempotent_and_uninstall_restores_archived_bytes()
    {
        using var game = SyntheticGame.Create(CompanionGame.CallOfPripyat);
        var installer = new CompanionInstaller(ModSourceRoot);
        var status = installer.GetStatus(CompanionGame.CallOfPripyat, steamRoots: [game.SteamRoot]);

        Assert.True(status.GameFound);
        Assert.Equal(game.GameDirectory, status.GameDirectory);
        Assert.False(status.ModInstalled);

        var installed = installer.Install(CompanionGame.CallOfPripyat, steamRoots: [game.SteamRoot]);

        Assert.True(installed.Success);
        Assert.True(installed.Changed);
        Assert.Equal("v1", installed.Version);
        AssertHooked(game.GameDirectory);
        AssertModAssetsInstalled(game.GameDirectory, "cop");
        Assert.Empty(Directory.EnumerateFileSystemEntries(game.AppDataDirectory));

        var manifestPath = Path.Combine(game.GameDirectory, ".save-editor-companion", "manifest.json");
        var manifestBytes = File.ReadAllBytes(manifestPath);
        var manifest = JsonDocument.Parse(manifestBytes);
        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("cop", manifest.RootElement.GetProperty("game").GetString());
        Assert.True(manifest.RootElement.GetProperty("files").GetArrayLength() > 3);
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            Assert.Matches("^[0-9a-f]{64}$", file.GetProperty("afterSha256").GetString());
            if (file.GetProperty("beforeSha256").ValueKind != JsonValueKind.Null)
            {
                Assert.Matches("^[0-9a-f]{64}$", file.GetProperty("beforeSha256").GetString());
                Assert.False(string.IsNullOrWhiteSpace(file.GetProperty("backupPath").GetString()));
            }
        }

        var backupCount = Directory.GetFiles(
            Path.Combine(game.GameDirectory, ".save-editor-companion", "backups"),
            "*",
            SearchOption.AllDirectories).Length;
        var repeated = installer.Install(CompanionGame.CallOfPripyat, steamRoots: [game.SteamRoot]);

        Assert.True(repeated.Success);
        Assert.False(repeated.Changed);
        Assert.Equal(manifestBytes, File.ReadAllBytes(manifestPath));
        Assert.Equal(backupCount, Directory.GetFiles(
            Path.Combine(game.GameDirectory, ".save-editor-companion", "backups"),
            "*",
            SearchOption.AllDirectories).Length);

        var removed = installer.Uninstall(CompanionGame.CallOfPripyat, steamRoots: [game.SteamRoot]);

        Assert.True(removed.Success);
        Assert.True(removed.Conflicts.Count == 0, string.Join("\n", removed.Conflicts));
        Assert.False(Directory.Exists(Path.Combine(game.GameDirectory, "gamedata")), string.Join("\n", removed.Conflicts));
        Assert.False(Directory.Exists(Path.Combine(game.GameDirectory, ".save-editor-companion")));

        Assert.Empty(Directory.EnumerateFileSystemEntries(game.AppDataDirectory));
    }

    [Fact]
    public void Root_archive_alias_without_add_path_uses_game_directory()
    {
        using var game = SyntheticGame.Create(CompanionGame.ShadowOfChernobyl);
        var fsgamePath = Path.Combine(game.GameDirectory, "fsgame.ltx");
        var fsgame = File.ReadAllText(fsgamePath).Replace(
            "$arch_dir$ = false| false| $fs_root$|",
            "$arch_dir$ = false| false| $fs_root$",
            StringComparison.Ordinal);
        File.WriteAllText(fsgamePath, fsgame);
        var installer = new CompanionInstaller(ModSourceRoot);

        var result = installer.Install(CompanionGame.ShadowOfChernobyl, game.GameDirectory);

        Assert.True(result.Success);
        AssertHooked(game.GameDirectory);
    }

    [Fact]
    public void Hook_patching_accepts_real_key_namespaces_and_preserves_cp1251_crlf_and_blank_whitespace()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var codePage = Encoding.GetEncoding(1251);
        var originalMenu = codePage.GetBytes(
            "-- синтетическая меню-фикстура\r\n" +
            "function main_menu:OnKeyboard(dik, keyboard_action)\r\n" +
            "\tif keyboard_action == ui_events.WINDOW_KEY_PRESSED then\r\n" +
            "\t\tif dik == DIK_keys.DIK_Q then\r\n" +
            "\t\t\tself:Close()\r\n" +
            "\t\tend\r\n" +
            "\t \t\r\n" +
            "\tend\r\n" +
            "\treturn true\r\n" +
            "end\r\n");
        const string menuHook = "if save_editor_companion_ui then save_editor_companion_ui.on_menu_key(dik, self) end";

        var installedMenu = CompanionHookPatcher.PatchMainMenu(originalMenu);
        var menuText = codePage.GetString(installedMenu);
        Assert.Contains($"\t\tend\r\n\t\t{menuHook}\r\n\t \t\r\n", menuText, StringComparison.Ordinal);
        Assert.Equal(originalMenu, CompanionHookPatcher.RemoveMainMenuHook(installedMenu));

        var originalBind = codePage.GetBytes(
            "-- синтетическая actor-фикстура\r\n" +
            "function actor_binder:update(delta)\r\n" +
            "\tobject_binder.update(self, delta)\r\n" +
            "end\r\n" +
            "\r\n" +
            "function actor_binder:use_inventory_item(obj)\r\n" +
            "\tself:use_inventory_item(obj)\r\n" +
            "end\r\n");
        const string useItemHook = "if save_editor_companion then save_editor_companion.on_use(obj) end";

        var installedBind = CompanionHookPatcher.PatchBindStalker(originalBind);
        var bindText = codePage.GetString(installedBind);
        Assert.Contains($"function actor_binder:use_inventory_item(obj)\r\n\t{useItemHook}\r\n", bindText, StringComparison.Ordinal);
        Assert.Equal(originalBind, CompanionHookPatcher.RemoveBindStalkerHooks(installedBind));
    }

    [Theory]
    [InlineData(CompanionGame.ShadowOfChernobyl, "soc")]
    [InlineData(CompanionGame.ClearSky, "cs")]
    [InlineData(CompanionGame.CallOfPripyat, "cop")]
    public void Each_trilogy_game_uses_its_game_specific_mod_files(CompanionGame gameId, string sourceFolder)
    {
        using var game = SyntheticGame.Create(gameId);
        var installer = new CompanionInstaller(ModSourceRoot);

        var result = installer.Install(gameId, selectedGameDirectory: game.GameDirectory);

        Assert.True(result.Success);
        AssertModAssetsInstalled(game.GameDirectory, sourceFolder);
        AssertHooked(game.GameDirectory);
    }

    [Fact]
    public void Existing_loose_gamedata_file_is_used_and_restored_exactly()
    {
        using var game = SyntheticGame.Create(CompanionGame.CallOfPripyat);
        const string archiveRelative = "scripts/bind_stalker.script";
        var relative = Path.Combine("scripts", "bind_stalker.script");
        var loosePath = Path.Combine(game.GameDirectory, "gamedata", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(loosePath)!);
        var archived = ReadArchiveFiles(game.GameDirectory, "configs.db")[archiveRelative];
        var local = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(archived) + "\n-- loose local override\n");
        File.WriteAllBytes(loosePath, local);
        const string menuArchiveRelative = "scripts/ui_main_menu.script";
        var looseMenuPath = Path.Combine(game.GameDirectory, "gamedata", "scripts", "ui_main_menu.script");
        var archivedMenu = ReadArchiveFiles(game.GameDirectory, "xpatch_02.db")[menuArchiveRelative];
        var localMenu = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(archivedMenu) + "\n-- loose menu override\n");
        File.WriteAllBytes(looseMenuPath, localMenu);
        var installer = new CompanionInstaller(ModSourceRoot);

        var result = installer.Install(CompanionGame.CallOfPripyat, selectedGameDirectory: game.GameDirectory);

        Assert.True(result.Success);
        Assert.Contains("-- loose local override", File.ReadAllText(loosePath), StringComparison.Ordinal);
        Assert.Equal(localMenu, CompanionHookPatcher.RemoveMainMenuHook(File.ReadAllBytes(looseMenuPath)));
        Assert.True(installer.Uninstall(CompanionGame.CallOfPripyat, game.GameDirectory).Success);
        Assert.Equal(local, File.ReadAllBytes(loosePath));
        Assert.Equal(localMenu, File.ReadAllBytes(looseMenuPath));
    }

    [Fact]
    public void Missing_hook_anchor_fails_before_any_installation_write()
    {
        using var game = SyntheticGame.Create(CompanionGame.CallOfPripyat);
        var bindPath = Path.Combine(game.GameDirectory, "gamedata", "scripts", "bind_stalker.script");
        Directory.CreateDirectory(Path.GetDirectoryName(bindPath)!);
        File.WriteAllText(bindPath, "function actor_binder:update(delta)\n\tself:update(delta)\nend\n");
        var installer = new CompanionInstaller(ModSourceRoot);

        var error = Assert.Throws<CompanionInstallerException>(() =>
            installer.Install(CompanionGame.CallOfPripyat, selectedGameDirectory: game.GameDirectory));

        Assert.Contains("bind_stalker.script", error.Message, StringComparison.Ordinal);
        Assert.Contains("anchor", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(game.GameDirectory, "gamedata", "scripts", "save_editor_companion.script")));
        Assert.False(Directory.Exists(Path.Combine(game.GameDirectory, ".save-editor-companion")));
    }

    [Fact]
    public void Uninstall_does_not_overwrite_a_file_changed_after_installation()
    {
        using var game = SyntheticGame.Create(CompanionGame.CallOfPripyat);
        var installer = new CompanionInstaller(ModSourceRoot);
        installer.Install(CompanionGame.CallOfPripyat, selectedGameDirectory: game.GameDirectory);
        var installedScript = Path.Combine(
            game.GameDirectory,
            "gamedata",
            "scripts",
            "save_editor_companion.script");
        File.AppendAllText(installedScript, "\n-- changed by another mod\n");
        var changedBytes = File.ReadAllBytes(installedScript);

        var result = installer.Uninstall(CompanionGame.CallOfPripyat, game.GameDirectory);

        Assert.False(result.Success);
        Assert.Contains(result.Conflicts, conflict => conflict.Contains("save_editor_companion.script", StringComparison.Ordinal));
        Assert.Equal(changedBytes, File.ReadAllBytes(installedScript));
        Assert.True(File.Exists(Path.Combine(game.GameDirectory, ".save-editor-companion", "manifest.json")));
    }

    [Fact]
    public void Recognizes_and_adopts_a_manual_install_before_updating_or_removing_it()
    {
        using var game = SyntheticGame.Create(CompanionGame.CallOfPripyat);
        var installer = new CompanionInstaller(ModSourceRoot);
        installer.Install(CompanionGame.CallOfPripyat, selectedGameDirectory: game.GameDirectory);
        var archived = ReadArchiveFiles(game.GameDirectory, "configs.db");
        var patched = ReadArchiveFiles(game.GameDirectory, "xpatch_02.db");
        AssertModAssetsInstalled(game.GameDirectory, "cop");
        Assert.Equal(
            archived["scripts/bind_stalker.script"],
            CompanionHookPatcher.RemoveBindStalkerHooks(File.ReadAllBytes(Path.Combine(game.GameDirectory, "gamedata", "scripts", "bind_stalker.script"))));
        Assert.Equal(
            patched["scripts/ui_main_menu.script"],
            CompanionHookPatcher.RemoveMainMenuHook(File.ReadAllBytes(Path.Combine(game.GameDirectory, "gamedata", "scripts", "ui_main_menu.script"))));
        Assert.Equal(
            archived["configs/misc/quest_items.ltx"],
            CompanionHookPatcher.RemoveQuestInclude(File.ReadAllBytes(Path.Combine(game.GameDirectory, "gamedata", "configs", "misc", "quest_items.ltx"))));
        Directory.Delete(Path.Combine(game.GameDirectory, ".save-editor-companion"), recursive: true);

        var status = installer.GetStatus(CompanionGame.CallOfPripyat, game.GameDirectory);
        Assert.True(status.ModInstalled, string.Join("\n", status.Issues));

        var adopted = installer.Install(CompanionGame.CallOfPripyat, game.GameDirectory);

        Assert.True(adopted.Success);
        Assert.False(adopted.Changed);
        Assert.True(File.Exists(Path.Combine(game.GameDirectory, ".save-editor-companion", "manifest.json")));
        var removed = installer.Uninstall(CompanionGame.CallOfPripyat, game.GameDirectory);
        Assert.True(removed.Success);
        Assert.False(Directory.Exists(Path.Combine(game.GameDirectory, "gamedata")));
    }

    [Fact]
    public void Call_of_pripyat_uses_patches_after_resources_in_fsgame_order()
    {
        using var game = SyntheticGame.Create(CompanionGame.CallOfPripyat);
        var resources = ReadArchiveFiles(game.GameDirectory, "configs.db");
        var lowerPatch = ReadArchiveFiles(game.GameDirectory, "xpatch_01.db");
        var higherPatch = ReadArchiveFiles(game.GameDirectory, "xpatch_02.db");
        Assert.NotEqual(resources["scripts/ui_main_menu.script"], lowerPatch["scripts/ui_main_menu.script"]);
        Assert.NotEqual(lowerPatch["scripts/ui_main_menu.script"], higherPatch["scripts/ui_main_menu.script"]);
        var installer = new CompanionInstaller(ModSourceRoot);

        installer.Install(CompanionGame.CallOfPripyat, game.GameDirectory);

        var installed = File.ReadAllBytes(Path.Combine(
            game.GameDirectory,
            "gamedata",
            "scripts",
            "ui_main_menu.script"));
        Assert.Equal(higherPatch["scripts/ui_main_menu.script"], CompanionHookPatcher.RemoveMainMenuHook(installed));
    }

    [Fact]
    public void Recursive_fsgame_archive_alias_finds_nested_archive_files()
    {
        using var game = SyntheticGame.Create(CompanionGame.CallOfPripyat);
        var resources = Path.Combine(game.GameDirectory, "resources");
        var nested = Path.Combine(resources, "nested");
        Directory.CreateDirectory(nested);
        File.Move(Path.Combine(resources, "configs.db"), Path.Combine(nested, "configs.db"));
        var fsgamePath = Path.Combine(game.GameDirectory, "fsgame.ltx");
        var fsgame = File.ReadAllText(fsgamePath).Replace(
            "$arch_dir_resources$ = false|",
            "$arch_dir_resources$ = true|",
            StringComparison.Ordinal);
        File.WriteAllText(fsgamePath, fsgame);
        var installer = new CompanionInstaller(ModSourceRoot);

        var result = installer.Install(CompanionGame.CallOfPripyat, game.GameDirectory);

        Assert.True(result.Success);
        AssertHooked(game.GameDirectory);
    }

    [Fact]
    public void Fsgame_without_archive_aliases_is_reported_in_status_and_not_guessed()
    {
        using var game = SyntheticGame.Create(CompanionGame.CallOfPripyat);
        File.WriteAllText(
            Path.Combine(game.GameDirectory, "fsgame.ltx"),
            "$app_data_root$ = true| false| $fs_root$| _appdata_\\\n");
        var installer = new CompanionInstaller(ModSourceRoot);

        var status = installer.GetStatus(CompanionGame.CallOfPripyat, game.GameDirectory);
        var exception = Assert.Throws<CompanionInstallerException>(() =>
            installer.Install(CompanionGame.CallOfPripyat, game.GameDirectory));

        Assert.Contains(status.Issues, issue => issue.Contains("fsgame.ltx", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("archive", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(game.GameDirectory, "gamedata")));
    }

    [Fact]
    public void Corrupt_patch_archive_is_reported_and_install_does_not_fall_back_to_resources()
    {
        using var game = SyntheticGame.Create(CompanionGame.CallOfPripyat);
        var patchPath = Path.Combine(game.GameDirectory, "patches", "xpatch_02.db");
        File.WriteAllBytes(patchPath, [1, 2, 3, 4]);
        var installer = new CompanionInstaller(ModSourceRoot);

        var status = installer.GetStatus(CompanionGame.CallOfPripyat, game.GameDirectory);
        var exception = Assert.Throws<CompanionInstallerException>(() =>
            installer.Install(CompanionGame.CallOfPripyat, game.GameDirectory));

        Assert.Contains(status.Issues, issue => issue.Contains("xpatch_02.db", StringComparison.Ordinal));
        Assert.Equal(patchPath, exception.FilePath);
        Assert.False(Directory.Exists(Path.Combine(game.GameDirectory, "gamedata")));
    }

    [Fact]
    public void Reports_missing_write_permission_without_copying_any_mod_file()
    {
        using var game = SyntheticGame.Create(CompanionGame.CallOfPripyat);
        var installer = new CompanionInstaller(ModSourceRoot, new DenyWritesFileSystem());

        Assert.ThrowsAny<IOException>(() =>
            installer.Install(CompanionGame.CallOfPripyat, game.GameDirectory));

        Assert.False(Directory.Exists(Path.Combine(game.GameDirectory, "gamedata")));
        Assert.False(Directory.Exists(Path.Combine(game.GameDirectory, ".save-editor-companion")));
    }

    private static void AssertHooked(string gameDirectory)
    {
        var bind = File.ReadAllText(Path.Combine(gameDirectory, "gamedata", "scripts", "bind_stalker.script"));
        var menu = File.ReadAllText(Path.Combine(gameDirectory, "gamedata", "scripts", "ui_main_menu.script"));
        var quest = File.ReadAllText(Path.Combine(gameDirectory, "gamedata", "configs", "misc", "quest_items.ltx"));
        Assert.Equal(1, Count(bind, "if save_editor_companion then save_editor_companion.update() end"));
        Assert.Equal(1, Count(bind, "if save_editor_companion then save_editor_companion.on_use(obj) end"));
        Assert.Equal(1, Count(menu, "if save_editor_companion_ui then save_editor_companion_ui.on_menu_key(dik, self) end"));
        Assert.EndsWith("#include \"save_editor_companion.ltx\"\n", quest, StringComparison.Ordinal);
    }

    private static void AssertModAssetsInstalled(string gameDirectory, string sourceFolder)
    {
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(ModSourceRoot, "gamedata"), "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(ModSourceRoot, sourceFolder, "gamedata"), "*", SearchOption.AllDirectories))
            .ToArray();
        Assert.NotEmpty(sourceFiles);
        foreach (var source in sourceFiles)
        {
            var commonRoot = Path.GetFullPath(Path.Combine(ModSourceRoot, "gamedata")) + Path.DirectorySeparatorChar;
            var gameRoot = Path.GetFullPath(Path.Combine(ModSourceRoot, sourceFolder, "gamedata")) + Path.DirectorySeparatorChar;
            var relative = Path.GetRelativePath(source.StartsWith(commonRoot, StringComparison.Ordinal) ? commonRoot : gameRoot, source);
            var target = Path.Combine(gameDirectory, "gamedata", relative);
            Assert.True(File.Exists(target), $"Missing installed asset: {relative}");
            var sourceBytes = File.ReadAllBytes(source);
            var installedBytes = File.ReadAllBytes(target);
            if (Path.GetExtension(source).ToLowerInvariant() is ".script" or ".xml" or ".ltx")
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var text = new UTF8Encoding(false, true).GetString(sourceBytes);
                Assert.Equal(text, Encoding.GetEncoding(1251).GetString(installedBytes));
            }
            else
            {
                Assert.Equal(sourceBytes, installedBytes);
            }
        }
    }

    private static Dictionary<string, byte[]> ReadArchiveFiles(string gameDirectory, string archiveName)
    {
        var path = Directory.EnumerateFiles(gameDirectory, archiveName, SearchOption.AllDirectories).Single();
        using var archive = XRayArchiveReader.Open(new MemoryStream(File.ReadAllBytes(
            path)));
        return archive.Entries.ToDictionary(
            entry => entry.Name.Replace("gamedata/", string.Empty, StringComparison.Ordinal),
            entry => archive.ReadFile(entry.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private sealed class SyntheticGame : IDisposable
    {
        private readonly string _root;

        private SyntheticGame(string root, string gameDirectory, string steamRoot, string appDataDirectory)
        {
            _root = root;
            GameDirectory = gameDirectory;
            SteamRoot = steamRoot;
            AppDataDirectory = appDataDirectory;
        }

        public string GameDirectory { get; }

        public string SteamRoot { get; }

        public string AppDataDirectory { get; }

        public static SyntheticGame Create(CompanionGame game)
        {
            var root = Path.Combine(Path.GetTempPath(), $"companion-installer-{Guid.NewGuid():N}");
            var steamRoot = Path.Combine(root, "steam");
            var steamApps = Path.Combine(steamRoot, "steamapps");
            var gameDirectoryName = game switch
            {
                CompanionGame.ShadowOfChernobyl => "STALKER Shadow of Chernobyl",
                CompanionGame.ClearSky => "STALKER Clear Sky",
                CompanionGame.CallOfPripyat => "Stalker Call of Pripyat",
                _ => throw new ArgumentOutOfRangeException(nameof(game)),
            };
            var gameDirectory = Path.Combine(steamApps, "common", gameDirectoryName);
            var appData = Path.Combine(gameDirectory, "_appdata_");
            Directory.CreateDirectory(Path.Combine(steamApps, "common"));
            Directory.CreateDirectory(appData);
            File.WriteAllText(Path.Combine(steamApps, "libraryfolders.vdf"),
                $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{steamRoot.Replace("\\", "\\\\", StringComparison.Ordinal)}\" }} }}\n");
            var appId = game switch
            {
                CompanionGame.ShadowOfChernobyl => 4500,
                CompanionGame.ClearSky => 20_510,
                CompanionGame.CallOfPripyat => 41_700,
                _ => throw new ArgumentOutOfRangeException(nameof(game)),
            };
            File.WriteAllText(Path.Combine(steamApps, $"appmanifest_{appId}.acf"),
                $"\"AppState\" {{ \"appid\" \"{appId}\" \"installdir\" \"{gameDirectoryName}\" }}\n");
            var resourceDirectory = Path.Combine(gameDirectory, "resources");
            Directory.CreateDirectory(resourceDirectory);
            var archiveAliases = game == CompanionGame.ShadowOfChernobyl
                ? "$arch_dir$ = false| false| $fs_root$|\n"
                : "$arch_dir_resources$ = false| false| $fs_root$| resources\\\n";
            if (game != CompanionGame.ShadowOfChernobyl)
            {
                Directory.CreateDirectory(Path.Combine(gameDirectory, "patches"));
                var patchAlias = game == CompanionGame.CallOfPripyat ? "$game_arch_mp$" : "$arch_dir_patches$";
                archiveAliases += $"{patchAlias} = false| true| $fs_root$| patches\\\n";
            }
            File.WriteAllText(
                Path.Combine(gameDirectory, "fsgame.ltx"),
                "$app_data_root$ = true| false| $fs_root$| _appdata_\\\n" + archiveAliases);

            using var fixtureManifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureDirectory, "manifest.json")));
            var gameKey = game switch
            {
                CompanionGame.ShadowOfChernobyl => "soc",
                CompanionGame.ClearSky => "cs",
                CompanionGame.CallOfPripyat => "cop",
                _ => throw new ArgumentOutOfRangeException(nameof(game)),
            };
            var archiveName = fixtureManifest.RootElement.GetProperty("games").GetProperty(gameKey)
                .GetProperty("archive").GetString()!;
            var archiveDestination = game switch
            {
                CompanionGame.ShadowOfChernobyl => Path.Combine(gameDirectory, "gamedata.db0"),
                CompanionGame.ClearSky => Path.Combine(resourceDirectory, "cs.db"),
                CompanionGame.CallOfPripyat => Path.Combine(resourceDirectory, "configs.db"),
                _ => throw new ArgumentOutOfRangeException(nameof(game)),
            };
            File.Copy(Path.Combine(FixtureDirectory, archiveName), archiveDestination);
            if (game == CompanionGame.CallOfPripyat)
            {
                var patchArchiveName = fixtureManifest.RootElement.GetProperty("games").GetProperty("cop")
                    .GetProperty("patchArchive02").GetString()!;
                var lowerPatchArchiveName = fixtureManifest.RootElement.GetProperty("games").GetProperty("cop")
                    .GetProperty("patchArchive01").GetString()!;
                File.Copy(
                    Path.Combine(FixtureDirectory, lowerPatchArchiveName),
                    Path.Combine(gameDirectory, "patches", "xpatch_01.db"));
                File.Copy(
                    Path.Combine(FixtureDirectory, patchArchiveName),
                    Path.Combine(gameDirectory, "patches", "xpatch_02.db"));
            }
            return new SyntheticGame(root, gameDirectory, steamRoot, appData);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class DenyWritesFileSystem : ICompanionInstallFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option) =>
            Directory.EnumerateFiles(path, pattern, option);

        public IEnumerable<string> EnumerateDirectories(string path, string pattern, SearchOption option) =>
            Directory.EnumerateDirectories(path, pattern, option);

        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

        public string ReadAllText(string path) => File.ReadAllText(path);

        public Stream OpenRead(string path) => File.OpenRead(path);

        public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

        public void CreateDirectory(string path) => throw new UnauthorizedAccessException("Synthetic permission denial.");

        public void WriteAllBytes(string path, byte[] bytes) => throw new UnauthorizedAccessException("Synthetic permission denial.");

        public void Move(string source, string destination, bool overwrite) => throw new UnauthorizedAccessException("Synthetic permission denial.");

        public void DeleteFile(string path) => throw new UnauthorizedAccessException("Synthetic permission denial.");

        public void DeleteDirectory(string path, bool recursive) => throw new UnauthorizedAccessException("Synthetic permission denial.");
    }
}
