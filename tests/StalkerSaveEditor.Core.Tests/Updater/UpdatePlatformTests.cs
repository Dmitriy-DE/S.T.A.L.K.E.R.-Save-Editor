using StalkerSaveEditor.Updater;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Updater;

public sealed class UpdatePlatformTests
{
    [Theory]
    [InlineData("windows", "x86_64", "portable", "windows-x86_64")]
    [InlineData("windows", "x86_64", "installer", "windows-installer-x86_64")]
    [InlineData("linux", "x86_64", "portable", "linux-x86_64")]
    [InlineData("linux", "x86_64", "package", "linux-deb-amd64")]
    [InlineData("macos", "arm64", "disk-image", "macos-arm64")]
    [InlineData("macos", "x86_64", "disk-image", "macos-x86_64")]
    public void Every_published_artifact_has_one_entry(string platform, string architecture, string kind, string key)
    {
        Assert.Equal(key, UpdatePlatform.ArtifactKey(platform, architecture, kind));
        Assert.True(UpdatePlatform.Describes(key, architecture, kind));
    }

    [Theory]
    [InlineData("linux", "x86_64", "installer")]
    [InlineData("windows", "x86_64", "package")]
    [InlineData("macos", "x86_64", "portable")]
    [InlineData("macos", "riscv", "disk-image")]
    [InlineData("freebsd", "x86_64", "portable")]
    public void Combinations_no_release_has_are_not_given_an_entry(string platform, string architecture, string kind) =>
        Assert.Null(UpdatePlatform.ArtifactKey(platform, architecture, kind));

    [Theory]
    [InlineData("linux-deb-amd64", "x86_64", "portable")]
    [InlineData("windows-x86_64", "arm64", "portable")]
    [InlineData("macos-arm64", "x86_64", "disk-image")]
    [InlineData("windows-installer-x86_64", "x86_64", "package")]
    [InlineData("solaris-sparc", "x86_64", "portable")]
    public void An_entry_cannot_describe_another_cpu_or_kind(string key, string architecture, string kind) =>
        Assert.False(UpdatePlatform.Describes(key, architecture, kind));

    [Theory]
    [InlineData("macos", "app-bundle", "disk-image")]
    [InlineData("linux", "package", "package")]
    [InlineData("linux", "portable", "portable")]
    [InlineData("windows", "installer", "installer")]
    [InlineData("windows", "portable", "portable")]
    public void An_installation_is_updated_by_the_matching_artifact_kind(string platform, string installed, string artifact) =>
        Assert.Equal(artifact, UpdatePlatform.ArtifactKindFor(platform, installed));
}
