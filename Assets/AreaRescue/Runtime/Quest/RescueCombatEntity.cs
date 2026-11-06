using System;
using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.AreaRescue
{
    public enum CombatResult { Cleared, Defeated }
    public sealed class RescueCombatEntity : QuestEntity<CombatResult>
    {
        [SerializeField] private RescueEncounter encounter;
        public override string CheckConfiguration() => encounter && encounter.Player ? "" : "Encounter or Player is missing.";
        public override QuestExecution CreateExecution() => new Execution(encounter);
        private sealed class Execution : QuestExecution<CombatResult>
        {
            private readonly RescueEncounter encounter;
            private bool handedOff;
            private readonly Guid attempt;
            public Execution(RescueEncounter encounter) { this.encounter = encounter; attempt = encounter.AttemptId; }
            protected override void OnTick()
            {
                if (!encounter || encounter.AttemptId != attempt) throw new InvalidOperationException("Encounter attempt ended.");
                Status = $"Guards defeated: {encounter.Spawner.Attempt?.ConfirmedDeaths ?? 0} / {encounter.Definition.EnemyCount}";
                if (encounter.IsPlayerDefeated()) { handedOff = true; Finish(CombatResult.Defeated); }
                else if (encounter.AreGuardsDefeated()) { handedOff = true; Finish(CombatResult.Cleared); }
            }
            protected override void OnStop() { if (!handedOff && encounter && encounter.AttemptId == attempt) encounter.Clear(); }
        }
    }
}
