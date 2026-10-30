using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PatcherUtils.Model
{
    public static class PatchLogger
    {
        private static readonly Lock WriteLock = new Lock();

        /// <summary>
        /// Resolved once on first use, so it is fixed to the working directory at that moment.
        /// </summary>
        public static readonly string LogFilePath = "Patcher.log".FromCwd();

        private static StreamWriter writer;

        /// <summary>
        /// Also write errors to stderr. Set by headless runs, which have no other way to show them.
        /// </summary>
        public static bool EchoErrorsToConsole;

        /// <summary>
        /// Receives every line instead of Patcher.log, with its level, for hosts that keep their own log such as the installer.
        /// </summary>
        public static Action<string, string> Redirect;

        private static void Write(string level, string message)
        {
            if (Redirect != null)
            {
                Redirect(level, message);
                return;
            }

            LogToFile($"{GetTimestamp()}[{level}]: {message}");
        }

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
            Write("OS", RuntimeInformation.OSDescription);
        }

        public static void LogDebug(string message) => Write("DEBUG", message);

        public static void LogInfo(string message) => Write("INFO", message);

        public static void LogError(string message)
        {
            Write("ERROR", message);
            EchoError($"ERROR: {message}");
        }

        public static void LogException(Exception ex)
        {
            Write("EXCEPTION", $"{ex.Message}\n\nStackTrace:\n{ex.StackTrace}");
            EchoError($"EXCEPTION: {ex.Message}");
        }
    }
}
