using System;
namespace DemonViglu.Startup
{
    internal sealed class Release : IDisposable
    {
        private Action action;
        public Release(Action action) { this.action = action; }
        public void Dispose() { action?.Invoke(); action = null; }
    }
}
