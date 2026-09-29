using Xunit;
using StalkerSaveEditor.Desktop.ViewModels;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class RelayCommandTests
{
    [Fact]
    public async Task Async_lambda_is_owned_by_the_command_blocks_reentry_and_does_not_throw()
    {
        var gate = new TaskCompletionSource();
        var runs = 0;
        var command = new RelayCommand(async () =>
        {
            runs++;
            await gate.Task;
            throw new InvalidOperationException("after await");
        });

        var first = command.RunAsync();
        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));
        command.Execute(null);
        gate.SetResult();
        await first;

        Assert.Equal(1, runs);
        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void Generic_command_is_disabled_for_a_parameter_of_another_type()
    {
        var command = new RelayCommand<string>(_ => { });

        Assert.False(command.CanExecute(42));
        Assert.True(command.CanExecute("x"));
    }
}
