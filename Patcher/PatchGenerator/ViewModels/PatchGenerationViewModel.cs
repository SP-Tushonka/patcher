using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using PatchClient.Models;
using PatcherUtils;
using PatchGenerator.Helpers;
using PatchGenerator.Models;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ReactiveUI.Primitives.Disposables;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using PatcherUtils.Model;
using Timer = System.Timers.Timer;

namespace PatchGenerator.ViewModels
{
    public class PatchGenerationViewModel : ViewModelBase
    {
        private const long UiRefreshIntervalMs = 50;
        private const int MaxVisiblePatchItems = 200;

        private long lastUiRefreshMs;

        private bool _AutoScroll = true;
        public bool AutoScroll
        {
            get => _AutoScroll;
            set => this.RaiseAndSetIfChanged(ref _AutoScroll, value);
        }

        private string _ProgressMessage;
        public string ProgressMessage
        {
            get => _ProgressMessage;
            set => this.RaiseAndSetIfChanged(ref _ProgressMessage, value);
        }

        private int _PatchPercent;
        public int PatchPercent
        {
            get => _PatchPercent;
            set => this.RaiseAndSetIfChanged(ref _PatchPercent, value);
        }

        private bool _IndeterminateProgress = false;
        public bool IndeterminateProgress
        {
            get => _IndeterminateProgress;
            set => this.RaiseAndSetIfChanged(ref _IndeterminateProgress, value);
        }

        private string _ElapsedTimeDetails;
        public string ElapsedTimeDetails
        {
            get => _ElapsedTimeDetails;
            set => this.RaiseAndSetIfChanged(ref _ElapsedTimeDetails, value);
        }

        private LineItem[] lineItems;

        public ObservableCollection<PatchItem> PatchItemCollection { get; set; } = new ObservableCollection<PatchItem>();
        public ObservableCollection<PatchItem> PatchItemLegendCollection { get; set; } = new ObservableCollection<PatchItem>();

        private Stopwatch patchGenStopwatch = new Stopwatch();
        private Timer updateElapsedTimeTimer = new Timer(1000);

        private readonly PatchGenInfo generationInfo;
        public PatchGenerationViewModel(IScreen Host, PatchGenInfo GenerationInfo) : base(Host)
        {
            generationInfo = GenerationInfo;
            ElapsedTimeDetails = "Starting ...";

            updateElapsedTimeTimer.Elapsed += UpdateElapsedTimeTimer_Elapsed;

            foreach (KeyValuePair<string, IBrush> pair in PatchItemDefinitions.Colors)
            {
                PatchItemLegendCollection.Add(new PatchItem("")
                {
                    Name = pair.Key,
                    Color = pair.Value,
                });
            }

            this.WhenActivated((MultipleDisposable dissposables) =>
            {
                GeneratePatches();
            });
        }

        private void UpdateElapsedTimeTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                ElapsedTimeDetails = $"Elapsed {patchGenStopwatch.Elapsed:hh\\:mm\\:ss}";
            });
        }

        public void GeneratePatches()
        {
            Task.Run(() =>
            {
                Dispatcher.UIThread.Post(() => ElapsedTimeDetails = "Elapsed 00:00:00");
                updateElapsedTimeTimer.Start();
                patchGenStopwatch.Start();

                var message = PatchGeneration.Run(generationInfo.SourceFolderPath,
                    generationInfo.TargetFolderPath, generationInfo.PatchName, Patcher_ProgressChanged);

                patchGenStopwatch.Stop();
                updateElapsedTimeTimer.Stop();

                // The timer ticks once a second, so a short run would otherwise never show its time
                var took = $"Took {patchGenStopwatch.Elapsed:hh\\:mm\\:ss}";
                Dispatcher.UIThread.Post(() => ElapsedTimeDetails = took);

                if(message.ExitCode != PatcherExitCode.Success && generationInfo.AutoClose)
                {
                    PatchLogger.LogInfo("Exiting: Auto close on failure");
                    Environment.Exit((int)message.ExitCode);
                }

                PatchLogger.LogInfo("Printing summary info ...");
                PrintSummary();

                var doneMessage = message.ExitCode == PatcherExitCode.Success ? "Patches generated" : "Generation failed, see the log";

                ProgressMessage = doneMessage;

                PatchGeneration.CopyPatchClient(generationInfo.PatchName);

                if (generationInfo.AutoZip)
                {
                    string archiveName = PatchGeneration.ArchiveName(
                        generationInfo.SourceFolderPath, generationInfo.TargetFolderPath);

                    AddPatchItem($"Zipping to {archiveName}.7z ...");

                    ProgressMessage = $"Zipping {archiveName}.7z";
                    PatchPercent = 0;

                    var zipProgress = new Progress<int>(percent => PatchPercent = percent);

                    string archivePath = PatchGeneration.CompressOutput(
                        generationInfo.PatchName, archiveName, zipProgress);

                    PatchPercent = 100;
                    ProgressMessage = doneMessage;

                    AddPatchItem(archivePath != null
                        ? $"Zipping complete: {archiveName}.7z"
                        : "Zipping failed - see the log");
                }

                if (generationInfo.AutoClose)
                {
                    Environment.Exit((int)PatcherExitCode.Success);
                }
            });
        }

        private void Patcher_ProgressChanged(object Sender, int Progress, int Total, int Percent, string Message = "", params LineItem[] AdditionalLineItems)
        {
            // Fires once per file. Marshalling every one floods the dispatcher on a full install.
            if (!ShouldRefreshUi(Progress >= Total))
            {
                return;
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressMessage = $"Comparing files {Progress:N0} / {Total:N0}";

                PatchPercent = Percent;

                PatchItemCollection.Add(new PatchItem(Message));

                // Bounded so a long run doesn't accumulate thousands of live UI items.
                while (PatchItemCollection.Count > MaxVisiblePatchItems)
                {
                    PatchItemCollection.RemoveAt(0);
                }

                lineItems = AdditionalLineItems;
            });
        }

        /// <summary>
        /// Append a highlighted line to the progress list from a background thread.
        /// </summary>
        private void AddPatchItem(string text)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IBrush brush = null;

                if (Application.Current != null &&
                    Application.Current.Resources.TryGetResource("SPT_Brush_Yellow", null, out var color))
                {
                    brush = color as IBrush;
                }

                PatchItemCollection.Add(new PatchItem("")
                {
                    Name = text,
                    Color = brush ?? PatchItemDefinitions.Colors["exists"]
                });
            });
        }

        private bool ShouldRefreshUi(bool force)
        {
            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref lastUiRefreshMs);

            if (!force && now - last < UiRefreshIntervalMs)
            {
                return false;
            }

            Interlocked.Exchange(ref lastUiRefreshMs, now);

            return true;
        }

        private void PrintSummary()
        {
            Dispatcher.UIThread?.InvokeAsync(() =>
            {
                if (Application.Current.Resources.TryGetResource("SPT_Brush_Yellow", null, out var color))
                {
                    if (color is IBrush brush)
                    {
                        PatchItemCollection.Add(new PatchItem("")
                        {
                            Name = new StringBuilder().AppendLine("Summary").AppendLine("----------").ToString(),
                            Color = brush
                        });

                        foreach (LineItem item in lineItems)
                        {
                            PatchItemCollection.Add(new PatchItem("")
                            {
                                Name = $"{item.ItemText}: {item.ItemValue}",
                                Color = brush
                            });
                        }
                    }
                }
            });
        }
    }

}
