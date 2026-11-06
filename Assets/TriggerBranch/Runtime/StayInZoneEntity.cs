using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.TriggerBranch
{
    public enum StayResult { Stayed, LeftEarly }

    public sealed class StayInZoneEntity : QuestEntity<StayResult>
    {
        [ShowInQuest, SerializeField] private PlayerTrigger zone;
        [ShowInQuest, Min(0.01f), SerializeField] private float seconds = 5f;
        public override string CheckConfiguration()
        {
            if (!zone) return "Zone is missing. Assign the PlayerTrigger component.";
            if (!zone.Player) return "Zone.Player is missing.";
            return !float.IsFinite(seconds) || seconds <= 0 ? "Seconds must be positive." : "";
        }
        public override QuestExecution CreateExecution() => new Execution(zone, seconds);

        // These fields belong to this activation, not to the scene component.
        private sealed class Execution : QuestExecution<StayResult>
        {
            private readonly PlayerTrigger zone;
            private readonly float seconds;
            private bool entered;
            private double enteredAt;
            public Execution(PlayerTrigger zone, float seconds) { this.zone = zone; this.seconds = seconds; }
            protected override void OnStart()
            {
                Status = "Waiting for player to enter";
                zone.Entered += Enter; zone.Left += Leave;
                if (zone.IsInside) Enter();
            }
            private void Enter()
            {
                if (entered) return;
                entered = true; enteredAt = Time.timeAsDouble;
            }
            private void Leave()
            {
                if (entered) Finish(Time.timeAsDouble - enteredAt >= seconds ? StayResult.Stayed : StayResult.LeftEarly);
            }
            protected override void OnTick()
            {
                if (!zone || !zone.isActiveAndEnabled) throw new System.InvalidOperationException("Trigger was removed or disabled.");
                if (!entered) return;
                var elapsed = Time.timeAsDouble - enteredAt;
                Status = $"Inside zone: {elapsed:0.0} / {seconds:0.0} s";
                if (zone.IsInside && elapsed >= seconds) Finish(StayResult.Stayed);
            }
            protected override void OnStop()
            {
                if (zone) { zone.Entered -= Enter; zone.Left -= Leave; }
            }
        }
    }
}
