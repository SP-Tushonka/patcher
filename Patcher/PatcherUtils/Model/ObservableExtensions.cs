using System;

namespace PatcherUtils.Model
{
    public static class ObservableExtensions
    {
        /// <summary>
        /// Subscribe with no handler, to run a cold observable for its side effect.
        /// </summary>
        /// <remarks>
        /// ReactiveUI commands do nothing until subscribed, and System.Reactive's parameterless
        /// Subscribe() is gone as of ReactiveUI 24.
        /// </remarks>
        public static void Run<T>(this IObservable<T> source) => source.Subscribe(NoOpObserver<T>.Instance);

        private sealed class NoOpObserver<T> : IObserver<T>
        {
            public static readonly NoOpObserver<T> Instance = new NoOpObserver<T>();

            public void OnNext(T value)
            {
            }

            public void OnError(Exception error) => PatchLogger.LogException(error);

            public void OnCompleted()
            {
            }
        }
    }
}
