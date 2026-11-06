using System;
using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.AreaRescue
{
    public enum CompanionResult { Rescued, Defeated }
    public sealed class RescueCompanionEntity : QuestEntity<CompanionResult>
    {
        [SerializeField] private RescueEncounter encounter;
        public override string CheckConfiguration() => encounter && encounter.Player ? "" : "Encounter or Player is missing.";
        public override QuestExecution CreateExecution() => new Execution(encounter);
        private sealed class Execution : QuestExecution<CompanionResult>
        {
            private readonly RescueEncounter encounter;
            private bool handedOff;
            private readonly Guid attempt;
            public Execution(RescueEncounter encounter) { this.encounter = encounter; attempt = encounter.AttemptId; }
            protected override void OnStart()
            {
                var result = encounter.EnableRescue();
                if (!result.Success) throw new InvalidOperationException(result.Message);
            }
            protected override void OnTick()
            {
                if (!encounter || encounter.AttemptId != attempt) throw new InvalidOperationException("Encounter attempt ended.");
                Status = encounter.IsCompanionRescued() ? "Waiting for rescue dialogue to finish" : "Waiting for player to rescue the companion";
                if (encounter.IsPlayerDefeated()) { handedOff = true; Finish(CompanionResult.Defeated); }
                else if (encounter.IsDialogueFinished()) { handedOff = true; Finish(CompanionResult.Rescued); }
            }
            protected override void OnStop() { if (!handedOff && encounter && encounter.AttemptId == attempt) encounter.Clear(); }
        }
    }
}
