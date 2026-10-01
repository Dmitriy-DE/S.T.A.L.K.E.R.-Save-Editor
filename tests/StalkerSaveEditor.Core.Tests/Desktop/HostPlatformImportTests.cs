using StalkerSaveEditor.Desktop.Services;
using Xunit;

namespace StalkerSaveEditor.Core.Tests.Desktop;

public sealed class HostPlatformImportTests
{
    [Fact]
    public async Task Two_imports_with_the_same_name_do_not_overwrite_each_other()
    {
        var first = await HostPlatform.ImportAsync(new MemoryStream([1, 1, 1]), "A/quicksave.sav");
        var second = await HostPlatform.ImportAsync(new MemoryStream([2, 2]), "quicksave.sav");
        try
        {
            Assert.NotEqual(first, second);
            Assert.Equal("quicksave.sav", Path.GetFileName(first));
            Assert.Equal("quicksave.sav", Path.GetFileName(second));
            Assert.Equal(new byte[] { 1, 1, 1 }, File.ReadAllBytes(first));
            Assert.Equal(new byte[] { 2, 2 }, File.ReadAllBytes(second));
            Assert.StartsWith(HostPlatform.OpenedSavesDirectory, first, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(first)!, recursive: true);
            Directory.Delete(Path.GetDirectoryName(second)!, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../../")]
    public async Task An_empty_or_traversal_name_becomes_a_safe_file_inside_the_import_folder(string name)
    {
        var path = await HostPlatform.ImportAsync(new MemoryStream([7]), name);
        try
        {
            Assert.Equal("save.sav", Path.GetFileName(path));
            Assert.StartsWith(HostPlatform.OpenedSavesDirectory, Path.GetFullPath(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task An_import_over_the_budget_is_refused_and_leaves_nothing_behind()
    {
        Directory.CreateDirectory(HostPlatform.OpenedSavesDirectory);
        var before = Directory.GetDirectories(HostPlatform.OpenedSavesDirectory).Length;

        await Assert.ThrowsAsync<IOException>(() => HostPlatform.ImportAsync(new EndlessStream(), "big.sav", maximumBytes: 1024 * 1024));

        Assert.Equal(before, Directory.GetDirectories(HostPlatform.OpenedSavesDirectory).Length);
    }

    /// <summary>Never ends: the bounded copy must stop on its own instead of materialising the stream.</summary>
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => count;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
