using System;
using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.TriggerBranch
{
    public enum AcknowledgementResult { Confirmed }
    public sealed class TrainingAcknowledgementEntity : QuestEntity<AcknowledgementResult>
    {
        private event Action confirmed;
        public void Confirm() => confirmed?.Invoke();
        public override QuestExecution CreateExecution() => new Execution(this);
        private sealed class Execution : QuestExecution<AcknowledgementResult>
        {
            private readonly TrainingAcknowledgementEntity entity;
            public Execution(TrainingAcknowledgementEntity entity) => this.entity = entity;
            protected override void OnStart() { Status = "Press Space or Acknowledge"; entity.confirmed += Confirm; }
            private void Confirm() { if (!IsPaused) Finish(AcknowledgementResult.Confirmed); }
            protected override void OnStop() { if (entity) entity.confirmed -= Confirm; }
        }
    }
}
