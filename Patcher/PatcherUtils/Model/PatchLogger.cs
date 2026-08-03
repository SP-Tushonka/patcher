using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PatcherUtils.Model
{
    public static class PatchLogger
    {
        private static readonly object WriteLock = new object();

        /// <summary>
        /// Resolved once on first use, so it is fixed to the working directory at that moment.
        /// </summary>
        public static readonly string LogFilePath = "Patcher.log".FromCwd();

        private static StreamWriter writer;

        /// <summary>
        /// Also write errors to stderr. Set by headless runs, which have no other way to show them.
        /// </summary>
        public static bool EchoErrorsToConsole;

        private static void LogToFile(string message)
        {
            // Patching runs on several threads; unsynchronised appends collide and lose lines.
            lock (WriteLock)
            {
                try
                {
                    // FileShare.ReadWrite so the log stays readable while patching runs.
                    // AutoFlush because the app exits via Environment.Exit, which skips finalizers.
                    writer ??= new StreamWriter(
                        new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    {
                        AutoFlush = true
                    };

                    writer.WriteLine(message);
                }
                catch (Exception)
                {
                    //TODO - proper logging at some point or whatever idk... -waffle
                }
            }
        }

        private static void EchoError(string message)
        {
            if (!EchoErrorsToConsole)
            {
                return;
            }

            try
            {
                Console.Error.WriteLine(message);
            }
            catch (IOException)
            {
            }
        }

        private static string GetTimestamp()
        {
            return DateTime.Now.ToString("MM/dd/yyyy - hh:mm:ss tt]");
        }

        public static void LogOSInfo()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                LogToFile($"{GetTimestamp()}[OS]: Windows");

            if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                LogToFile($"{GetTimestamp()}[OS]: Linux");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                LogToFile($"{GetTimestamp()}[OS]: OSX");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
                LogToFile($"{GetTimestamp()}[OS]: FreeBSD");


            LogToFile($"{GetTimestamp()}[OS]: {RuntimeInformation.OSDescription}");
        }

        public static void LogDebug(string message) => LogToFile($"{GetTimestamp()}[DEBUG]: {message}");

        public static void LogInfo(string message) => LogToFile($"{GetTimestamp()}[INFO]: {message}");

        public static void LogError(string message)
        {
            LogToFile($"{GetTimestamp()}[ERROR]: {message}");
            EchoError($"ERROR: {message}");
        }

        public static void LogException(Exception ex)
        {
            LogToFile($"{GetTimestamp()}[EXCEPTION]: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}");
            EchoError($"EXCEPTION: {ex.Message}");
        }
    }
}
