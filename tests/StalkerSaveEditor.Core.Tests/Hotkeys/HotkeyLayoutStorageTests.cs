using StalkerSaveEditor.Core.Hotkeys;
using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Hotkeys;

public sealed class HotkeyLayoutStorageTests
{
    [Fact]
    public void A_saved_layout_loads_back_and_a_missing_or_broken_file_gives_the_default()
    {
        var directory = Directory.CreateTempSubdirectory("hotkeys-");
        try
        {
            var path = Path.Combine(directory.FullName, "hotkeys.txt");
            Assert.Same(HotkeyLayout.Default, HotkeyLayout.Load(path));

            var custom = HotkeyLayout.Parse("heal=Alt+Q\nrepair_equipped=Ctrl+Shift+R\nmark=Ctrl+M\njump_last=Ctrl+J\nquicksave=Ctrl+K\n");
            custom.Save(path);
            var loaded = HotkeyLayout.Load(path);
            Assert.Equal(custom.ToText(), loaded.ToText());
            Assert.Equal("Alt+Q", loaded.Bindings.Single(b => b.Action == CompanionHotkeyAction.Heal).Gesture.ToString());

            File.WriteAllText(path, "heal=Ctrl+H\nmark=Ctrl+H\n");
            Assert.Same(HotkeyLayout.Default, HotkeyLayout.Load(path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Default_layout_text_round_trips()
    {
        Assert.Equal(HotkeyLayout.Default.ToText(), HotkeyLayout.Parse(HotkeyLayout.Default.ToText()).ToText());
        Assert.Equal("quicksave", HotkeyLayout.ActionName(CompanionHotkeyAction.QuickSave));
    }

    [Fact]
    public async Task Duplicate_keys_are_rejected_on_the_companion_screen()
    {
        var viewModel = new CompanionViewModel(new StalkerSaveEditor.Desktop.Services.MockCompanionService());
        viewModel.Hotkeys.Clear();
        viewModel.Hotkeys.Add(new CompanionHotkeyItemViewModel("heal", "Ctrl+H", "heal"));
        viewModel.Hotkeys.Add(new CompanionHotkeyItemViewModel("mark", "Ctrl+H", "mark"));

        await viewModel.SaveHotkeysAsync();

        Assert.Contains("Ctrl+H", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", viewModel.StatusMessage, StringComparison.Ordinal);
    }
}
