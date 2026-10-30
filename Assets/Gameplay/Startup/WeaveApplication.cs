using DemonViglu.Startup;
using DemonViglu.Startup.Unity;
using DemonWeave.Quests;

namespace DemonWeave.Startup
{
    /// <summary>App-owned local quest service. Scene hosts open/dispose their runs; no scene references live here.</summary>
    [UnityEngine.DefaultExecutionOrder(-10000)]
    public sealed class WeaveApplication : AppHost
    {
        protected override void Configure(ServiceRegistry services) => services.Add(new QuestService(), takeOwnership: true);
        protected override void StopServices() => Events.Publish(new WeaveApplicationStopping());
    }
    public readonly struct WeaveApplicationStopping { }
}
