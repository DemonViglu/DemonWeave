using UnityEngine;

namespace DemonWeave.TriggerBranch
{
    // The only orchestration in this sample. Replace this component with your Quest later.
    public sealed class TriggerBranchDemo : MonoBehaviour
    {
        public PlayerTrigger Zone;
        public RotatingCube RedCube;
        public RotatingCube GreenCube;
        [Min(0)] public float RequiredStaySeconds = 5f;
        public enum Phase { WaitingForEntry, Timing, RedSelected, GreenSelected }
        public Phase CurrentPhase { get; private set; }
        private double enteredAt;

        private void OnEnable()
        {
            if (!Zone || !RedCube || !GreenCube)
            {
                Debug.LogError("Assign Zone, Red Cube and Green Cube.", this);
                enabled = false;
                return;
            }
            Zone.Entered += BeginTiming;
            Zone.Left += ChooseOnExit;
            if (Zone.IsInside) BeginTiming();
        }
        private void OnDisable()
        {
            if (Zone) { Zone.Entered -= BeginTiming; Zone.Left -= ChooseOnExit; }
            if (CurrentPhase == Phase.Timing) CurrentPhase = Phase.WaitingForEntry;
        }
        private void BeginTiming()
        {
            if (CurrentPhase != Phase.WaitingForEntry) return;
            enteredAt = Time.timeAsDouble;
            CurrentPhase = Phase.Timing;
        }
        private void Update()
        {
            if (CurrentPhase == Phase.Timing && Zone.IsInside && Time.timeAsDouble - enteredAt >= RequiredStaySeconds)
                SelectRed();
        }
        private void ChooseOnExit()
        {
            if (CurrentPhase != Phase.Timing) return;
            if (Time.timeAsDouble - enteredAt >= RequiredStaySeconds) SelectRed();
            else
            {
                CurrentPhase = Phase.GreenSelected;
                GreenCube.StartRotating();
            }
        }
        private void SelectRed()
        {
            CurrentPhase = Phase.RedSelected;
            RedCube.StartRotating();
        }
    }
}
