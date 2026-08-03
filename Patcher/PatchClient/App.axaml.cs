using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PatchClient.ViewModels;
using PatchClient.Views;
using ReactiveUI;
using ReactiveUI.Primitives;
using System;
using System.Linq;
using System.Reflection;
using PatcherUtils.Model;

namespace PatchClient
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                bool autoClose = false;
                bool debugOutput = false;

                if (desktop.Args != null && desktop.Args.Length >= 1)
                {
                    autoClose = desktop.Args.Any(x => x.ToLower() == "autoclose");
                    debugOutput = desktop.Args.Any(x => x.ToLower() == "debug");
                }

                if (debugOutput)
                {
                    PatchLogger.LogInfo("Running in debug mode");
                }

                if (autoClose)
                {
                    PatchLogger.LogInfo("Running with autoclose");
                }

                var version = Assembly.GetExecutingAssembly().GetName().Version;
                
                PatchLogger.LogInfo($"Patch Client v{version?.ToString() ?? "N/A"}");

                MainWindowViewModel viewModel = new MainWindowViewModel(autoClose, debugOutput);

                // ViewModel as well as DataContext: ReactiveUI no longer syncs the two, and without
                // it the view model never activates, so the router never navigates.
                desktop.MainWindow = new MainWindow
                {
                    DataContext = viewModel,
                    ViewModel = viewModel
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
