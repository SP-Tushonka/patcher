using System;
using System.IO;
using System.Runtime.InteropServices;
using PatchClient.Models;
using PatcherUtils;
using PatcherUtils.Model;
using PatchGenerator.Models;

namespace PatchGenerator
{
    /// <summary>
    /// Generates patches without starting Avalonia, for machines with no display server.
    /// </summary>
    public static class HeadlessGenerator
    {
        private const int ProgressStepPercent = 2;
        private const int AttachParentProcess = -1;

        private static int _lastReportedPercent = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        // This is a WinExe so windowed mode doesn't flash a console, which leaves a headless run on
        // Windows with nowhere to write.
        private static void AttachToParentConsole()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !AttachConsole(AttachParentProcess))
            {
                return;
            }

            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }

        public static int Run(GenStartupArgs args)
        {
            AttachToParentConsole();

            Console.WriteLine($"Source: {args.SourceFolderPath}");
            Console.WriteLine($"Target: {args.TargetFolderPath}");
            Console.WriteLine($"Output: {PatchGeneration.OutputFolder(args.OutputFolderName)}");
            Console.WriteLine();

            PatchMessage message;

            try
            {
                message = PatchGeneration.Run(args.SourceFolderPath, args.TargetFolderPath,
                    args.OutputFolderName, ReportProgress);
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
                Console.Error.WriteLine($"Patch generation failed: {ex.Message}");
                return (int)PatcherExitCode.PatchFailed;
            }

            Console.WriteLine();
            Console.WriteLine($"{message.ExitCode}: {message.Message}");

            if (message.ExitCode != PatcherExitCode.Success)
            {
                return (int)message.ExitCode;
            }

            PatchGeneration.CopyPatchClient(args.OutputFolderName);

            // PatcherExitCode.Success is 10; a shell expects 0.
            return 0;
        }

        private static void ReportProgress(object sender, int progress, int total, int percent,
            string message = "", params LineItem[] additionalLineItems)
        {
            if (percent == _lastReportedPercent || percent % ProgressStepPercent != 0)
            {
                return;
            }

            _lastReportedPercent = percent;

            string counts = additionalLineItems.Length == 0
                ? ""
                : "  " + string.Join("  ", Array.ConvertAll(additionalLineItems, i => $"{i.ItemText}: {i.ItemValue}"));

            Console.WriteLine($"[{percent,3}%] {progress}/{total}{counts}");
        }
    }
}
