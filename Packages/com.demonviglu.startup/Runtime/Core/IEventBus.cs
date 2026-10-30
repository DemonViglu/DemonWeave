using System;

namespace DemonViglu.Startup
{
    public interface IEventBus
    {
        void Publish<T>(T message);
        IDisposable Post<T>(T message);
        bool TryPost<T>(T message, out IDisposable cancellation);
        int Flush(int maxMessages = 128);
        int PendingCount { get; }
        IDisposable Subscribe<T>(Action<T> handler);
        bool Unsubscribe<T>(Action<T> handler);
    }
}
