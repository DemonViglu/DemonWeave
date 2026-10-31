using System.Linq;
using DemonViglu.Dialogue;
using DemonViglu.Dialogue.Unity;
using DemonViglu.UI;
using DemonWeave.Presentation;
using DemonWeave.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace DemonWeave.AreaRescue
{
    public sealed class RescueHud : MonoBehaviour, IDialogueCommands
    {
        public RescueEncounter Encounter;
        public RescueCheckpoint Checkpoint;
        public RescueInput PlayerInput;
        public UiRoot Ui;
        public UiCatalog Catalog;
        public DialogueAsset RescueDialogue;
        public Text Objective;
        public Text Status;
        public Text Feedback;
        public Button Retry;
        public const string DialogueId = "rescue.dialogue";
        private void Start()
        {
            if (!Ui.Initialize(Catalog)) { enabled = false; return; }
            PlayerInput.RescueRequested += OpenRescue;
            Encounter.Ended += CloseDialogue;
            Retry.onClick.AddListener(Checkpoint.Restore);
        }
        private void Update()
        {
            Objective.transform.parent.gameObject.SetActive(!Ui.IsShown(DialogueId));
            Status.text = "HEALTH  " + Encounter.Player.Health + "/" + Encounter.Player.MaxHealth + "    •    GUARDS  "
                + (Encounter.Spawner.Attempt?.ConfirmedDeaths ?? 0) + "/" + Encounter.Definition.EnemyCount;
            Feedback.text = (string.IsNullOrEmpty(Checkpoint.Feedback) ? Encounter.Feedback : Checkpoint.Feedback);
            var quest = Checkpoint.Quest.ActiveRun?.Instances.FirstOrDefault();
            var stage = quest?.Stages.FirstOrDefault(s => s.State == QuestStatus.Active);
            Objective.text = quest == null ? (Encounter.Player.IsDead && !Checkpoint.Recovering ? "Defeated — retry when ready" : Checkpoint.Recovering ? "Restoring checkpoint…" : "No active quest") : quest.Title + "\n" + (stage?.Title ?? (quest.State == QuestStatus.Completed ? "Companion rescued" : "Enter the courtyard"));
            Retry.interactable = !Checkpoint.Recovering;
        }
        public void OpenRescue()
        {
            if (Checkpoint.Recovering || !Encounter.CanRescue || !Encounter.Companion || Encounter.IsDialogueFinished() || Ui.IsShown(DialogueId)) return;
            var attempt = Encounter.AttemptId;
            if (!Ui.Show(DialogueId) || !Ui.TryGetView(DialogueId, out var view)) return;
            try
            {
                if (((DialoguePanel)view).Begin(RescueDialogue, null, this, result => {
                    Ui.CloseOrReturn(DialogueId);
                    if (result == DialogueState.Completed) Encounter.FinishDialogue(attempt);
                })) return;
            }
            catch { Ui.CloseOrReturn(DialogueId); throw; }
            Ui.CloseOrReturn(DialogueId);
        }
        public bool TryExecute(string commandId, out string reason)
        {
            reason = "The companion is unavailable.";
            if (commandId != "companion.release" || Checkpoint.Recovering || !Encounter.Companion) return false;
            bool result = Encounter.IsCompanionRescued() || Encounter.ReleaseCompanion(Encounter.AttemptId);
            if (result) reason = "";
            return result;
        }
        private void CloseDialogue() { if (Ui) Ui.CloseOrReturn(DialogueId); }
        private void OnDisable()
        {
            if (PlayerInput) PlayerInput.RescueRequested -= OpenRescue;
            if (Encounter) Encounter.Ended -= CloseDialogue;
            CloseDialogue(); Retry?.onClick.RemoveAllListeners();
        }
    }
}


