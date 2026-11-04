using System;
using System.IO;
using System.Text;
using HonestFlow.Infrastructure;
using Xunit;

namespace HonestFlow.Tests;

public sealed class LogFileTailReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "hf-log-tail-" + Guid.NewGuid() + ".log");

    [Fact]
    public void UnchangedFile_DoesNotRebuildItsText_AndAppendKeepsExistingLines()
    {
        File.WriteAllText(_path, "first\n", new UTF8Encoding(true));
        var reader = new LogFileTailReader();
        Assert.Equal("first\n", reader.ReadChanges(_path));
        Assert.Null(reader.ReadChanges(_path));
        File.AppendAllText(_path, "вторая строка\n");
        Assert.Equal("first\nвторая строка\n", reader.ReadChanges(_path));
        Assert.Equal("first\nвторая строка\n", reader.ReadChanges(_path, force: true));
    }

    [Fact]
    public void LargeFile_UsesBoundedAllocation_AndRetainsNewestRecords()
    {
        using (var stream = File.Create(_path))
        {
            byte[] block = Encoding.UTF8.GetBytes(new string('x', 8191) + "\n");
            for (int i = 0; i < 2048; i++) stream.Write(block);
            stream.Write(Encoding.UTF8.GetBytes("newest record\n"));
        }
        var reader = new LogFileTailReader();
        long before = GC.GetAllocatedBytesForCurrentThread();
        string text = reader.ReadChanges(_path);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.EndsWith("newest record\n", text);
        Assert.True(text.Length < LogFileTailReader.DefaultMaxCharacters + 200);
        Assert.True(allocated < 4 * 1024 * 1024, $"Allocated {allocated} bytes for a 16 MiB file.");

        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) Assert.Null(reader.ReadChanges(_path));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 512 * 1024);
    }

    [Fact]
    public void GrowingFile_RemainsBoundedAcrossManyRefreshes()
    {
        File.WriteAllText(_path, "oldest\n");
        var reader = new LogFileTailReader(2048);
        reader.ReadChanges(_path);
        for (int i = 0; i < 100; i++)
        {
            File.AppendAllText(_path, new string('a', 1000) + $"record-{i}\n");
            string text = reader.ReadChanges(_path);
            Assert.EndsWith($"record-{i}\n", text);
            Assert.True(text.Length < 2248);
        }
        Assert.DoesNotContain("oldest", reader.ReadChanges(_path, force: true));
    }

    [Fact]
    public void Utf8SequenceSplitBetweenRefreshes_IsDecodedWithoutReplacementCharacters()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("А🙂Б");
        File.WriteAllBytes(_path, bytes[..3]);
        var reader = new LogFileTailReader();
        Assert.Equal("А", reader.ReadChanges(_path));
        using (var stream = new FileStream(_path, FileMode.Append, FileAccess.Write)) stream.Write(bytes[3..]);
        Assert.Equal("А🙂Б", reader.ReadChanges(_path));
    }

    [Fact]
    public void TruncationAndSameLengthReplacement_ResetPreviousText()
    {
        File.WriteAllText(_path, "old content");
        var reader = new LogFileTailReader();
        reader.ReadChanges(_path);
        File.WriteAllText(_path, "short");
        Assert.Equal("short", reader.ReadChanges(_path));
        File.WriteAllText(_path, "other");
        File.SetLastWriteTimeUtc(_path, File.GetLastWriteTimeUtc(_path).AddSeconds(1));
        Assert.Equal("other", reader.ReadChanges(_path));
        reader.Reset();
        Assert.Equal("other", reader.ReadChanges(_path));
    }

    public void Dispose() => File.Delete(_path);
}
