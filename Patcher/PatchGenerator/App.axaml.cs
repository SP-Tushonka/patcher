using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PatchGenerator.Models;
using PatchGenerator.ViewModels;
using PatchGenerator.Views;
using ReactiveUI;
using ReactiveUI.Primitives;
using System;
using System.Reflection;
using PatcherUtils.Model;

namespace PatchGenerator
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
                
            PatchLogger.LogInfo($"Patch Generator v{version?.ToString() ?? "N/A"}");
            
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Startup += Desktop_Startup;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void Desktop_Startup(object? sender, ControlledApplicationLifetimeStartupEventArgs e)
        {
            if (sender is IClassicDesktopStyleApplicationLifetime desktop)
            {
                GenStartupArgs genArgs = GenStartupArgs.Parse(e.Args);
                MainWindowViewModel viewModel = new MainWindowViewModel(genArgs);

                // ViewModel as well as DataContext: ReactiveUI no longer syncs the two, and without
                // it the view model never activates, so the router never navigates.
                desktop.MainWindow = new MainWindow
                {
                    DataContext = viewModel,
                    ViewModel = viewModel
                };
            }
        }
    }
}
