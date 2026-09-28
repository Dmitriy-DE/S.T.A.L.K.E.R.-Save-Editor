using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;
using Xunit;
using Xunit.Abstractions;

namespace StalkerSaveEditor.Core.Tests.Memory;

public sealed class OpenEditCloseCycleTests(ITestOutputHelper output)
{
    private const int CycleCount = 100;

    [Theory]
    [InlineData("writer-money/xray-money-soc-source.sav", "stalker-soc")]
    [InlineData("writer-money/xray-money-soc-ee-source.sav", "stalker-soc-ee")]
    [InlineData("writer-money/xray-money-cs-source.sav", "stalker-cs")]
    [InlineData("writer-money/xray-money-cs-ee-source.sav", "stalker-cs-ee")]
    [InlineData("writer-money/xray-money-cop-source.sav", "stalker-cop")]
    [InlineData("writer-money/xray-money-cop-ee-source.sav", "stalker-cop-ee")]
    [InlineData("writer-s2-money/s2-money-source.sav", "stalker2")]
    public void One_hundred_open_edit_close_cycles_release_save_buffers(string fixture, string expectedRelease)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture.Replace('/', Path.DirectorySeparatorChar));
        for (var index = 0; index < 10; index++)
        {
            _ = OpenEditClose(path, expectedRelease);
        }

        var heapBefore = GC.GetTotalMemory(forceFullCollection: true);
        var retainedSources = RunMeasuredCycles(path, expectedRelease);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var heapAfter = GC.GetTotalMemory(forceFullCollection: true);
        output.WriteLine("{0}: cycles={1}, managed heap after full GC before={2} B after={3} B delta={4} B, retained source buffers={5}",
            expectedRelease, CycleCount, heapBefore, heapAfter, heapAfter - heapBefore, retainedSources);
        Assert.Equal(0, retainedSources);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunMeasuredCycles(string path, string expectedRelease)
    {
        var sourceReferences = new WeakReference[CycleCount];

        for (var index = 0; index < sourceReferences.Length; index++)
        {
            sourceReferences[index] = OpenEditClose(path, expectedRelease);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return sourceReferences.Count(reference => reference.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenEditClose(string path, string expectedRelease)
    {
        byte[] source;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan))
        {
            source = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
            stream.ReadExactly(source);
        }

        var release = EditService.DetectFormat(source)
            ?? throw new InvalidDataException("Synthetic save format was not detected.");
        Assert.Equal(expectedRelease, release);
        var currentMoney = release == "stalker2"
            ? Stalker2SaveReader.FromBytes(source).Money
            : release.EndsWith("-ee", StringComparison.Ordinal)
                ? XRayEnhancedReader.FromBytes(source).Money
                : XRayTrilogyReader.FromBytes(source).Money;
        var nextMoney = currentMoney == uint.MaxValue ? currentMoney - 1 : currentMoney + 1;
        var sourceSha = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        var plan = new EditPlan(sourceSha, money: nextMoney);
        var prepared = EditService.PrepareEdit(source, plan, release);
        EditService.VerifyReadBack(prepared.Data.Span, release, plan);
        return new WeakReference(source);
    }
}
