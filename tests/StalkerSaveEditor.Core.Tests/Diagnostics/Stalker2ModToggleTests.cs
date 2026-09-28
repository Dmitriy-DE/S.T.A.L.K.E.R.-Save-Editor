using StalkerSaveEditor.Core.Diagnostics;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

public sealed class Stalker2ModToggleTests
{
    [Fact]
    public void Disable_and_restore_moves_custom_mods_without_deleting_or_copying_content()
    {
        using var fixture = new Stalker2Fixture();
        var mod = Path.Combine(fixture.Paks, "~mods", "sample.pak");
        Directory.CreateDirectory(Path.GetDirectoryName(mod)!);
        File.WriteAllBytes(mod, [4, 2, 7, 1]);

        var disabled = Stalker2ModToggle.Disable(fixture.Root);

        Assert.True(disabled.Changed);
        Assert.Equal(Stalker2ModState.Disabled, Stalker2ModToggle.GetState(fixture.Root));
        Assert.False(Directory.Exists(Path.Combine(fixture.Paks, "~mods")));
        Assert.Equal(new byte[] { 4, 2, 7, 1 }, File.ReadAllBytes(Path.Combine(fixture.Content, "~mods.disabled", "sample.pak")));

        var restored = Stalker2ModToggle.Restore(fixture.Root);

        Assert.True(restored.Changed);
        Assert.Equal(Stalker2ModState.Enabled, Stalker2ModToggle.GetState(fixture.Root));
        Assert.Equal(new byte[] { 4, 2, 7, 1 }, File.ReadAllBytes(mod));
    }

    [Fact]
    public void Disable_refuses_to_overwrite_a_prior_recovery_directory()
    {
        using var fixture = new Stalker2Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Paks, "~mods"));
        Directory.CreateDirectory(Path.Combine(fixture.Content, "~mods.disabled"));
        var existing = Path.Combine(fixture.Paks, "~mods", "keep.pak");
        File.WriteAllText(existing, "keep");

        Assert.Throws<IOException>(() => Stalker2ModToggle.Disable(fixture.Root));
        Assert.True(File.Exists(existing));
        Assert.Equal(Stalker2ModState.Conflict, Stalker2ModToggle.GetState(fixture.Root));
    }

    [Fact]
    public void Restore_refuses_to_replace_a_new_custom_mod_folder()
    {
        using var fixture = new Stalker2Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Content, "~mods.disabled"));
        Directory.CreateDirectory(Path.Combine(fixture.Paks, "~mods"));
        File.WriteAllText(Path.Combine(fixture.Content, "~mods.disabled", "old.pak"), "old");
        File.WriteAllText(Path.Combine(fixture.Paks, "~mods", "new.pak"), "new");

        Assert.Throws<IOException>(() => Stalker2ModToggle.Restore(fixture.Root));
        Assert.True(File.Exists(Path.Combine(fixture.Content, "~mods.disabled", "old.pak")));
        Assert.True(File.Exists(Path.Combine(fixture.Paks, "~mods", "new.pak")));
    }

    private sealed class Stalker2Fixture : IDisposable
    {
        public Stalker2Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "sse-s2-mods-" + Guid.NewGuid().ToString("N"));
            Paks = Path.Combine(Root, "Stalker2", "Content", "Paks");
            Content = Path.GetDirectoryName(Paks)!;
            Directory.CreateDirectory(Paks);
        }

        public string Root { get; }
        public string Paks { get; }
        public string Content { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
