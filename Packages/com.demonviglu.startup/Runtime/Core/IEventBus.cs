using System;

namespace DemonViglu.Startup
{
    public interface IEventBus
    {
        void Publish<T>(T message);
        IDisposable Subscribe<T>(Action<T> handler);
        bool Unsubscribe<T>(Action<T> handler);
    }
}
