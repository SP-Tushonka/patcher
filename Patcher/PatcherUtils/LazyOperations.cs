using System;
using PatcherUtils.Model;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using SevenZip;

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

        private static string SevenZDll = "7z.dll";

        /// <summary>
        /// The path to the 7za.exe file in the <see cref="TempDir"/>
        /// </summary>
        public static string SevenZDllPath = Path.Combine(TempDir, SevenZDll);

        private static string PatcherClient = "PatchClient.exe";
        /// <summary>
        /// The path to the patcher.exe file in the <see cref="TempDir"/>
        /// </summary>
        public static string PatcherClientPath = Path.Combine(TempDir, PatcherClient);

        /// <summary>
        /// The hdiffz binary for the running platform. Patch generation shells out to this; the
        /// Windows and Linux builds are both embedded so the generator can run on either.
        /// </summary>
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

            // Extracting an executable does not carry a permission bit, so on unix the freshly
            // written hdiffz binary is not runnable until we set one.
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
                    case string a when a.EndsWith(SevenZDll):
                        {
                            StreamResourceOut(assembly, resource, SevenZDllPath);
                            break;
                        }
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

        public static void CompressDirectory(string SourceDirectoryPath, string DestinationFilePath, IProgress<int> progress)
        {
            try
            {
                PatchLogger.LogInfo($"Compressing: {SourceDirectoryPath}");
                PatchLogger.LogInfo($"Output file: {DestinationFilePath}");
                var outputFile = new FileInfo(DestinationFilePath);
                SevenZipBase.SetLibraryPath(SevenZDllPath);
                
                PatchLogger.LogInfo($"7z.dll set: {SevenZDllPath}");
                
                var compressor = new SevenZipCompressor()
                {
                    ArchiveFormat = OutArchiveFormat.SevenZip,
                    CompressionMethod = CompressionMethod.Lzma2,
                    CompressionLevel = CompressionLevel.Normal,
                    PreserveDirectoryRoot = true
                };

                compressor.Compressing += (_, args) => { progress.Report(args.PercentDone); };

                using var outputStream = outputFile.OpenWrite();

                PatchLogger.LogInfo("Starting compression");
                
                compressor.CompressDirectory(SourceDirectoryPath, outputStream);
                
                PatchLogger.LogInfo("Compression complete");

                outputFile.Refresh();

                // failed to compress data
                if (!outputFile.Exists || outputFile.Length == 0)
                {
                    PatchLogger.LogError("Failed to compress patcher");
                }
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
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
