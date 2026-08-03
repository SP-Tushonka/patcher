namespace PatchGenerator.Models
{
    public class GenStartupArgs
    {
        public bool ReadyToRun => OutputFolderName != "" && SourceFolderPath != "" && TargetFolderPath != "";
        public string OutputFolderName { get; private set; } = "";
        public string SourceFolderPath { get; private set; } = "";
        public string TargetFolderPath { get; private set; } = "";
        public bool AutoClose { get; private set; } = false;

        public bool Headless { get; private set; } = false;

        protected GenStartupArgs(string outputFolderName, string sourceFolderPath, string targetFolderPath, bool autoClose, bool headless)
        {
            OutputFolderName = outputFolderName;
            SourceFolderPath = sourceFolderPath;
            TargetFolderPath = targetFolderPath;
            AutoClose = autoClose;
            Headless = headless;
        }

        public static GenStartupArgs Parse(string[] Args)
        {
            if (Args == null || Args.Length == 0) return null;

            string outputFolderPath = "";
            string sourceFolderPath = "";
            string targetFolderPath = "";
            bool autoClose = false;
            bool headless = false;

            foreach (string arg in Args)
            {
                if (arg.Split("::").Length != 2) return null;

                var argSplit = arg.Split("::");

                switch (argSplit[0].ToLower())
                {
                    case "outputfoldername":
                        {
                            outputFolderPath = argSplit[1];
                            break;
                        }
                    case "sourcefolderpath":
                        {
                            sourceFolderPath = argSplit[1];
                            break;
                        }
                    case "targetfolderpath":
                        {
                            targetFolderPath = argSplit[1];
                            break;
                        }
                    case "autoclose":
                        {
                            autoClose = bool.Parse(argSplit[1]);
                            break;
                        }
                    case "headless":
                        {
                            headless = bool.Parse(argSplit[1]);
                            break;
                        }
                }
            }

            return new GenStartupArgs(outputFolderPath, sourceFolderPath, targetFolderPath, autoClose, headless);
        }
    }
}
