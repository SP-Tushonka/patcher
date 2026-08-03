using PatcherUtils.Model;
using Avalonia;
using PatchGenerator.Models;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;

namespace PatchGenerator.ViewModels
{
    public class MainWindowViewModel : ReactiveObject, IActivatableViewModel, IScreen
    {
        public RoutingState Router { get; } = new RoutingState();
        public ViewModelActivator Activator { get; } = new ViewModelActivator();

        public ReactiveCommand<RxVoid, RxVoid> CloseCommand => ReactiveCommand.Create(() =>
        {
            if (Application.Current.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopApp)
            {
                desktopApp.MainWindow.Close();
            }
        });

        public MainWindowViewModel(GenStartupArgs genArgs = null)
        {
            this.WhenActivated((MultipleDisposable disposables) =>
            {
                if (genArgs != null && genArgs.ReadyToRun)
                {
                    PatchGenInfo genInfo = new PatchGenInfo();

                    genInfo.TargetFolderPath = genArgs.TargetFolderPath;
                    genInfo.SourceFolderPath = genArgs.SourceFolderPath;
                    genInfo.PatchName = genArgs.OutputFolderName;
                    genInfo.AutoClose = genArgs.AutoClose;
                    genInfo.AutoZip = genArgs.AutoZip;

                    Router.Navigate.Execute(new PatchGenerationViewModel(this, genInfo)).Run();
                    return;
                }

                Router.Navigate.Execute(new OptionsViewModel(this)).Run();
            });
        }
    }
}
