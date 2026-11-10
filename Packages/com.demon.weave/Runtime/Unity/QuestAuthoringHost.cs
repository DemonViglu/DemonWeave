using System.Collections.Generic;
using DemonWeave.Quests;
namespace DemonWeave.Unity
{
    public interface IQuestAuthoringHost
    {
        QuestBookAsset BookAsset { get; set; }
        List<ObjectBinding> BindingEntries { get; }
        QuestRun ActiveRun { get; }
        IReadOnlyList<string> Activity { get; }
    }
}
