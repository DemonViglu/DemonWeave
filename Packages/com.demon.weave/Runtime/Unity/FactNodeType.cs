using System.Collections.Generic;
using DemonWeave.Quests;
using UnityEngine;

namespace DemonWeave.Unity
{
    [CreateAssetMenu(menuName = "DemonWeave/Node Types/Wait for Fact", fileName = "WaitForFact")]
    public sealed class FactNodeType : QuestNodeType
    {
        public string Fact = "";
        public bool Expected = true;
        protected override QuestStage BuildDefinition() => new QuestStage {
            Title = DisplayName,
            Objectives = new List<QuestContent> { new QuestContent {
                Id = "objective", Label = DisplayName, Kind = ContentKind.Fact, Key = Fact, Expected = Expected
            } }
        };
    }
}
