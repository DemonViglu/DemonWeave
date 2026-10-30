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
    public sealed class RescuePresentation : MonoBehaviour, IDialogueCommands
    {
        public AreaRescueScenario Scenario;
        public RescuePlayerController PlayerInput;
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
            Scenario.AttemptEnding += CloseDialogue;
            Retry.onClick.AddListener(Scenario.RestoreCheckpoint);
        }
        private void Update()
        {
            Objective.transform.parent.gameObject.SetActive(!Ui.IsShown(DialogueId));
            Status.text = "HEALTH  " + Scenario.Player.Health + "/" + Scenario.Player.MaxHealth + "    •    GUARDS  "
                + (Scenario.Spawner.Attempt?.ConfirmedDeaths ?? 0) + "/" + Scenario.Encounter.EnemyCount;
            Feedback.text = Scenario.Feedback;
            var quest = Scenario.Run?.Instances.FirstOrDefault();
            var stage = quest?.Stages.FirstOrDefault(s => s.State == QuestStatus.Active);
            Objective.text = quest == null ? (Scenario.Player.IsDead && !Scenario.Recovering ? "Defeated — retry when ready" : "Restoring checkpoint…") : quest.Title + "\n" + (stage?.Title ?? (quest.State == QuestStatus.Completed ? "Companion rescued" : "Enter the courtyard"));
            Retry.interactable = !Scenario.Recovering;
        }
        public void OpenRescue()
        {
            if (Scenario.Recovering || !Scenario.RescueAvailable || !Scenario.Companion || Scenario.DialogueFinished || Ui.IsShown(DialogueId)) return;
            var attempt = Scenario.AttemptId;
            if (!Ui.Show(DialogueId) || !Ui.TryGetView(DialogueId, out var view)) return;
            try
            {
                if (((DialoguePanel)view).Begin(RescueDialogue, null, this, result => {
                    Ui.CloseOrReturn(DialogueId);
                    if (result == DialogueState.Completed) Scenario.FinishRescueDialogue(attempt);
                })) return;
            }
            catch { Ui.CloseOrReturn(DialogueId); throw; }
            Ui.CloseOrReturn(DialogueId);
        }
        public bool TryExecute(string commandId, out string reason)
        {
            reason = "The companion is unavailable.";
            if (commandId != "companion.release" || Scenario.Recovering || !Scenario.Companion) return false;
            bool result = Scenario.Rescued || Scenario.BeginRescue(Scenario.AttemptId);
            if (result) reason = "";
            return result;
        }
        private void CloseDialogue() { if (Ui) Ui.CloseOrReturn(DialogueId); }
        private void OnDisable()
        {
            if (PlayerInput) PlayerInput.RescueRequested -= OpenRescue;
            if (Scenario) Scenario.AttemptEnding -= CloseDialogue;
            CloseDialogue(); Retry?.onClick.RemoveAllListeners();
        }
    }
}
