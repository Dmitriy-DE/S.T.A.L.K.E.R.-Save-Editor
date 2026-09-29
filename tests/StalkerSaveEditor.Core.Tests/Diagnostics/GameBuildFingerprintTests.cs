using StalkerSaveEditor.Core.Companion;
using StalkerSaveEditor.Core.Diagnostics;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Diagnostics;

public sealed class GameBuildFingerprintTests
{
    [Fact]
    public void Known_unknown_and_missing_builds_are_told_apart()
    {
        Assert.Equal(GameBuildStatus.Verified, GameBuildFingerprints.Classify(GameTarget.ClearSky, "11450472").Status);
        Assert.Equal(GameBuildStatus.Unknown, GameBuildFingerprints.Classify(GameTarget.ClearSky, "99999999").Status);
        Assert.Equal(GameBuildStatus.NotInstalled, GameBuildFingerprints.Classify(GameTarget.ClearSky, null).Status);
        Assert.Equal(GameBuildStatus.Unknown, GameBuildFingerprints.Classify(GameTarget.Stalker2, "123").Status);
    }

    [Fact]
    public void Detects_the_build_of_a_discovered_install()
    {
        var installs = new[] { new GameDoctorInstallation(GameTarget.CallOfPripyat, "/games/cop", GameInstallSource.Steam, "11450453") };

        Assert.Equal(GameBuildStatus.Verified, GameBuildFingerprints.Detect(GameTarget.CallOfPripyat, installs).Status);
        Assert.Equal(GameBuildStatus.NotInstalled, GameBuildFingerprints.Detect(GameTarget.ClearSky, installs).Status);
        Assert.Equal(GameTarget.CallOfPripyatEnhancedEdition, GameBuildFingerprints.TargetForFormat("stalker-cop-ee"));
    }
}
