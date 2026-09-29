using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AvaloniaViewTestGroup
{
    public const string Name = "Avalonia view construction";
}
