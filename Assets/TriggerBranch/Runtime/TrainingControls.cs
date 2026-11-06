using System.Collections;
using System.Linq;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DemonWeave.TriggerBranch
{
    [DefaultExecutionOrder(-100)]
    public sealed class TrainingControls : MonoBehaviour
    {
        public QuestSceneHost Training;
        public QuestSceneHost Followup;
        public QuestWorldState State;
        public QuestWorldFact TrainingAllowed;
        public QuestWorldFact TrainingAborted;
        public TrainingAcknowledgementEntity Acknowledgement;
        public SimplePlayerMover Player;
        public RotatingCube[] Cubes;
        private Vector3 spawn;
        private bool restarting;
        private GUIStyle labelStyle, buttonStyle;
        private void Start() => spawn = Player.transform.position;
        public void TogglePause() => State.Set(TrainingAllowed, State.Read(TrainingAllowed.Id) != true);
        public void Interrupt() => State.Set(TrainingAborted, true);
        public void Confirm() { if (Followup.ActiveRun?.Instances.Any(q => q.State == QuestStatus.Active && !q.IsPaused && q.Stages.Any(n => n.State == QuestStatus.Active && !n.IsPaused)) == true) Acknowledgement.Confirm(); }
        public void Restart() { if (!restarting) StartCoroutine(RestartAfterPhysics()); }
        private IEnumerator RestartAfterPhysics()
        {
            restarting = true; Training.StopRun();
            State.Set(TrainingAborted, false); State.Set(TrainingAllowed, true);
            foreach (var cube in Cubes) cube.ResetRotation();
            var body = Player.GetComponent<Rigidbody>(); body.position = spawn; body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms(); yield return new WaitForFixedUpdate(); yield return null;
            Training.RestartRun(); restarting = false;
        }
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.pKey.wasPressedThisFrame) TogglePause();
            if (keyboard.xKey.wasPressedThisFrame) Interrupt();
            if (keyboard.rKey.wasPressedThisFrame) Restart();
            if (keyboard.spaceKey.wasPressedThisFrame) Confirm();
        }
        private void OnGUI()
        {
            labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 16, fixedHeight = 28 };
            var previousMatrix = GUI.matrix;
            var scale = Mathf.Max(.75f, Screen.width / 1280f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUILayout.BeginArea(new Rect(16, 16, 440, 250), GUI.skin.box);
            GUILayout.Label("WASD: move   P: pause/resume   X: interrupt   R: restart", labelStyle);
            var quest = Training.ActiveRun?.Instances.FirstOrDefault();
            GUILayout.Label("Challenge: " + (quest == null ? "Restarting" : quest.IsPaused || quest.Stages.Any(n => n.State == QuestStatus.Active && n.IsPaused) ? "Paused" : quest.State.ToString()), labelStyle);
            if (quest != null) GUILayout.Label(Training.ExecutionStatus(quest.DefinitionId, "stay"), labelStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(State.Read(TrainingAllowed.Id) == true ? "Pause condition" : "Resume condition", buttonStyle)) TogglePause();
            if (GUILayout.Button("Interrupt", buttonStyle)) Interrupt();
            GUI.enabled = !restarting; if (GUILayout.Button("Restart", buttonStyle)) Restart(); GUI.enabled = true;
            GUILayout.EndHorizontal();
            var followup = Followup.ActiveRun?.Instances.FirstOrDefault();
            GUILayout.Label("Follow-up: " + (followup?.State.ToString() ?? "Not loaded"), labelStyle);
            GUI.enabled = followup?.State == QuestStatus.Active && !followup.IsPaused && followup.Stages.Any(n => n.State == QuestStatus.Active && !n.IsPaused);
            if (GUILayout.Button("Acknowledge training (Space)", buttonStyle)) Confirm(); GUI.enabled = true;
            GUILayout.Label("The follow-up reads the recorded completion of the challenge.", labelStyle);
            GUILayout.EndArea(); GUI.matrix = previousMatrix;
        }
    }
}
