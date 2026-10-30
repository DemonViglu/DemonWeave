using System;
using System.IO;
using System.Linq;
using DemonWeave.Encounters;
using DemonWeave.Presentation;
using DemonWeave.Quests;
using DemonWeave.World;
using DemonWeave.Unity;
using DemonWeave.Startup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UnityEngine.EventSystems;
using Button = UnityEngine.UI.Button;

namespace DemonWeave.AreaRescue.Editor
{
    [InitializeOnLoad]
    public static class AreaRescueVerification
    {
        private const string Flag = "DemonWeave.AreaRescueVerification";
        private static int step;
        private static int dialoguePhase, dialogueFrame;
        private static AreaRescueWalkProbe walking;
        private static float deadline, closedDoorY;
        private static Guid firstAttempt;
        private static EncounterAttempt oldRoster;
        private static AreaRescueScenario host;
        static AreaRescueVerification() { if (SessionState.GetBool(Flag, false)) EditorApplication.update += Tick; }
        public static void Run()
        { Prepare(false); }
        public static void RunEdited()
        { Prepare(true); }
        public static void RunManualRecovery() { Prepare(false, true); }
        public static void PreviewDialogue() { Prepare(false, false, true); }
        public static void CheckAssetEntry()
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var book = AssetDatabase.LoadAssetAtPath<QuestBookAsset>("Assets/AreaRescue/Content/RescueQuest.asset");
                Check(AssetDatabase.OpenAsset(book), "Quest asset open callback");
                var window = Resources.FindObjectsOfTypeAll<DemonWeave.Editor.QuestEditorWindow>().Single();
                Check(window.rootVisualElement.Query<UnityEngine.UIElements.Label>().ToList().Any(label => label.text == book.Title), "Asset opens its own editor without a scene host");
                window.Close(); Directory.CreateDirectory("Work/TEMP"); File.WriteAllText("Work/TEMP/asset-entry-result.txt", "PASS: QuestBook asset opens the matching editor in an empty scene."); EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogError(exception); EditorApplication.Exit(1); }
        }
        private static void Prepare(bool edited, bool manual = false, bool preview = false)
        {
            EditorSceneManager.OpenScene(AreaRescueSetup.ScenePath);
            SessionState.SetBool(Flag + ".edited", edited);
            SessionState.SetBool(Flag + ".manual", manual); SessionState.SetBool(Flag + ".preview", preview);
            if (manual)
            {
                var scenario = UnityEngine.Object.FindAnyObjectByType<AreaRescueScenario>();
                scenario.Book = UnityEngine.Object.Instantiate(scenario.Book);
                scenario.Book.Quests.Single().Stages.Single(s => s.Id == "defeat").FinishExec.Single().CapabilityId = "checkpoint.wait-retry";
            }
            if (edited)
            {
                var scenario = UnityEngine.Object.FindAnyObjectByType<AreaRescueScenario>();
                var definition = UnityEngine.Object.Instantiate(scenario.Encounter); definition.EnemyCount = 4; scenario.Encounter = definition;
                var presentation = UnityEngine.Object.FindAnyObjectByType<RescuePresentation>();
                presentation.RescueDialogue = UnityEngine.Object.Instantiate(presentation.RescueDialogue);
                presentation.RescueDialogue.Lines[0].Text = "Verification: replacement dialogue is in use.";
                var binding = scenario.Bindings.Single(b => b.Role == scenario.DoorRole);
                var original = binding.Entity; original.name = "Verification original door";
                var replacement = UnityEngine.Object.Instantiate(original.gameObject); replacement.name = "Verification replacement door";
                replacement.transform.position += Vector3.right * 5; binding.Entity = replacement.GetComponent<QuestEntity>();
                SessionState.SetString(Flag + ".quest", scenario.Book.ExportDefinitions());
            }
            SessionState.SetBool(Flag, true); EditorApplication.isPlaying = true;
        }
        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                if (!host)
                {
                    host = UnityEngine.Object.FindAnyObjectByType<AreaRescueScenario>();
                    if (!host || host.Run == null) return;
                    deadline = Time.realtimeSinceStartup + 35;
                    closedDoorY = host.Bindings.Single(b => b.Role == host.DoorRole).Entity.transform.localPosition.y;
                    firstAttempt = host.AttemptId;
                    walking = new GameObject("Verification movement").AddComponent<AreaRescueWalkProbe>(); walking.Host = host;
                }
                if (Time.realtimeSinceStartup > deadline) throw new Exception("Timed out at step " + step + ": " + host.Feedback + "; Timeline=" + host.Timeline.State + ", release=" + host.Timeline.ReleaseRequested + ", ready=" + host.Spawner.IsReady + ", entered=" + host.HasEntered + ", player=" + host.Player.transform.position);
                var presentation = UnityEngine.Object.FindAnyObjectByType<RescuePresentation>();
                if (SessionState.GetBool(Flag + ".manual", false))
                {
                    if (step == 0)
                    {
                        WalkIn();
                        if (!host.HasEntered || host.Spawner.Attempt == null) return;
                        walking.Walking = false;
                        host.Player.Damage(100); Check(!host.Recovering && host.Run == null, "Authored defeat branch waits instead of auto-restoring");
                        Click(presentation.Retry); step = 1; return;
                    }
                    if (host.Recovering || host.Run == null) return;
                    Check(host.Player.Health == host.Player.MaxHealth && !host.HasEntered, "Manual retry restores checkpoint");
                    Finish(true, "PASS: authored manual defeat policy waits; visible retry pointer restores checkpoint."); return;
                }
                switch (step)
                {
                    case 0:
                        WalkIn();
                        if (!host.HasEntered) return;
                        walking.Walking = false;
                        Check(!host.Prepared, "Entering cannot synchronously finish the asynchronous opening"); step++; break;
                    case 1:
                        if (!host.Prepared) return;
                        int expected = SessionState.GetBool(Flag + ".edited", false) ? 4 : 3;
                        Check(host.Spawner.Members.Count == expected && host.Companion, "Configured enemy count and one runtime companion generated");
                        Check(host.Bindings.Single(b => b.Role == host.DoorRole).Entity.transform.localPosition.y > closedDoorY + 3, "Timeline really opens the bound door");
                        if (expected == 4)
                        {
                            Check(Mathf.Abs(GameObject.Find("Verification original door").transform.localPosition.y - closedDoorY) < .01f, "Original door is not animated after rebinding");
                            Check(host.Book.ExportDefinitions() == SessionState.GetString(Flag + ".quest", ""), "Object and count changes do not rewrite quest rules");
                        }
                        var outsider = UnityEngine.Object.FindObjectsByType<CombatActor>().Single(a => a.gameObject.name.StartsWith("Unrelated sentry")); outsider.Damage(100);
                        Check(host.Spawner.Attempt.ConfirmedDeaths == 0, "Unrelated enemy death does not count");
                        foreach (var enemy in host.Spawner.Members.Take(3)) enemy.Damage(100);
                        if (expected == 4) { Check(!host.Spawner.IsCleared, "Edited fourth enemy is required"); host.Spawner.Members[3].Damage(100); }
                        step++; break;
                    case 2:
                        if (!host.RescueAvailable) return;
                        if (dialoguePhase == 0) { presentation.OpenRescue(); dialoguePhase = 1; dialogueFrame = Time.frameCount; return; }
                        if (Time.frameCount <= dialogueFrame) return;
                        presentation.Ui.TryGetView(RescuePresentation.DialogueId, out var view);
                        var dialogue = (DialoguePanel)view;
                        if (dialoguePhase == 1 && SessionState.GetBool(Flag + ".edited", false))
                            Check(dialogue.Line.text == "Verification: replacement dialogue is in use.", "Replacement dialogue asset reaches the displayed conversation");
                        if (SessionState.GetBool(Flag + ".preview", false))
                        { EditorApplication.update -= Tick; SessionState.SetBool(Flag, false); return; }
                        if (dialoguePhase == 1)
                        {
                            Click(dialogue.Choices.GetComponentsInChildren<Button>().Single(b => b.gameObject.activeInHierarchy));
                            Check(host.Rescued && !host.DialogueFinished, "Release alone does not finish dialogue"); dialoguePhase = 2; dialogueFrame = Time.frameCount; return;
                        }
                        if (dialoguePhase == 2)
                        { Click(dialogue.CancelButton); Check(!host.DialogueFinished, "Cancelled dialogue is not completion"); dialoguePhase = 3; dialogueFrame = Time.frameCount; return; }
                        if (dialoguePhase == 3)
                        { presentation.OpenRescue(); dialoguePhase = 4; dialogueFrame = Time.frameCount; return; }
                        if (dialoguePhase == 4)
                        { Click(dialogue.Choices.GetComponentsInChildren<Button>().Single(b => b.gameObject.activeInHierarchy)); dialoguePhase = 5; dialogueFrame = Time.frameCount; return; }
                        Click(dialogue.ContinueButton);
                        Check(host.Run.Instances.Single().State == QuestStatus.Completed, "Actual rescue and dialogue complete Quest");
                        oldRoster = host.Spawner.Attempt; host.Player.Damage(100);
                        Check(!host.Recovering, "Completed quest no longer owns encounter death handling");
                        host.RestoreCheckpoint();
                        Check(oldRoster.IsClosed && !host.FinishRescueDialogue(firstAttempt), "Explicit retry invalidates old roster and dialogue callback immediately"); step++; break;
                    case 3:
                        if (host.Recovering || host.Run == null) return;
                        Check(host.AttemptId != firstAttempt && host.Player.Health == host.Player.MaxHealth, "New attempt and player restored");
                        Check(host.Spawner.Members.Count == 0 && !host.Companion && !host.HasEntered, "Dynamic objects removed and entry reset");
                        Check(Mathf.Abs(host.Bindings.Single(b => b.Role == host.DoorRole).Entity.transform.localPosition.y - closedDoorY) < .01f, "Door restored after director cancelled");
                        firstAttempt = host.AttemptId; step++; break;
                    case 4:
                        WalkIn();
                        if (host.Timeline.State != TimelineOperationState.Running) return;
                        walking.Walking = false; oldRoster = host.Spawner.Attempt; host.Player.Damage(100); step++; break;
                    case 5:
                        if (host.Recovering || host.Run == null) return;
                        Check(oldRoster.IsClosed && host.Timeline.State != TimelineOperationState.Running && host.AttemptId != firstAttempt, "Death during opening cancels Timeline and pre-generation");
                        step++; break;
                    case 6:
                        WalkIn(); if (!host.Prepared) return; walking.Walking = false;
                        oldRoster = host.Spawner.Attempt; firstAttempt = host.AttemptId;
                        host.Spawner.Members[0].Damage(100); host.Player.Damage(100);
                        Check(oldRoster.IsClosed, "Death during unfinished combat invalidates the roster"); step++; break;
                    case 7:
                        if (host.Recovering || host.Run == null) return;
                        Check(host.AttemptId != firstAttempt && host.Spawner.Members.Count == 0, "Unfinished combat resets without keeping survivors"); step++; break;
                    case 8:
                        WalkIn(); if (!host.Prepared) return; walking.Walking = false;
                        var member = host.Spawner.Members[0]; UnityEngine.Object.DestroyImmediate(member.gameObject);
                        Check(host.Spawner.Attempt.HasUnavailableRequiredMember && host.Spawner.Attempt.ConfirmedDeaths == 0 && !host.Spawner.IsCleared, "Destroy is not kill or clear");
                        host.RestoreCheckpoint(); step++; break;
                    case 9:
                        if (host.Recovering || host.Run == null) return;
                        Check(host.Spawner.Members.Count == 0, "Unavailable-member path cleans up on restore"); step++; break;
                    case 10:
                        WalkIn(); if (!host.Prepared) return; walking.Walking = false;
                        var closingRun = host.Run; var closingRoster = host.Spawner.Attempt; int stopNotices = 0;
                        using (host.ApplicationHost.Events.Subscribe<WeaveApplicationStopping>(_ => stopNotices++))
                        {
                            host.ApplicationHost.StopApplication(); host.ApplicationHost.StopApplication();
                            Check(stopNotices == 1 && !host.ApplicationHost.IsRunning && host.ApplicationHost.Services.IsDisposed, "Startup stops once and disposes owned services");
                            Check(closingRun.IsClosed && closingRoster.IsClosed && host.Run == null && host.Spawner.Members.Count == 0, "Gameplay is quiesced before service teardown");
                            host.RestoreCheckpoint(); Check(host.Run == null && !host.Recovering, "Stopped application cannot restart a quest through Retry");
                        }
                        Finish(true, "PASS: region/Timeline/scoped encounter/dialogue; authored defeat and recovery; old callback rejection; Startup 0.2 application stop cleans active gameplay and owned services exactly once."); break;
                }
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }
        private static void WalkIn()
        {
            if (host.HasEntered || host.Recovering) return;
            walking.Walking = true;
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Click(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var rect=(RectTransform)button.transform;
            var pointer=new PointerEventData(EventSystem.current) { position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)), button=PointerEventData.InputButton.Left };
            var hits=new System.Collections.Generic.List<RaycastResult>(); EventSystem.current.RaycastAll(pointer,hits);
            var target=hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
            Check(target == button.gameObject, "Pointer path blocked for " + button.name + " by " + (hits.Count > 0 ? hits[0].gameObject.name : "no hit"));
            ExecuteEvents.Execute(target,pointer,ExecuteEvents.pointerClickHandler);
        }
        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Flag, false);
            Directory.CreateDirectory("Work/TEMP"); File.WriteAllText("Work/TEMP/area-rescue-result.txt", message);
            if (host) File.WriteAllLines("Work/TEMP/area-rescue-trace.txt", host.Activity);
            if (success) Debug.Log(message); else Debug.LogError(message); EditorApplication.Exit(success ? 0 : 1);
        }
    }
    public sealed class AreaRescueWalkProbe : MonoBehaviour
    {
        public AreaRescueScenario Host;
        public bool Walking;
        private void Update()
        {
            if (Walking && Host && !Host.HasEntered && !Host.Recovering)
                Host.Player.GetComponent<CharacterController>().Move((Vector3.forward * 5 + Vector3.down * 2) * Time.deltaTime);
        }
    }
}
