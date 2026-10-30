using System;
using System.IO;
using System.Linq;
using DemonWeave.Editor;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DemonWeave.Messenger.Editor
{
    [InitializeOnLoad]
    public static class MessengerVerification
    {
        private const string Flag = "DemonWeave.VerifyMessenger";
        private const string Output = "Work/TEMP/messenger-stage-20261029";
        private static int frames;
        static MessengerVerification() { if (SessionState.GetBool(Flag, false)) EditorApplication.update += Tick; }
        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(Output);
                EditorSceneManager.OpenScene(MessengerSetup.ScenePath);
                var host = UnityEngine.Object.FindAnyObjectByType<MessengerScenario>();
                var book = ScriptableObject.CreateInstance<QuestBookAsset>();
                try
                {
                    book.Id = "offline-messenger"; book.Quests = MessengerSetup.Definitions(false);
                    var before = book.ExportDefinitions();
                    var review = QuestProposalReview.Prepare(book, host, MessengerProposalExample.Create(book));
                    Check(before == book.ExportDefinitions(), "review cannot mutate author data");
                    QuestProposalReview.Apply(review, host);
                    File.WriteAllText(Output + "/adopted.json", book.ExportDefinitions());
                    File.WriteAllText(Output + "/proposal-review.txt", "OFFLINE FIXTURE, NOT LIVE MODEL\n" + review.Diff);
                }
                finally { UnityEngine.Object.DestroyImmediate(book); }
                SessionState.SetBool(Flag, true); EditorApplication.isPlaying = true;
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        private static void Tick()
        {
            if (!EditorApplication.isPlaying || ++frames < 20) return;
            EditorApplication.update -= Tick;
            try { Verify(); Finish(true, "PASS: actual Unity negotiation/refusal/infiltration/handover; once-only cost; offline reviewed stage patch executes; no hidden child quests; Startup cleanup. No live model or human UX claim."); }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        private static void Verify()
        {
            var original = UnityEngine.Object.FindAnyObjectByType<MessengerScenario>();
            Check(original.Run != null, "sample starts: " + original.Feedback);
            var app = original.ApplicationHost; var service = app.Kernel.App.Services.Get<QuestService>();
            original.Close(); original.gameObject.SetActive(false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MessengerSetup.PrefabPath);
            void Variant(string name, bool success, bool adopted, Action<MessengerScenario> verify)
            {
                var root = UnityEngine.Object.Instantiate(prefab); var host = root.GetComponent<MessengerScenario>();
                var book = ScriptableObject.CreateInstance<QuestBookAsset>();
                book.Quests = adopted ? QuestBookAsset.ParseDefinitions(File.ReadAllText(Output + "/adopted.json")) : MessengerSetup.Definitions();
                host.Book = book; host.ApplicationHost = app; host.NegotiationSucceeds = success;
                try { host.StartScenario(); Check(host.Run != null, host.Feedback); verify(host); File.WriteAllLines(Output + "/" + name + ".txt", host.Activity); }
                finally { host.Close(); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(book); }
            }
            void Investigate(MessengerScenario host)
            {
                Click(host, "contact"); Click(host, "clue-b");
                Check(host.Run.Find("missing-envoy").Stage("rescue").State == QuestStatus.Waiting, "both clues required");
                Click(host, "clue-a");
                Check(host.Run.Instances.Count == 1 && host.Run.Find("missing-envoy").Stages.Count == 3, "one quest with three native stages");
                Check(host.Run.Find("missing-envoy").Stage("rescue").SelectedRouteId == "", "available route not automatically selected");
                Check(!host.SelectRoute("missing-envoy", "rescue", "infiltrate"), "infiltration unavailable before refusal");
            }
            void Handover(MessengerScenario host)
            {
                Check(host.ReadFact("envoy", "safe") == true && host.Run.Find("missing-envoy").State == QuestStatus.Active, "safety alone is not handover");
                Click(host, "contact");
                Check(host.ReadFact("envoy", "handed-over") == true && host.Run.Find("missing-envoy").State == QuestStatus.Completed, "actual parenting completes handover");
            }
            Variant("negotiation", true, false, host => {
                Investigate(host); Check(host.SelectRoute("missing-envoy", "rescue", "negotiate"), "select negotiation");
                Check(!host.GuardHostile && host.Coins == 8, "peaceful release with actual cost"); Handover(host);
            });
            Variant("changed-object-and-parameter", true, false, host => {
                host.Close();
                var binding = host.Bindings.Single(b => b.Role == "envoy"); var old = binding.Entity;
                var substitute = UnityEngine.Object.Instantiate(old.gameObject, old.transform.parent).GetComponent<QuestEntity>();
                substitute.DisplayName = "Replacement envoy"; binding.Entity = substitute;
                host.Book.Quests[0].Stages.Single(s => s.Id == "rescue").Routes.Single(r => r.Id == "negotiate").SelectedExec[0].ArgumentsJson = "{\"Deposit\":4}";
                host.StartScenario(); Investigate(host); host.SelectRoute("missing-envoy", "rescue", "negotiate");
                Check(host.Coins == 6 && Vector3.Distance(old.transform.position, host.SafePoint.position) > .5f, "edited parameter and replacement binding affect actual world only");
                Handover(host);
            });
            Variant("refusal-infiltration-adopted", false, true, host => {
                Investigate(host); Check(host.SelectRoute("missing-envoy", "rescue", "negotiate"), "select negotiation");
                Check(host.NegotiationRejected && host.ReadFact("envoy", "safe") == false, "successful conversation does not fake rescue");
                host.SelectRoute("missing-envoy", "rescue", "negotiate"); Check(host.Coins == 8, "repeat choice cannot charge twice");
                Check(host.SelectRoute("missing-envoy", "rescue", "infiltrate"), "reviewed switch allows infiltration");
                Check(host.GuardHostile && host.Coins == 8, "actual hostility and retained cost");
                var stage = host.Run.Find("missing-envoy").Stage("rescue");
                Check(stage.ChoiceHistory.Count == 2 && !host.SelectRoute("missing-envoy", "rescue", "negotiate"), "closed route cannot revive");
                Check(!host.Run.ReportRouteEvent("missing-envoy", "rescue", "negotiate", 1, new QuestWorldEvent(Guid.NewGuid(), "envoy", "interacted")), "old route callback rejected");
                Click(host, "tunnel"); Handover(host);
            });
            Check(service.ActiveRunCount == 0, "closed worlds release all runs");
        }
        private static void Click(MessengerScenario host, string role) => host.Interact(host.Bindings.Single(b => b.Role == role).Entity);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Finish(bool success, string message)
        {
            SessionState.SetBool(Flag, false); Directory.CreateDirectory(Output); File.WriteAllText(Output + "/result.txt", message);
            if (success) Debug.Log(message); else Debug.LogError(message);
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
