using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PatcherUtils.Model;

namespace PatchGenerator.Models
{
    public static class ClientVersion
    {
        private const string ClientExe = "EscapeFromTarkov.exe";
        private const string ConsistencyInfo = "ConsistencyInfo";

        /// <summary>
        /// Read the EFT version from a client folder, e.g. "0.16.9.5.40743".
        /// </summary>
        /// <returns>Null if neither the exe nor the manifest carries a usable version.</returns>
        public static string Read(string clientFolder)
        {
            if (string.IsNullOrWhiteSpace(clientFolder))
            {
                return null;
            }

            return ReadFromClientExe(clientFolder) ?? ReadFromConsistencyInfo(clientFolder);
        }

        /// <summary>
        /// Build the "Patcher_{source}_to_{target}" name from the two clients.
        /// </summary>
        /// <returns>Null if either version could not be read.</returns>
        public static string BuildPatchName(string sourceFolder, string targetFolder)
        {
            string source = Read(sourceFolder);
            string target = Read(targetFolder);

            if (source == null || target == null)
            {
                return null;
            }

            return $"Patcher_{source}_to_{target}";
        }

        private static string ReadFromClientExe(string clientFolder)
        {
            string exePath = Path.Combine(clientFolder, ClientExe);

            if (!File.Exists(exePath))
            {
                PatchLogger.LogInfo($"No {ClientExe} in {clientFolder}");
                return null;
            }

            try
            {
                // ProductVersion looks like "0.16.9.5-40743-f137e819"; the first two segments are
                // the version and build that clients are named by.
                string productVersion = FileVersionInfo.GetVersionInfo(exePath).ProductVersion;

                if (string.IsNullOrWhiteSpace(productVersion))
                {
                    return null;
                }

                string[] parts = productVersion.Split('-');

                return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : parts[0];
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
                return null;
            }
        }

        /// <summary>
        /// Outside Windows, FileVersionInfo only reads managed assembly metadata, so a native client
        /// exe yields nothing. The manifest beside it carries the same version string.
        /// </summary>
        private static string ReadFromConsistencyInfo(string clientFolder)
        {
            string manifestPath = Path.Combine(clientFolder, ConsistencyInfo);

            if (!File.Exists(manifestPath))
            {
                PatchLogger.LogInfo($"No {ConsistencyInfo} in {clientFolder}");
                return null;
            }

            try
            {
                using FileStream stream = File.OpenRead(manifestPath);
                using JsonDocument document = JsonDocument.Parse(stream);

                if (!document.RootElement.TryGetProperty("Version", out JsonElement version))
                {
                    return null;
                }

                string value = version.GetString();

                PatchLogger.LogInfo($"Read version {value} from {ConsistencyInfo}");

                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch (Exception ex)
            {
                PatchLogger.LogException(ex);
                return null;
            }
        }
    }
}
