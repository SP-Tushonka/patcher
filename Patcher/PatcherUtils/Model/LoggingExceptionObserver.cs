using System;

namespace PatcherUtils.Model
{
    /// <summary>
    /// Routes unhandled ReactiveUI exceptions to the patch log.
    /// </summary>
    public class LoggingExceptionObserver : IObserver<Exception>
    {
        public void OnNext(Exception value) => PatchLogger.LogException(value);

        public void OnError(Exception error) => PatchLogger.LogException(error);

        public void OnCompleted()
        {
        }
    }
}
