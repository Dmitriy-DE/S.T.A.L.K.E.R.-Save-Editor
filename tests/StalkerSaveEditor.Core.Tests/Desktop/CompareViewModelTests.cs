using StalkerSaveEditor.Desktop.ViewModels;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class CompareViewModelTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "writer-money", name);

    [Fact]
    public void Shows_the_money_change_between_a_save_and_its_edited_copy()
    {
        var vm = new CompareViewModel(_ => null);
        vm.SetSubject(Fixture("xray-money-cop-expected.sav"), "stalker-cop",
        [
            new CompareCandidate("before", Fixture("xray-money-cop-source.sav")),
            new CompareCandidate("self", Fixture("xray-money-cop-expected.sav")),
        ]);

        Assert.Single(vm.Candidates);
        vm.Selected = vm.Candidates[0];

        var money = Assert.Single(vm.Rows, row => row.Label == "Деньги");
        Assert.NotEqual(money.Before, money.After);
    }

    [Fact]
    public void Reports_an_unreadable_file_instead_of_throwing()
    {
        var vm = new CompareViewModel(_ => null);
        vm.SetSubject(Fixture("xray-money-cop-source.sav"), "stalker-cop", [new CompareCandidate("gone", Fixture("missing.sav"))]);

        vm.Selected = vm.Candidates[0];

        Assert.Empty(vm.Rows);
        Assert.StartsWith("Не удалось прочитать", vm.Status, StringComparison.Ordinal);
    }
}
