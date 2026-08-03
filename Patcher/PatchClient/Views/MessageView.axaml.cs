using ReactiveUI.Primitives.Disposables;
using Avalonia.Markup.Xaml;
using ReactiveUI.Avalonia;
using PatchClient.ViewModels;
using ReactiveUI;

namespace PatchClient.Views
{
    public partial class MessageView : ReactiveUserControl<MessageViewModel>
    {
        public MessageView()
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
