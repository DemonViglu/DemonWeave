using System;
using DemonWeave.Unity;
using DemonWeave.World;
using UnityEngine;

namespace DemonWeave.AreaRescue
{
    public enum OpeningResult { Ready, Defeated }
    public sealed class RescueOpeningEntity : QuestEntity<OpeningResult>
    {
        [SerializeField] private RescueEncounter encounter;
        [ShowInQuest, SerializeField] private GameObject door;
        [ShowInQuest, Min(.1f), SerializeField] private float timeout = 20;
        public override string CheckConfiguration() => encounter ? encounter.ValidateOpening(door, timeout) : "Encounter is missing.";
        public override QuestExecution CreateExecution() => new Execution(encounter, door, timeout);
        private sealed class Execution : QuestExecution<OpeningResult>
        {
            private readonly RescueEncounter encounter;
            private readonly GameObject door;
            private readonly float timeout;
            private bool handedOff;
            private Guid attempt;
            public Execution(RescueEncounter encounter, GameObject door, float timeout)
            { this.encounter = encounter; this.door = door; this.timeout = timeout; }
            protected override void OnStart()
            {
                var result = encounter.Prepare(door, timeout);
                if (!result.Success) throw new InvalidOperationException(result.Message);
                attempt = encounter.AttemptId;
            }
            protected override void OnTick()
            {
                if (!encounter || encounter.AttemptId != attempt) throw new InvalidOperationException("Encounter attempt ended.");
                Status = encounter.Feedback;
                if (encounter.IsPlayerDefeated()) { handedOff = true; Finish(OpeningResult.Defeated); }
                else if (encounter.Opening.State == TimelineOperationState.Failed) throw new InvalidOperationException(encounter.Opening.Error);
                else if (encounter.IsPrepared()) { handedOff = true; Finish(OpeningResult.Ready); }
            }
            protected override void OnStop()
            {
                if (!handedOff && encounter && attempt != Guid.Empty && encounter.AttemptId == attempt) encounter.Clear();
            }
        }
    }
}
