using System;
using PatcherUtils.Model;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PatcherUtils
{
    public class LazyOperations
    {
        /// <summary>
        /// A directory to store temporary data.
        /// </summary>
        public static string TempDir = "PATCHER_TEMP".FromCwd();


        /// <summary>
        /// The folder that the patches will be stored in
        /// </summary>
        public static string PatchFolder = "SPT_Patches";

        private static string PatcherClient = "PatchClient.exe";
        /// <summary>
        /// The path to the patcher.exe file in the <see cref="TempDir"/>
        /// </summary>
        public static string PatcherClientPath = Path.Combine(TempDir, PatcherClient);

        private static string HDiffEXE =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "hdiffz.exe" : "hdiffz";

        /// <summary>
        /// The path to the hdiffz file in the <see cref="TempDir"/>
        /// </summary>
        public static string HDiffPath = Path.Combine(TempDir, HDiffEXE);

        /// <summary>
        /// Streams embedded resources out of the assembly
        /// </summary>
        /// <param name="ResourceName"></param>
        /// <param name="OutputFilePath"></param>
        /// <remarks>The resource will not be streamed out if the <paramref name="OutputFilePath"/> already exists</remarks>
        private static void StreamResourceOut(Assembly assembly, string ResourceName, string OutputFilePath)
        {
            FileInfo outputFile = new FileInfo(OutputFilePath);

            if (outputFile.Exists)
            {
                PatchLogger.LogInfo($"Deleting Existing Resource: {outputFile.Name}");
                outputFile.Delete();
            }

            if (!outputFile.Directory.Exists)
            {
                PatchLogger.LogInfo($"Creating Resource Directory: {outputFile.Directory.Name}");
                Directory.CreateDirectory(outputFile.Directory.FullName);
            }

            using (FileStream fs = File.Create(OutputFilePath))
            using (Stream s = assembly.GetManifestResourceStream(ResourceName))
            {
                s.CopyTo(fs);
                PatchLogger.LogInfo($"Resourced streamed out of assembly: {outputFile.Name}");
            }

            // Extracted files have no exec bit on unix.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(OutputFilePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }

        /// <summary>
        /// Checks the resources in the assembly and streams them to the temp directory for later use.
        /// </summary>
        public static void ExtractResourcesToTempDir(Assembly assembly = null)
        {
            if(assembly == null) assembly = Assembly.GetExecutingAssembly();

            foreach (string resource in assembly.GetManifestResourceNames())
            {
                switch (resource)
                {
                    case string a when a.EndsWith(PatcherClient):
                        {
                            StreamResourceOut(assembly, resource, PatcherClientPath);
                            break;
                        }
                    case string a when a.EndsWith(HDiffEXE):
                        {
                            StreamResourceOut(assembly, resource, HDiffPath);
                            break;
                        }
                }
            }
        }

        /// <summary>
        /// Deletes the <see cref="TempDir"/> recursively
        /// </summary>
        public static void CleanupTempDir()
        {
            DirectoryInfo dir = new DirectoryInfo(TempDir);

            if (dir.Exists)
            {
                dir.Delete(true);
                PatchLogger.LogInfo("Temp directory deleted");
            }
        }
    }
}
