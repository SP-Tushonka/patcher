using PatcherUtils.Model;
using Xunit;

namespace PatcherUtils.Tests;

public class PatchLoggerTests
{
    [Fact]
    public void LogInfo_KeepsEveryLineWhenWrittenFromManyThreads()
    {
        const int threads = 8;
        const int perThread = 250;

        // Unique per run; the log file is appended to and shared with the other tests.
        string token = Guid.NewGuid().ToString("N");

        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, thread =>
        {
            for (int i = 0; i < perThread; i++)
            {
                PatchLogger.LogInfo($"{token} thread {thread} line {i}");
            }
        });

        // Opened sharing-friendly: the logger holds the file open for the process lifetime.
        using FileStream stream = new(PatchLogger.LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new(stream);

        int matched = 0;

        while (reader.ReadLine() is { } line)
        {
            if (line.Contains(token))
            {
                matched++;
            }
        }

        Assert.Equal(threads * perThread, matched);
    }
}
