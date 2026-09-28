using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using StalkerSaveEditor.Core.Catalogs;
using StalkerSaveEditor.Core.Editing;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;

namespace StalkerSaveEditor.Benchmarks;

[MemoryDiagnoser]
public class HotPathBenchmarks
{
    private byte[] _soc = [];
    private byte[] _stalker2 = [];
    private byte[] _archive = [];
    private string _archiveEntry = string.Empty;
    private EditPlan _socMoneyPlan = null!;
    private EditPlan _stalker2MoneyPlan = null!;

    [GlobalSetup]
    public void LoadSyntheticFixtures()
    {
        _soc = ReadFixture("xray-soc.sav");
        _stalker2 = ReadFixture("synthetic-s2.sav");
        _archive = ReadFixture("xray-archive/synthetic.db");
        _archiveEntry = "gamedata/config/items.ltx";
        _socMoneyPlan = new EditPlan(Sha256(_soc), money: 321);
        _stalker2MoneyPlan = new EditPlan(Sha256(_stalker2), money: 321);
    }

    [Benchmark]
    public PreparedEdit WriteSocMoney() => XRayMoneyWriter.Prepare(_soc, _socMoneyPlan);

    [Benchmark]
    public PreparedEdit WriteStalker2Money() => Stalker2MoneyWriter.Prepare(_stalker2, _stalker2MoneyPlan);

    [Benchmark]
    public PreparedEdit WriteStalker2MoneyThroughEditWriter() =>
        Stalker2EditWriter.Prepare(_stalker2, _stalker2MoneyPlan);

    [Benchmark]
    public IReadOnlyDictionary<string, CatalogBundle> LoadEmbeddedCatalogs() => CatalogBundleReader.LoadEmbedded();

    [Benchmark]
    public byte[] OpenAndReadXRayArchiveFile()
    {
        using var archive = XRayArchiveReader.Open(new MemoryStream(_archive, writable: false));
        return archive.ReadFile(_archiveEntry);
    }

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
