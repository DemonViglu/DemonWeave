using System;
using System.IO;
using System.Linq;
using DemonWeave.Presentation;
using DemonWeave.Quests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DemonWeave.Messenger.Editor
{
    [InitializeOnLoad]
    public static class MessengerProductVerification
    {
        private const string Flag = "DemonWeave.ProductVerify";
        private static int frames;
        static MessengerProductVerification() { if (SessionState.GetBool(Flag, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            EditorSceneManager.OpenScene(MessengerProductSetup.ScenePath);
            SessionState.SetBool(Flag, true); EditorApplication.isPlaying = true;
        }
        private static void Tick()
        {
            if (!EditorApplication.isPlaying || ++frames < 30) return;
            EditorApplication.update -= Tick;
            try { Verify(); End(true, "PASS: actual UiRoot stack and visible close button, dialogue commands, cancellation before accept, stale choice ignored, journal focus restored, world consequences and separate handover. Physical keyboard routing remains unverified."); }
            catch (Exception e) { End(false, e.ToString()); }
        }
        private static void Verify()
        {
            var p = UnityEngine.Object.FindAnyObjectByType<MessengerPresentation>(); var world = p.Scenario;
            Check(world.Run != null, "Quest starts");
            DialoguePanel Dialogue() { p.Ui.TryGetView(MessengerPresentation.DialogueId, out var view); return (DialoguePanel)view; }
            Button Choice(int index) => Dialogue().Choices.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy).ElementAt(index);
            Check(p.OpenConversation("contact") && p.Ui.BlocksGameplayInput, "contact modal blocks world input");
            Dialogue().CancelButton.onClick.Invoke(); Check(!world.Accepted && !p.Ui.BlocksGameplayInput, "cancel is not acceptance");
            p.OpenConversation("contact"); Choice(0).onClick.Invoke(); Check(world.Accepted, "explicit option accepts");
            foreach (var role in new[] { "clue-a", "clue-b" }) world.Interact(world.Bindings.Single(b => b.Role == role).Entity);
            p.OpenJournal(); Check(p.Ui.IsShown(MessengerPresentation.JournalId), "journal opens");
            p.OpenConversation("guard");
            p.Ui.TryGetView(MessengerPresentation.JournalId, out var journal);
            Check(!journal.IsFocused && Dialogue().IsFocused && !Choice(1).interactable, "modal owns focus and unavailable route disabled");
            var oldButton = Choice(0); oldButton.onClick.Invoke();
            Check(world.NegotiationRejected && world.Coins == 8, "actual negotiation command");
            oldButton.onClick.Invoke(); Check(world.Coins == 8, "stale button cannot pay twice");
            Check(Choice(0).interactable, "Quest enables infiltration after refusal"); Choice(0).onClick.Invoke();
            Check(world.GuardHostile && journal.IsFocused && p.Ui.BlocksGameplayInput, "world hostility and underlying journal focus restored");
            p.Ui.Back(); Check(!p.Ui.BlocksGameplayInput, "journal Back restores world input");
            world.Interact(world.Bindings.Single(b => b.Role == "tunnel").Entity);
            Check(world.Run.Find("missing-envoy").State == QuestStatus.Active, "rescue is not quest completion");
            p.OpenConversation("contact"); Check(!Choice(0).interactable && Choice(1).interactable, "handover is a separate fact-gated choice"); Choice(1).onClick.Invoke();
            Check(world.ReadFact("envoy", "handed-over") == true && world.Run.Find("missing-envoy").State == QuestStatus.Completed, "dialogue command causes actual safe handover");
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void End(bool success, string text)
        {
            SessionState.SetBool(Flag, false); Directory.CreateDirectory("Work/TEMP"); File.WriteAllText("Work/TEMP/product-result.txt", text);
            if (success) Debug.Log(text); else Debug.LogError(text); EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
