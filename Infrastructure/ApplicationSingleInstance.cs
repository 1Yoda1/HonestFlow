using System;
using System.Threading;

namespace HonestFlow.Infrastructure
{
    public sealed class ApplicationSingleInstance : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _disposed;

        private ApplicationSingleInstance(Mutex mutex)
        {
            _mutex = mutex;
        }

        public static bool TryAcquire(string name, out ApplicationSingleInstance instance)
        {
            var mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                instance = null;
                return false;
            }

            instance = new ApplicationSingleInstance(mutex);
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                _mutex.ReleaseMutex();
            }
            finally
            {
                _mutex.Dispose();
            }
        }
    }
}
