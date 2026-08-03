using PatcherUtils.Model;
using Avalonia;
using ReactiveUI.Avalonia;
using Splat;
using System;
using ReactiveUI;
using System.Reflection;

namespace PatchClient
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UseReactiveUI(builder => builder
                    .WithExceptionHandler(new LoggingExceptionObserver())
                    // RoutedViewHost resolves through these; there is no assembly scan in ReactiveUI 24.
                    .RegisterView<Views.MessageView, ViewModels.MessageViewModel>()
                    .RegisterView<Views.PatcherView, ViewModels.PatcherViewModel>())
                .UsePlatformDetect()
                .LogToTrace();
        }
    }
}
