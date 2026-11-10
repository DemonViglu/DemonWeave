using DemonViglu.Startup.Unity;

namespace DemonViglu.Startup.Sample
{
    // Empty project configuration; AppHost provides immediate and queued events.
    [UnityEngine.DefaultExecutionOrder(-10000)]
    public sealed class BasicComposition : AppHost
    {
        protected override void Configure(DemonViglu.Startup.ServiceRegistry services) { }
    }
}
