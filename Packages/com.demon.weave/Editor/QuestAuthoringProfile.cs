using DemonWeave.Quests;
using DemonWeave.Unity;
namespace DemonWeave.Editor
{
    // Optional graph provenance only. Component internals are not another node parameter surface.
    public abstract class QuestAuthoringProfile
    {
        public abstract bool Supports(IQuestAuthoringHost host);
        public virtual string ProducerFor(IQuestAuthoringHost host, QuestCondition condition) => null;
        public virtual bool Produces(IQuestAuthoringHost host, QuestExec action, QuestCondition condition) => action.CapabilityId == ProducerFor(host, condition);
    }
}
