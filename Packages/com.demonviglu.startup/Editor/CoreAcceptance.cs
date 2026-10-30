using System;
namespace DemonViglu.Startup.Editor
{
    public static class CoreAcceptance
    {
        private sealed class Owned : IDisposable { public int Releases; public void Dispose() => Releases++; }
        public static int Run()
        {
            using var bus = new EventBus();
            int calls = 0;
            var subscription = bus.Subscribe<int>(value => calls += value);
            bus.Publish(2); subscription.Dispose(); bus.Publish(2);
            if (calls != 2) throw new Exception("Event subscription was not released.");
            var services = new ServiceRegistry(); var owned = new Owned();
            services.Add(owned, true); services.Add<IEventBus>(bus); services.Freeze();
            if (!ReferenceEquals(services.Get<Owned>(), owned)) throw new Exception("Registry changed service identity.");
            bool rejected = false;
            try { services.Add(new Owned()); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Registry accepted registration after composition.");
            services.Dispose(); services.Dispose();
            if (owned.Releases != 1) throw new Exception("Owned service must be released once.");
            return 4;
        }
    }
}
