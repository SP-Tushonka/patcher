using ReactiveUI.Primitives.Disposables;
using Avalonia;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using PatchClient.ViewModels;
using ReactiveUI;

namespace PatchClient.Views
{
    public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.WhenActivated((MultipleDisposable disposables) => { });
            AvaloniaXamlLoader.Load(this);
        }
    }
}
