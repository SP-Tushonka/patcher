using System;
using System.IO;
using System.Reflection;
using PatchClient.Models;
using PatcherUtils;
using PatcherUtils.Model;

namespace PatchGenerator.Models
{
    /// <summary>
    /// Generation steps shared by the windowed and headless entry points.
    /// </summary>
    public static class PatchGeneration
    {
        public static string OutputFolder(string patchName) => patchName.FromCwd();

        public static PatchMessage Run(string sourceFolder, string targetFolder, string patchName,
            ProgressChangedHandler onProgress)
        {
            string patchOutputFolder = Path.Join(OutputFolder(patchName), LazyOperations.PatchFolder);

            // PatchClient.exe is embedded here rather than in PatcherUtils.
            LazyOperations.ExtractResourcesToTempDir(Assembly.GetExecutingAssembly());

            PatchHelper patcher = new PatchHelper(sourceFolder, targetFolder, patchOutputFolder);
            patcher.ProgressChanged += onProgress;

            return patcher.GeneratePatches();
        }

        /// <summary>
        /// Name the archive after the two client versions, falling back to "patcher" when either
        /// EscapeFromTarkov.exe is missing or carries no version.
        /// </summary>
        public static string ArchiveName(string sourceFolder, string targetFolder) =>
            ClientVersion.BuildPatchName(sourceFolder, targetFolder) ?? "patcher";

        /// <summary>
        /// Compress the finished output folder, leaving the archive inside it next to patcher.exe.
        /// </summary>
        /// <returns>The archive path, or null if compression failed.</returns>
        public static string CompressOutput(string patchName, string archiveName, IProgress<int> progress)
        {
            string outputFolder = OutputFolder(patchName);

            // Staged as a sibling first: compressing straight into the folder being compressed would
            // feed the archive to itself. The parent is the same volume, so the move is a rename.
            string staged = Path.Combine(Path.GetDirectoryName(outputFolder) ?? ".", $"{archiveName}.7z");
            string destination = Path.Combine(outputFolder, $"{archiveName}.7z");

            if (!LazyOperations.CompressDirectory(outputFolder, staged, progress))
            {
                return null;
            }

            try
            {
                File.Move(staged, destination, true);
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
                return null;
            }

            return destination;
        }

        public static void CopyPatchClient(string patchName)
        {
            File.Copy(LazyOperations.PatcherClientPath,
                Path.Combine(OutputFolder(patchName), "patcher.exe"), true);

            PatchLogger.LogInfo("Copied patcher.exe to output folder");
        }
    }
}
