using PatchClient.Models;
using PatcherUtils.Model;
using SharpHDiffPatch.Core;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PatcherUtils
{
    public class PatchHelper
    {
        private const string ConsistencyInfoFileName = "ConsistencyInfo";

        private string SourceFolder = "";
        private string TargetFolder = "";
        private string DeltaFolder = "";

        private int fileCountTotal;
        private int filesProcessed;

        private int deltaCount;
        private int newCount;
        private int delCount;
        private int existCount;

        private bool debugOutput;

        private int creationFailureCount;
        private readonly ConcurrentQueue<string> creationFailures = new ConcurrentQueue<string>();

        /// <summary>
        /// Reports patch creation or application progress
        /// </summary>
        /// <remarks>Includes an array of <see cref="LineItem"/> with details for each type of patch</remarks>
        public event ProgressChangedHandler ProgressChanged;

        protected virtual void RaiseProgressChanged(int progress, int total, string Message = "",
            params LineItem[] AdditionalLineItems)
        {
            int percent = total == 0 ? 100 : (int)Math.Floor((double)progress / total * 100);

            ProgressChanged?.Invoke(this, progress, total, percent, Message, AdditionalLineItems);
        }

        // Fresh instances each call: the handler marshals these to the UI thread, so patching
        // threads must not still be mutating them.
        private LineItem[] SnapshotGenerationInfo() => new[]
        {
            new LineItem("Delta Patch", Volatile.Read(ref deltaCount)),
            new LineItem("New Patch", Volatile.Read(ref newCount)),
            new LineItem("Del Patch", Volatile.Read(ref delCount)),
            new LineItem("File Exists", Volatile.Read(ref existCount))
        };

        private LineItem[] SnapshotApplyInfo() => new[]
        {
            new LineItem("Patches Remaining", Volatile.Read(ref deltaCount)),
            new LineItem("New Files to Add", Volatile.Read(ref newCount)),
            new LineItem("Files to Delete", Volatile.Read(ref delCount))
        };

        /// <summary>
        /// A helper class to create and apply patches to folders
        /// </summary>
        /// <param name="SourceFolder">The directory that will have patches applied to it.</param>
        /// <param name="TargetFolder">The directory to compare against during patch creation.</param>
        /// <param name="DeltaFolder">The directory where the patches are/will be located.</param>
        /// <remarks><paramref name="TargetFolder"/> can be null if you only plan to apply patches.</remarks>
        public PatchHelper(string SourceFolder, string TargetFolder, string DeltaFolder, bool debugOutput = false)
        {
            // Must match DirectoryInfo.FullName, which expands 8.3 names and relative segments.
            this.SourceFolder = SourceFolder == null ? null : Path.GetFullPath(SourceFolder);
            this.TargetFolder = TargetFolder == null ? null : Path.GetFullPath(TargetFolder);
            this.DeltaFolder = DeltaFolder == null ? null : Path.GetFullPath(DeltaFolder);
            this.debugOutput = debugOutput;
        }

        /// <summary>
        /// Get the delta folder file path. 
        /// </summary>
        /// <param name="SourceFilePath"></param>
        /// <param name="SourceFolderPath"></param>
        /// <param name="FileExtension">The extension to append to the file</param>
        /// <returns>A file path inside the delta folder</returns>
        private string GetDeltaPath(string SourceFilePath, string SourceFolderPath, string FileExtension)
        {
            return Path.Join(DeltaFolder,
                $"{Path.GetRelativePath(SourceFolderPath, SourceFilePath)}.{FileExtension}");
        }

        /// <summary>
        /// Check if two files hold identical bytes
        /// </summary>
        /// <param name="SourceFilePath"></param>
        /// <param name="TargetFilePath"></param>
        /// <returns>True if the contents match</returns>
        private bool CompareFileContents(string SourceFilePath, string TargetFilePath)
        {
            var sourceInfo = new FileInfo(SourceFilePath);
            var targetInfo = new FileInfo(TargetFilePath);

            // Return false if file size differs
            if (sourceInfo.Length != targetInfo.Length)
            {
                return false;
            }

            bool matched = ContentsEqual(SourceFilePath, TargetFilePath);

            PatchLogger.LogInfo(
                $"Content Check: S({sourceInfo.Name}) - T({targetInfo.Name}) - Match:{matched}");

            return matched;
        }

        // Comparing directly beats hashing here: it stops at the first difference, and an exact
        // answer removes any chance of a collision marking a changed file as unchanged.
        private static bool ContentsEqual(string SourceFilePath, string TargetFilePath)
        {
            const int BufferSize = 128 * 1024;

            using var sourceStream = File.OpenRead(SourceFilePath);
            using var targetStream = File.OpenRead(TargetFilePath);

            byte[] sourceBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            byte[] targetBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);

            try
            {
                while (true)
                {
                    int read = sourceStream.ReadAtLeast(sourceBuffer, BufferSize, throwOnEndOfStream: false);

                    if (read == 0)
                    {
                        return true;
                    }

                    targetStream.ReadExactly(targetBuffer, 0, read);

                    if (!sourceBuffer.AsSpan(0, read).SequenceEqual(targetBuffer.AsSpan(0, read)))
                    {
                        return false;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sourceBuffer);
                ArrayPool<byte>.Shared.Return(targetBuffer);
            }
        }

        /// <summary>
        /// Apply a delta to a file using xdelta
        /// </summary>
        /// <param name="SourceFilePath"></param>
        /// <param name="DeltaFilePath"></param>
        private (bool, string) ApplyDelta(string SourceFilePath, string DeltaFilePath)
        {
            string decodedPath = SourceFilePath + ".decoded";

            try
            {
                HDiffPatch patcher = new HDiffPatch();
                HDiffPatch.LogVerbosity = Verbosity.Quiet;

                patcher.Initialize(DeltaFilePath);
                patcher.Patch(SourceFilePath, decodedPath, false, default, false, false);
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
                return (false, ex.Message);
            }

            try
            {
                File.Move(decodedPath, SourceFilePath, true);
                PatchLogger.LogInfo($"Delta applied: {DeltaFilePath}");
                return (true, "");
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Create a .delta file using xdelta
        /// </summary>
        /// <param name="SourceFilePath"></param>
        /// <param name="TargetFilePath"></param>
        /// <remarks>Used to patch an existing file with xdelta</remarks>
        private void CreateDelta(string SourceFilePath, string TargetFilePath)
        {
            string deltaPath = GetDeltaPath(SourceFilePath, SourceFolder, "delta");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(deltaPath));
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
                RecordCreationFailure($"[DELTA] {deltaPath}: {ex.Message}");
                return;
            }

            // The parameters we use here are important to allow patch application using the SharpHDiffPatch.Core library, please don't change them
            using Process hdiff = Process.Start(new ProcessStartInfo
            {
                FileName = LazyOperations.HDiffPath,
                Arguments = $"-s-64 -c-zstd-21-24 -d \"{SourceFilePath}\" \"{TargetFilePath}\" \"{deltaPath}\"",
                CreateNoWindow = true
            });

            hdiff.WaitForExit();

            if (hdiff.ExitCode != 0)
            {
                PatchLogger.LogError($"hdiffz exited {hdiff.ExitCode} for: {SourceFilePath}");
                RecordCreationFailure($"[DELTA] hdiffz exited {hdiff.ExitCode}: {SourceFilePath}");
                return;
            }

            if (File.Exists(deltaPath))
            {
                PatchLogger.LogInfo($"File created [DELTA]: {deltaPath}");
            }
            else
            {
                PatchLogger.LogError($"File Create failed [DELTA]: {deltaPath}");
                RecordCreationFailure($"[DELTA] not written: {deltaPath}");
            }
        }

        /// <summary>
        /// Create a .del file
        /// </summary>
        /// <param name="SourceFile"></param>
        /// <remarks>Used to mark a file for deletion</remarks>
        private void CreateDelFile(string SourceFile)
        {
            string deltaPath = GetDeltaPath(SourceFile, SourceFolder, "del");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(deltaPath));
                File.Create(deltaPath).Dispose();
                PatchLogger.LogInfo($"File Created [DEL]: {deltaPath}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
                RecordCreationFailure($"[DEL] {deltaPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Create a .new file
        /// </summary>
        /// <param name="TargetFile"></param>
        /// <remarks>Used to mark a file that needs to be added</remarks>
        private void CreateNewFile(string TargetFile)
        {
            FileInfo targetSourceInfo = new FileInfo(TargetFile);

            string deltaPath = GetDeltaPath(TargetFile, TargetFolder, "new");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(deltaPath));
                targetSourceInfo.CopyTo(deltaPath, true);
                PatchLogger.LogInfo($"File Created [NEW]: {deltaPath}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
                RecordCreationFailure($"[NEW] {deltaPath}: {ex.Message}");
            }
        }

        private void RecordCreationFailure(string detail)
        {
            Interlocked.Increment(ref creationFailureCount);
            creationFailures.Enqueue(detail);
        }

        /// <summary>
        /// Generate a full set of patches using the source and target folders specified during contruction./>
        /// </summary>
        /// <returns></returns>
        /// <remarks>Patches are created in the delta folder specified during contruction</remarks>
        public PatchMessage GeneratePatches()
        {
            PatchLogger.LogInfo(" ::: Starting patch generation :::");
            //get all directory information needed
            DirectoryInfo sourceDir = new DirectoryInfo(SourceFolder);
            DirectoryInfo targetDir = new DirectoryInfo(TargetFolder);
            DirectoryInfo deltaDir = Directory.CreateDirectory(DeltaFolder);

            //make sure all directories exist
            if (!sourceDir.Exists)
            {
                string message = $"Could not find source directory: {sourceDir.FullName}";
                PatchLogger.LogError(message);
                return new PatchMessage(message, PatcherExitCode.MissingDir);
            }

            if (!targetDir.Exists)
            {
                string message = $"Could not find target directory: {targetDir.FullName}";
                PatchLogger.LogError(message);
                return new PatchMessage(message, PatcherExitCode.MissingDir);
            }

            if (!deltaDir.Exists)
            {
                string message = $"Could not find delta directory: {deltaDir.FullName}";
                PatchLogger.LogError(message);
                return new PatchMessage(message, PatcherExitCode.MissingDir);
            }

            LazyOperations.ExtractResourcesToTempDir();

            List<FileInfo> sourceFiles = sourceDir.GetFiles("*", SearchOption.AllDirectories).ToList();
            var targetFiles = targetDir.GetFiles("*", SearchOption.AllDirectories).ToList();
            ConcurrentQueue<FileInfo> foundFilesQueue = new ConcurrentQueue<FileInfo>();

            // Case insensitive: these trees come from Windows, where a case difference is the same
            // file rather than a delete plus add.
            var sourceFilesByRelativePath = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (FileInfo sourceFile in sourceFiles)
            {
                sourceFilesByRelativePath[Path.GetRelativePath(sourceDir.FullName, sourceFile.FullName)] = sourceFile;
            }

            fileCountTotal = targetFiles.Count;

            PatchLogger.LogInfo($"Total source files: {fileCountTotal}");

            deltaCount = 0;
            newCount = 0;
            delCount = 0;
            existCount = 0;
            filesProcessed = 0;
            creationFailureCount = 0;
            creationFailures.Clear();

            RaiseProgressChanged(0, fileCountTotal, "Generating deltas...", SnapshotGenerationInfo());

            try
            {


                Parallel.ForEach(targetFiles,
                    new ParallelOptions() { MaxDegreeOfParallelism = 10 }, targetFile =>
                    {
                        //find a matching source file based on the relative path of the file
                        sourceFilesByRelativePath.TryGetValue(
                            Path.GetRelativePath(targetDir.FullName, targetFile.FullName), out FileInfo sourceFile);

                        //if the target file doesn't exist in the source files, the target file needs to be added.
                        if (sourceFile == null)
                        {
                            PatchLogger.LogInfo("::: Creating .new file :::");
                            CreateNewFile(targetFile.FullName);

                            Interlocked.Increment(ref newCount);

                            RaiseProgressChanged(Interlocked.Increment(ref filesProcessed), fileCountTotal,
                                $"{targetFile.FullName.Replace(TargetFolder, "...")}.new", SnapshotGenerationInfo());

                            return;
                        }

                        string extension = "";

                        // Steam's one differs from BSG, always make a new one out of this.
                        if (string.Equals(targetFile.Name, ConsistencyInfoFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            foundFilesQueue.Enqueue(sourceFile);
                            PatchLogger.LogInfo("::: Creating .new file :::");
                            CreateNewFile(targetFile.FullName);

                            Interlocked.Increment(ref newCount);

                            RaiseProgressChanged(Interlocked.Increment(ref filesProcessed), fileCountTotal,
                                $"{targetFile.FullName.Replace(TargetFolder, "...")}.new", SnapshotGenerationInfo());

                            return;
                        }

                        // if a matching source file was found, check the file hashes and get the delta.
                        // add it to the bag for removal later
                        if (!CompareFileContents(sourceFile.FullName, targetFile.FullName))
                        {
                            foundFilesQueue.Enqueue(sourceFile);
                            PatchLogger.LogInfo("::: Creating .delta file :::");
                            CreateDelta(sourceFile.FullName, targetFile.FullName);
                            extension = ".delta";
                            Interlocked.Increment(ref deltaCount);
                        }
                        else
                        {
                            foundFilesQueue.Enqueue(sourceFile);
                            PatchLogger.LogInfo("::: File Exists :::");
                            Interlocked.Increment(ref existCount);
                        }

                        RaiseProgressChanged(Interlocked.Increment(ref filesProcessed), fileCountTotal,
                            $"{targetFile.FullName.Replace(TargetFolder, "...")}{extension}", SnapshotGenerationInfo());
                    });
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
            }

            // remove all queued files that were found in the source files list
            PatchLogger.LogInfo(":: Updating Source List ::");
            try
            {
                int processedQueueCount = 0;
                int queueTotal = foundFilesQueue.Count;

                var matchedSourceFiles = new HashSet<string>(
                    foundFilesQueue.Select(f => f.FullName), StringComparer.OrdinalIgnoreCase);

                foreach (var queuedFile in foundFilesQueue)
                {
                    RaiseProgressChanged(++processedQueueCount, queueTotal,
                        $"Queued file removed: {queuedFile.Name}", SnapshotGenerationInfo());
                }

                sourceFiles.RemoveAll(f => matchedSourceFiles.Contains(f.FullName));
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
            }

            //Any remaining source files do not exist in the target folder and can be removed.
            //reset progress info

            if (sourceFiles.Count == 0)
            {
                PatchLogger.LogInfo("::: Patch Generation Complete :::");

                return GenerationResult();
            }

            RaiseProgressChanged(0, sourceFiles.Count, "Processing .del files...", SnapshotGenerationInfo());
            filesProcessed = 0;
            fileCountTotal = sourceFiles.Count;

            foreach (FileInfo delFile in sourceFiles)
            {
                PatchLogger.LogInfo("::: Creating .del file :::");
                CreateDelFile(delFile.FullName);

                delCount++;
                filesProcessed++;

                RaiseProgressChanged(filesProcessed, fileCountTotal,
                    $"{delFile.FullName.Replace(SourceFolder, "...")}.del", SnapshotGenerationInfo());
            }

            PatchLogger.LogInfo("::: Patch Generation Complete :::");

            return GenerationResult();
        }

        private PatchMessage GenerationResult()
        {
            int failures = Volatile.Read(ref creationFailureCount);

            if (failures == 0)
            {
                return new PatchMessage("Generation Done", PatcherExitCode.Success);
            }

            foreach (string failure in creationFailures)
            {
                PatchLogger.LogError($"Patch file not created: {failure}");
            }

            return new PatchMessage(
                $"{failures} patch file(s) could not be written. The patch set is incomplete - see the log.",
                PatcherExitCode.PatchFailed);
        }

        /// <summary>
        /// Apply a set of patches using the source and delta folders specified during construction.
        /// </summary>
        /// <returns></returns>
        public PatchMessage ApplyPatches()
        {
            PatchLogger.LogInfo("::: Starting patch application :::");

            PatchLogger.LogOSInfo();

            //get needed directory information
            DirectoryInfo sourceDir = new DirectoryInfo(SourceFolder);
            DirectoryInfo deltaDir = new DirectoryInfo(DeltaFolder);

            //check directories exist
            if (!sourceDir.Exists)
            {
                string message = $"Could not find source directory: {sourceDir.FullName}";
                PatchLogger.LogError(message);
                return new PatchMessage(message, PatcherExitCode.MissingDir);
            }

            if (!deltaDir.Exists)
            {
                string message = $"Could not find delta directory: {deltaDir.FullName}";
                PatchLogger.LogError(message);
                return new PatchMessage(message, PatcherExitCode.MissingDir);
            }

            LazyOperations.ExtractResourcesToTempDir();

            List<FileInfo> SourceFiles = sourceDir.GetFiles("*", SearchOption.AllDirectories).ToList();

            List<FileInfo> deltaFiles = deltaDir.GetFiles("*", SearchOption.AllDirectories).ToList();

            deltaCount = deltaFiles.Where(x => x.Extension == ".delta").Count();
            newCount = deltaFiles.Where(x => x.Extension == ".new").Count();
            delCount = deltaFiles.Where(x => x.Extension == ".del").Count();

            PatchLogger.LogInfo($"Patch File Counts: DELTA({deltaCount}) - NEW({newCount}) - DEL({delCount})");

            var sourceFilesByRelativePath = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (FileInfo sourceFile in SourceFiles)
            {
                sourceFilesByRelativePath[Path.GetRelativePath(sourceDir.FullName, sourceFile.FullName)] = sourceFile;
            }

            ConcurrentQueue<PatchMessage> errorsQueue = new ConcurrentQueue<PatchMessage>();
            filesProcessed = 0;

            fileCountTotal = deltaFiles.Count;
            var patchingTokenSource = new CancellationTokenSource();

            try
            {
                Parallel.ForEach(deltaFiles,
                    new ParallelOptions() { MaxDegreeOfParallelism = 5, CancellationToken = patchingTokenSource.Token },
                    deltaFile =>
                    {
                        string relativePath = Path.GetRelativePath(deltaDir.FullName, deltaFile.FullName);
                        string targetRelativePath =
                            Path.ChangeExtension(relativePath, null);

                        switch (deltaFile.Extension)
                        {
                            case ".delta":
                            {
                                //apply delta
                                sourceFilesByRelativePath.TryGetValue(targetRelativePath, out FileInfo sourceFile);

                                if (sourceFile == null)
                                {
                                    errorsQueue.Enqueue(new PatchMessage(
                                        $"Failed to find matching source file for '{deltaFile.FullName}'",
                                        PatcherExitCode.MissingFile));
                                    patchingTokenSource.Cancel();
                                    return;
                                }

                                PatchLogger.LogInfo("::: Applying Delta :::");
                                var result = ApplyDelta(sourceFile.FullName, deltaFile.FullName);

                                if (!result.Item1)
                                {
                                    errorsQueue.Enqueue(new PatchMessage(result.Item2, PatcherExitCode.PatchFailed));
                                    patchingTokenSource.Cancel();
                                    return;
                                }

                                Interlocked.Decrement(ref deltaCount);

                                break;
                            }
                            case ".new":
                            {
                                //copy new file
                                string destination = Path.Join(sourceDir.FullName, targetRelativePath);

                                PatchLogger.LogInfo("::: Adding New File :::");

                                try
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                                    File.Copy(deltaFile.FullName, destination, true);
                                    PatchLogger.LogInfo($"File added: {destination}");
                                }
                                catch (Exception ex)
                                {
                                    PatchLogger.LogException(ex);
                                    errorsQueue.Enqueue(new PatchMessage(ex.Message, PatcherExitCode.PatchFailed));
                                    patchingTokenSource.Cancel();
                                    return;
                                }

                                Interlocked.Decrement(ref newCount);

                                break;
                            }
                            case ".del":
                            {
                                //remove unneeded file
                                string delFilePath = Path.Join(sourceDir.FullName, targetRelativePath);

                                PatchLogger.LogInfo("::: Removing Uneeded File :::");

                                try
                                {
                                    if (File.Exists(delFilePath))
                                    {
                                        File.Delete(delFilePath);
                                        PatchLogger.LogInfo($"File removed: {delFilePath}");
                                    }
                                    else
                                    {
                                        PatchLogger.LogInfo($"Already absent: {delFilePath}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    PatchLogger.LogException(ex);
                                    errorsQueue.Enqueue(new PatchMessage(ex.Message, PatcherExitCode.PatchFailed));
                                    patchingTokenSource.Cancel();
                                    return;
                                }

                                Interlocked.Decrement(ref delCount);

                                break;
                            }
                        }

                        RaiseProgressChanged(Interlocked.Increment(ref filesProcessed), fileCountTotal,
                            deltaFile.Name, SnapshotApplyInfo());
                    });
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
            }

            if (errorsQueue.Count > 0)
            {
                PatchLogger.LogError($"Error queue entry count: {errorsQueue.Count}");
                PatchLogger.LogError("Dequeuing errors");
                
                PatchMessage firstError = null;

                while (errorsQueue.TryDequeue(out PatchMessage error))
                {
                    firstError ??= error;
                    PatchLogger.LogError(error.Message);
                }

                return firstError ?? new PatchMessage("Something went wrong :(", PatcherExitCode.PatchFailed);
            }

            PatchLogger.LogInfo("::: Patching Complete :::");
            return new PatchMessage($"Patching Complete. You can delete the patcher.exe file.",
                PatcherExitCode.Success);
        }
    }
}