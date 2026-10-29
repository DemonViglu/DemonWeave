using DemonViglu.Startup;
using DemonViglu.Startup.Unity;
using DemonWeave.Quests;

namespace DemonWeave.Startup
{
    /// <summary>App-owned local quest service. Scene hosts open/dispose their runs; no scene references live here.</summary>
    public sealed class QuestComposition : AppComposition
    {
        public override void Configure(ServiceRegistry services) => services.Add(new QuestService(), takeOwnership: true);
    }
}
