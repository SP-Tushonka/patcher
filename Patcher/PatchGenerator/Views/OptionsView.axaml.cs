using ReactiveUI.Primitives.Disposables;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using PatchGenerator.ViewModels;
using ReactiveUI;

namespace PatchGenerator.Views
{
    public partial class OptionsView : ReactiveUserControl<OptionsViewModel>
    {
        public OptionsView()
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
