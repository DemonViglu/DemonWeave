using System.Linq;
using DemonWeave.Editor;
using DemonWeave.Quests;
using DemonWeave.Unity;
using Newtonsoft.Json.Linq;
namespace DemonWeave.AreaRescue.Editor
{
    public sealed class RescueAuthoringProfile : QuestAuthoringProfile
    {
        public override bool Supports(IQuestAuthoringHost host) => host.BindingEntries.Any(b => b.Entity is RescueEncounter);
        public override string ProducerFor(IQuestAuthoringHost host, QuestCondition condition) => condition.Kind==ConditionKind.Fact&&condition.Expected&&new[]{"prepared","cleared","player-defeated"}.Contains(condition.Key)?"rescue.prepare":null;
        public override bool Produces(IQuestAuthoringHost host, QuestExec action, QuestCondition condition)
        {
            if(action.CapabilityId!=ProducerFor(host,condition))return false;
            try{return (string)JObject.Parse(action.ArgumentsJson)[CapabilityCatalog.TargetKey]==condition.Subject;}catch{return false;}
        }
    }
}
