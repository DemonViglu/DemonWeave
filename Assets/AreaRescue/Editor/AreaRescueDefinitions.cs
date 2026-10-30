using System.Collections.Generic;
using DemonWeave.Quests;

namespace DemonWeave.AreaRescue.Editor
{
    public static class AreaRescueDefinitions
    {
        public static List<QuestDefinition> Create()
        {
            QuestContent Fact(string id, string label, string role, string key) => new QuestContent {
                Id = id, Label = label, Kind = ContentKind.Fact, Subject = role, Key = key
            };
            return new List<QuestDefinition> { new QuestDefinition {
                Id = "courtyard-rescue", Title = "Break the silence",
                Accept = new QuestCondition { Kind = ConditionKind.Fact, Subject = "zone", Key = "entered" },
                Stages = new List<QuestStage> {
                    new QuestStage { Id = "enter", Title = "Enter the courtyard", Objectives = new List<QuestContent> {
                        Fact("entered", "Reach the marked courtyard", "zone", "entered") } },
                    new QuestStage { Id = "prepare", Title = "Breach the gate", After = new List<string> { "enter" },
                        BeginExec = new List<QuestExec> { new QuestExec { Id = "opening", CapabilityId = "encounter.prepare-opening", ArgumentsJson = "{\"Timeout\":20}" } },
                        Objectives = new List<QuestContent> { Fact("ready", "Wait for the gate and encounter to be ready", "encounter", "prepared") } },
                    new QuestStage { Id = "clear", Title = "Defeat the guards", After = new List<string> { "prepare" },
                        Objectives = new List<QuestContent> { Fact("guards", "Defeat the guards assigned to this encounter", "encounter", "cleared") } },
                    new QuestStage { Id = "defeat", Title = "遭遇战败", Required = false,
                        OpenWhen = new QuestCondition { Kind = ConditionKind.Fact, Subject = "encounter", Key = "player-defeated" },
                        Objectives = new List<QuestContent> { Fact("defeated", "本次遭遇中玩家死亡", "encounter", "player-defeated") },
                        FinishExec = new List<QuestExec> { new QuestExec { Id = "recover", CapabilityId = "checkpoint.restore" } } },
                    new QuestStage { Id = "rescue", Title = "Free your companion", After = new List<string> { "clear" },
                        BeginExec = new List<QuestExec> { new QuestExec { Id = "allow-release", CapabilityId = "companion.allow-rescue" } },
                        Objectives = new List<QuestContent> {
                            Fact("freed", "Release the captured companion", "encounter", "rescued"),
                            Fact("spoken", "Finish speaking with your companion", "encounter", "dialogue-finished") } }
                }
            } };
        }
    }
}
