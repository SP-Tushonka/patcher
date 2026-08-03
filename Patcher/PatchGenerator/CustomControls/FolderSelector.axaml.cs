using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using System.IO;
using System.Linq;

namespace PatchGenerator.CustomControls
{
    public partial class FolderSelector : UserControl
    {
        public FolderSelector()
        {
            InitializeComponent();

            AddHandler(DragDrop.DropEvent, Drop);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Drop(object sender, DragEventArgs e)
        {
            if (!e.DataTransfer.Contains(DataFormat.File))
            {
                return;
            }

            string[] filePaths = e.DataTransfer.TryGetFiles()
                ?.Select(item => item.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .ToArray() ?? [];

            if (filePaths.Length != 1)
            {
                FolderPath = "Cannot drop multiple files";
                FolderSelected = false;
                return;
            }

            if (!new DirectoryInfo(filePaths[0]).Exists)
            {
                FolderPath = "Dropped object must be a folder";
                FolderSelected = false;
                return;
            }

            FolderPath = filePaths[0];
            FolderSelected = true;
        }

        private static readonly StyledProperty<bool> FolderSelectedProperty =
            AvaloniaProperty.Register<FolderSelector, bool>(nameof(FolderSelected));

        private bool FolderSelected
        {
            get => GetValue(FolderSelectedProperty);
            set => SetValue(FolderSelectedProperty, value);
        }

        public static readonly StyledProperty<string> FolderPathProperty =
            AvaloniaProperty.Register<FolderSelector, string>(nameof(FolderPath));

        public string FolderPath
        {
            get => GetValue(FolderPathProperty);
            set => SetValue(FolderPathProperty, value);
        }
    }
}
