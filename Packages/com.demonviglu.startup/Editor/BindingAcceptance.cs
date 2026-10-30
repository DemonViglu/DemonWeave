using System;

namespace DemonViglu.Startup.Editor
{
    public static class BindingAcceptance
    {
        private sealed class Borrowed : IDisposable
        {
            public bool Disposed;
            public void Dispose() { Disposed = true; }
        }
        private static void Assert(bool value) { if (!value) throw new Exception("Binding assertion failed."); }
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
        public static int Run()
        {
            using (var binding = new ServiceBinding<Borrowed>())
            {
                Throws<InvalidOperationException>(() => binding.Require());
                var a = new Borrowed();
                var first = binding.Bind(a);
                Assert(ReferenceEquals(binding.Require(), a));
                Throws<InvalidOperationException>(() => binding.Bind(a));
                first.Dispose();
                using (var second = binding.Bind(a))
                {
                    first.Dispose();
                    Assert(ReferenceEquals(binding.Current, a));
                }
                Assert(binding.Current == null && !a.Disposed);
            }
            using (var registry = new ServiceRegistry())
            {
                var binding = new ServiceBinding<Borrowed>(); registry.Add(binding, true); registry.Freeze();
                var provider = new Borrowed(); var lease = binding.Bind(provider);
                Throws<InvalidOperationException>(() => registry.Add(new Borrowed()));
                registry.Dispose(); lease.Dispose();
                Assert(binding.Current == null && !provider.Disposed);
                Throws<ObjectDisposedException>(() => binding.Bind(provider));
            }
            return 2;
        }
    }
}
