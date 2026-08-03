using System;
using PatcherUtils.Model;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Linq;
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

        private static string SevenZipExe =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "7za.exe" : "7zz";

        /// <summary>
        /// The path to the 7-Zip console binary in the <see cref="TempDir"/>
        /// </summary>
        public static string SevenZipPath = Path.Combine(TempDir, SevenZipExe);

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
                    case string a when a.EndsWith(SevenZipExe):
                        {
                            StreamResourceOut(assembly, resource, SevenZipPath);
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

        /// <summary>
        /// Compress a folder to a .7z archive using LZMA2, matching the settings EftPatchHelper uses.
        /// </summary>
        /// <returns>True if the archive was written.</returns>
        public static bool CompressDirectory(string SourceDirectoryPath, string DestinationFilePath,
            IProgress<int> progress)
        {
            try
            {
                var sourceDir = new DirectoryInfo(Path.GetFullPath(SourceDirectoryPath));
                var outputFile = new FileInfo(Path.GetFullPath(DestinationFilePath));

                PatchLogger.LogInfo($"Compressing: {sourceDir.FullName}");
                PatchLogger.LogInfo($"Output file: {outputFile.FullName}");

                if (outputFile.Exists)
                {
                    outputFile.Delete();
                }

                // Run from inside the folder and add its contents, so patcher.exe and the patch folder
                // sit at the archive root and extract straight into a game folder.
                var startInfo = new ProcessStartInfo
                {
                    FileName = SevenZipPath,
                    WorkingDirectory = sourceDir.FullName,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                foreach (string argument in new[]
                         {
                             "a", "-t7z", "-m0=LZMA2", "-mx=5", "-bsp1",
                             outputFile.FullName, "*"
                         })
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using Process sevenZip = Process.Start(startInfo);

                ReportSevenZipProgress(sevenZip, progress);

                sevenZip.WaitForExit();

                if (sevenZip.ExitCode != 0)
                {
                    PatchLogger.LogError($"7-Zip exited {sevenZip.ExitCode}");
                    return false;
                }

                outputFile.Refresh();

                if (!outputFile.Exists || outputFile.Length == 0)
                {
                    PatchLogger.LogError("Failed to compress patcher");
                    return false;
                }

                PatchLogger.LogInfo($"Compression complete: {outputFile.Length} bytes");
                return true;
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
                return false;
            }
        }

        // -bsp1 puts progress on stdout as lines containing a percentage.
        private static void ReportSevenZipProgress(Process sevenZip, IProgress<int> progress)
        {
            if (progress == null)
            {
                return;
            }

            string line;

            while ((line = sevenZip.StandardOutput.ReadLine()) != null)
            {
                int percentIndex = line.IndexOf('%');

                if (percentIndex <= 0)
                {
                    continue;
                }

                string digits = new string(line.Substring(0, percentIndex)
                    .Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());

                if (int.TryParse(digits, out int percent))
                {
                    progress.Report(percent);
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
