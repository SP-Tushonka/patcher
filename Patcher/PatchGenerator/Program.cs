using Avalonia;
using Avalonia.ReactiveUI;
using Splat;
using System;
using ReactiveUI;
using System.Reflection;
using PatchGenerator.Models;

namespace PatchGenerator
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static int Main(string[] args)
        {
            // Must return before any Avalonia call; a desktop lifetime needs a display server.
            GenStartupArgs genArgs = GenStartupArgs.Parse(args);

            if (genArgs != null && genArgs.Headless)
            {
                if (!genArgs.ReadyToRun)
                {
                    Console.Error.WriteLine(
                        "headless needs outputfoldername, sourcefolderpath and targetfolderpath.");
                    return 1;
                }

                return HeadlessGenerator.Run(genArgs);
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            return 0;
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            Locator.CurrentMutable.RegisterViewsForViewModels(Assembly.GetExecutingAssembly());

            return AppBuilder.Configure<App>()
                .UseReactiveUI()
                .UsePlatformDetect()
                .LogToTrace();
        }
    }
}
