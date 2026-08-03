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

        public static void CopyPatchClient(string patchName)
        {
            File.Copy(LazyOperations.PatcherClientPath,
                Path.Combine(OutputFolder(patchName), "patcher.exe"), true);

            PatchLogger.LogInfo("Copied patcher.exe to output folder");
        }
    }
}
