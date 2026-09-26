using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using StalkerSaveEditor.Core.Codecs;
using StalkerSaveEditor.Core.Formats.Enhanced;
using StalkerSaveEditor.Core.Formats.Stalker2;
using StalkerSaveEditor.Core.Formats.XRay;

namespace StalkerSaveEditor.Benchmarks;

[MemoryDiagnoser]
public class SaveFormatBenchmarks
{
    private byte[] _lzoStream = [];
    private int _lzoUnpackedSize;
    private byte[] _krakenStream = [];
    private int _krakenUnpackedSize;
    private byte[] _soc = [];
    private byte[] _clearSky = [];
    private byte[] _callOfPripyat = [];
    private byte[] _socEnhanced = [];
    private byte[] _clearSkyEnhanced = [];
    private byte[] _callOfPripyatEnhanced = [];
    private byte[] _stalker2 = [];

    [GlobalSetup]
    public void LoadSyntheticFixtures()
    {
        _soc = ReadFixture("xray-soc.sav");
        _clearSky = ReadFixture("xray-clear-sky.sav");
        _callOfPripyat = ReadFixture("xray-call-of-pripyat.sav");
        _socEnhanced = ReadFixture("xray-soc-ee.sav");
        _clearSkyEnhanced = ReadFixture("xray-clear-sky-ee.sav");
        _callOfPripyatEnhanced = ReadFixture("xray-call-of-pripyat-ee.sav");
        _stalker2 = ReadFixture("synthetic-s2.sav");

        var socContainer = XRayContainer.FromBytes(_soc);
        _lzoStream = _soc.AsSpan(12).ToArray();
        _lzoUnpackedSize = socContainer.UnpackedSize;
        _krakenStream = _stalker2.AsSpan(sizeof(uint), _stalker2.Length - 2 * sizeof(uint)).ToArray();
        _krakenUnpackedSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(_stalker2));

        _ = KrakenCodec.Decompress(_krakenStream, _krakenUnpackedSize);
    }

    [Benchmark]
    public byte[] LzoDecompress() => Lzo1xCodec.Decompress(_lzoStream, _lzoUnpackedSize);

    [Benchmark]
    public byte[] KrakenDecompress() => KrakenCodec.Decompress(_krakenStream, _krakenUnpackedSize);

    [Benchmark]
    public XRayTrilogySave ParseSoc() => XRayTrilogyReader.FromBytes(_soc);

    [Benchmark]
    public XRayTrilogySave ParseClearSky() => XRayTrilogyReader.FromBytes(_clearSky);

    [Benchmark]
    public XRayTrilogySave ParseCallOfPripyat() => XRayTrilogyReader.FromBytes(_callOfPripyat);

    [Benchmark]
    public XRayTrilogySave ParseSocEnhanced() => XRayEnhancedReader.FromBytes(_socEnhanced);

    [Benchmark]
    public XRayTrilogySave ParseClearSkyEnhanced() => XRayEnhancedReader.FromBytes(_clearSkyEnhanced);

    [Benchmark]
    public XRayTrilogySave ParseCallOfPripyatEnhanced() => XRayEnhancedReader.FromBytes(_callOfPripyatEnhanced);

    [Benchmark]
    public Stalker2Save ParseStalker2() => Stalker2SaveReader.FromBytes(_stalker2);

    private static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
