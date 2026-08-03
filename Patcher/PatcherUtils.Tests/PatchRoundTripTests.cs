using System.Security.Cryptography;
using System.Text;
using PatchClient.Models;
using PatcherUtils.Model;
using Xunit;

namespace PatcherUtils.Tests;

/// <summary>
/// End to end coverage for <see cref="PatchHelper"/>: generate a patch set from a source and target
/// tree, apply it to a copy of the source, and require the result to be byte identical to the target.
/// </summary>
public class PatchRoundTripTests : IDisposable
{
    private readonly string _root;
    private readonly string _originalCurrentDirectory;

    public PatchRoundTripTests()
    {
        _originalCurrentDirectory = Directory.GetCurrentDirectory();
        _root = Path.Combine(Path.GetTempPath(), "PatcherUtils.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        // PatchHelper resolves its temp/resource directory from the working directory.
        Directory.SetCurrentDirectory(_root);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_originalCurrentDirectory);

        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // A locked handle here should not fail an otherwise passing test run.
        }
    }

    [Fact]
    public void GenerateThenApply_ReproducesTargetTreeExactly()
    {
        string source = Path.Combine(_root, "source");
        string target = Path.Combine(_root, "target");
        string delta = Path.Combine(_root, "delta");
        string install = Path.Combine(_root, "install");

        byte[] large = Random(3_000_000, seed: 1);

        // Unchanged on both sides.
        Write(source, "same.dat", Random(50_000, seed: 2));
        Write(target, "same.dat", Random(50_000, seed: 2));

        // Changed, nested.
        Write(source, "Data/Assembly-CSharp.dll", large);
        Write(target, "Data/Assembly-CSharp.dll", Mutate(large, seed: 3));

        // Changed, and named so that naive ".delta" string replacement corrupts the path.
        Write(source, "a.b.c/nested.delta.bin", Random(80_000, seed: 4));
        Write(target, "a.b.c/nested.delta.bin", Random(80_000, seed: 5));

        // Present only in the source: must be deleted.
        Write(source, "Data/obsolete.dat", Random(20_000, seed: 6));

        // Present only in the target: must be added.
        Write(target, "Data/brandnew.dat", Random(30_000, seed: 7));

        PatchMessage generated = new PatchHelper(source, target, delta).GeneratePatches();
        Assert.Equal(PatcherExitCode.Success, generated.ExitCode);

        CopyTree(source, install);

        PatchMessage applied = new PatchHelper(install, null, delta).ApplyPatches();
        Assert.Equal(PatcherExitCode.Success, applied.ExitCode);

        Assert.Equal(HashTree(target), HashTree(install));
    }

    [Fact]
    public void ApplyPatches_ReportsFailureWhenAPatchHasNoMatchingSourceFile()
    {
        string install = Path.Combine(_root, "install");
        string delta = Path.Combine(_root, "delta");

        Write(install, "present.dat", Random(1000, seed: 8));
        Write(delta, "absent.dat.delta", Random(500, seed: 9));

        PatchMessage applied = new PatchHelper(install, null, delta).ApplyPatches();

        Assert.NotEqual(PatcherExitCode.Success, applied.ExitCode);
    }

    private static void Write(string root, string relativePath, byte[] contents)
    {
        string full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, contents);
    }

    private static byte[] Random(int length, int seed)
    {
        byte[] buffer = new byte[length];
        new Random(seed).NextBytes(buffer);
        return buffer;
    }

    /// <summary>Flip a scattered 5% of bytes, so the file still deltas well but is not identical.</summary>
    private static byte[] Mutate(byte[] source, int seed)
    {
        byte[] copy = (byte[])source.Clone();
        Random random = new(seed);

        for (int i = 0; i < copy.Length / 20; i++)
        {
            copy[random.Next(copy.Length)] ^= 0xA5;
        }

        return copy;
    }

    private static void CopyTree(string from, string to)
    {
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    private static string HashTree(string root)
    {
        StringBuilder builder = new();

        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            using FileStream stream = File.OpenRead(file);

            builder.Append(Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Append('=')
                .Append(Convert.ToHexString(SHA256.HashData(stream)))
                .Append('\n');
        }

        return builder.ToString();
    }
}
