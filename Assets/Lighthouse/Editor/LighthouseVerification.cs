using System;
using System.IO;
using System.Linq;
using DemonWeave.Quests;
using DemonWeave.Unity;
using DemonWeave.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DemonWeave.Lighthouse.Editor
{
    [InitializeOnLoad]
    public static class LighthouseVerification
    {
        private const string Flag = "DemonWeave.Lighthouse.Verify";
        private const string Output = "Work/TEMP/quest-structure-20261029";
        private static int frames;
        static LighthouseVerification() { if (SessionState.GetBool(Flag, false)) EditorApplication.update += Tick; }
        [MenuItem("DemonWeave/验证灯塔任务结构")]
        public static void RunInteractive()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SessionState.SetBool(Flag + ".interactive", true);
            Run();
        }
        public static void Run()
        {
            try
            {
                LighthouseSetup.PrepareBindingSample(); CheckDefinitionBoundaries();
                EditorSceneManager.OpenScene(LighthouseSetup.ScenePath);
                PrepareAdoptedContent();
                SessionState.SetBool(Flag, true); EditorApplication.isPlaying = true;
            }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        private static void CheckDefinitionBoundaries()
        {
            var definitions = LighthouseSetup.Definitions();
            Check(QuestValidation.Validate(definitions).Count == 0, "sample definitions valid");
            definitions.Single(x => x.Id == "battery").ParentId = "battery";
            Check(QuestValidation.Validate(definitions).Any(x => x.Code == "parent.cycle"), "parent cycle diagnostic");
            definitions = LighthouseSetup.Definitions();
            definitions.Single(x => x.Id == "inspect").Accept.Subject = "missing";
            Check(QuestValidation.Validate(definitions).Any(x => x.Code == "quest.missing" && x.Path.Contains("accept")), "path-bearing missing reference");
            var first = new QuestDefinition { Id = "first", Title = "First", Finish = new System.Collections.Generic.List<QuestContent> {
                new QuestContent { Id = "count", Subject = "object", Key = "use", Required = 2 } } };
            var next = new QuestDefinition { Id = "next", Title = "Next", Accept = new QuestCondition { Kind = ConditionKind.QuestState, Subject = "first" },
                Finish = new System.Collections.Generic.List<QuestContent> { new QuestContent { Id = "count", Subject = "object", Key = "use" } } };
            using (var run = new QuestRun(new[] { next, first }, new EmptyWorld()))
            {
                first.Finish[0].Required = 99; // Authoring mutation cannot alter an already-created run.
                run.Refresh(); var id = Guid.NewGuid();
                run.Report(new QuestWorldEvent(id, "object", "use")); run.Report(new QuestWorldEvent(id, "object", "use"));
                Check(run.Find("first").Progress("count") == 1, "duplicate event is not progress");
                id = Guid.NewGuid(); run.Report(new QuestWorldEvent(id, "object", "use"));
                Check(run.Find("first").State == QuestStatus.Completed && run.Find("next").State == QuestStatus.Active && run.Find("next").Progress("count") == 0,
                    "snapshot and no event inheritance by newly activated successor");
                run.Report(new QuestWorldEvent(id, "object", "use")); Check(run.Find("next").Progress("count") == 0, "repeat cannot leak into successor");
                run.Dispose(); Check(!run.Report(new QuestWorldEvent(Guid.NewGuid(), "object", "use")), "closed run ignores late event");
            }
        }
        private sealed class EmptyWorld : IQuestWorld { public bool? ReadFact(string role, string key) => null; }
        private static void Tick()
        {
            if (!EditorApplication.isPlaying || ++frames < 20) return;
            EditorApplication.update -= Tick;
            try { Verify(); Finish(true, "PASS: Startup ownership; independent tasks/OR merge; real battery/generator/light/flag; begin/finish/fail effects; swapped tower owns its socket/light; adopted OFFLINE repair proposal runs through real Unity world; zero intensity is not completion; scope and event boundaries. No live model or human author-time claim."); }
            catch (Exception e) { Finish(false, e.ToString()); }
        }
        private static void Verify()
        {
            var scenario = UnityEngine.Object.FindAnyObjectByType<LighthouseScenario>();
            var app = scenario.ApplicationHost;
            var service = app.Kernel.App.Services.Get<QuestService>();
            Check(scenario.Run != null && service.ActiveRunCount == 1, "Startup owns scene run");
            var defaultRun = scenario.Run;
            Click(scenario, "harbour"); Click(scenario, "tower");
            Check(defaultRun.Find("battery").State == QuestStatus.Active && defaultRun.Find("generator").State == QuestStatus.Active, "two independent active routes");
            Click(scenario, "keeper");
            Check(scenario.Battery.parent == scenario.BatterySocket, "real battery transfer");
            Check(defaultRun.Find("light").State == QuestStatus.Completed && defaultRun.Find("report").State == QuestStatus.Active, "light fact gates report");
            Check(scenario.ReadFact("tower", "lit") == true && defaultRun.Find("lighthouse").State == QuestStatus.Active, "real light does not finish parent");
            var flag = scenario.Bindings.Single(x => x.Role == "flag").Entity.transform; var y = flag.position.y;
            Click(scenario, "harbour"); Click(scenario, "harbour");
            Check(defaultRun.Find("lighthouse").State == QuestStatus.Completed && Mathf.Approximately(flag.position.y, y + 1), "parent finish + once-only finish Exec");
            Check(defaultRun.Find("generator").State == QuestStatus.Cancelled, "explicit parent policy cancels unused route");
            Capture(scenario.GetComponent<LighthouseInteraction>().Camera);
            File.WriteAllLines(Output + "/battery-trace.txt", scenario.Trace);
            scenario.Close(); Check(service.ActiveRunCount == 0 && defaultRun.IsClosed, "scene close releases Startup service references");
            scenario.gameObject.SetActive(false); // Keep only the tested world active; avoid overlapping colliders/cameras.

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LighthouseSetup.WorldPrefab);
            void Variant(Action<LighthouseScenario> prepare, Action<LighthouseScenario> verify)
            {
                var root = UnityEngine.Object.Instantiate(prefab); var other = root.GetComponent<LighthouseScenario>();
                var book = ScriptableObject.CreateInstance<QuestBookAsset>(); book.Quests = other.Book.Quests.Select(q => q.Copy()).Reverse().ToList();
                other.Book = book; other.ApplicationHost = app;
                try { prepare(other); other.StartScenario(); Check(other.Run != null, "variant starts: " + other.Error); verify(other); }
                finally { other.Close(); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(book); }
            }
            Variant(x => x.KeeperWillLend = false, x =>
            {
                Click(x, "harbour"); Click(x, "tower"); Click(x, "keeper");
                Check(x.Run.Find("battery").State == QuestStatus.Active && x.ReadFact("tower", "lit") == false, "keeper refusal makes no false progress");
                Click(x, "generator"); Check(x.GeneratorRunning && x.Run.Find("light").State == QuestStatus.Completed, "alternative generator route, reordered definitions");
                Click(x, "harbour"); Check(x.Run.Find("lighthouse").State == QuestStatus.Completed, "alternative merge and final report");
                File.WriteAllLines(Output + "/generator-trace.txt", x.Trace);
            });
            Variant(x => x.Bindings.Single(b => b.Role == "tower").Entity = x.GetComponentsInChildren<QuestEntity>().Single(e => e.DisplayName == "Tower B"), x =>
            {
                var original = x.GetComponentsInChildren<QuestEntity>().Single(e => e.DisplayName == "Tower A");
                var alternate = x.Bindings.Single(b => b.Role == "tower").Entity;
                var receiver = alternate.GetComponent<LighthouseReceiver>();
                var definition = x.Book.ExportDefinitions();
                Physics.SyncTransforms(); var input = x.GetComponent<LighthouseInteraction>();
                Check(input.Pick(input.Camera.WorldToScreenPoint(alternate.transform.position + Vector3.up * 2)) == alternate, "replacement tower is mouse-pickable");
                Click(x, "harbour"); x.Interact(original);
                Check(x.Run.Find("inspect").Progress("inspection") == 0, "old tower no longer consumes task interaction");
                Click(x, "tower"); Click(x, "keeper");
                Check(x.Battery.parent == receiver.BatterySocket && receiver.Lamp.intensity > 0, "replacement owns battery and light effect");
                Check(original.GetComponent<LighthouseReceiver>().Lamp.intensity == 0, "old tower remains dark");
                Click(x, "harbour"); Check(x.Run.Find("lighthouse").State == QuestStatus.Completed, "rebound world completes same definition");
                Check(x.Book.ExportDefinitions() == definition, "binding replacement changes no quest rules");
                File.WriteAllLines(Output + "/tower-b-trace.txt", x.Trace);
            });
            Variant(x => { x.KeeperWillLend = false; x.Book.Quests = QuestBookAsset.ParseDefinitions(File.ReadAllText(Output + "/adopted-definition.json")); }, x =>
            {
                Click(x, "harbour"); Click(x, "tower"); Click(x, "keeper");
                Check(x.Run.Find("battery").State == QuestStatus.Active, "refusal still requires real alternative");
                Click(x, "generator"); Check(x.ReadFact("tower", "lit") == true, "adopted repair proposal lights actual tower");
                Click(x, "harbour"); Check(x.Run.Find("lighthouse").State == QuestStatus.Completed, "adopted proposal completes through world facts");
                File.WriteAllLines(Output + "/adopted-proposal-play.txt", x.Trace);
            });
            Variant(x => x.Book.Quests.Single(q => q.Id == "light").Accept.Children.Single(c => c.Kind == ConditionKind.Any).Kind = ConditionKind.All, x =>
            {
                Click(x, "harbour"); Click(x, "tower"); Click(x, "keeper");
                Check(x.Run.Find("light").State == QuestStatus.Waiting && x.ReadFact("tower", "lit") == false, "edited AND rule blocks single supply route");
                Click(x, "generator"); Check(x.Run.Find("light").State == QuestStatus.Completed, "edited AND rule admits both routes");
                Click(x, "harbour"); Check(x.Run.Find("lighthouse").State == QuestStatus.Completed, "edited route rule completes through facts");
                File.WriteAllLines(Output + "/and-route-trace.txt", x.Trace);
            });
            Variant(x => {
                x.Book.Quests.Single(q => q.Id == "light").BeginExec[0].ArgumentsJson = "{\"TargetRole\":\"tower\",\"Intensity\":0}";
                x.Book.Quests.Single(q => q.Id == "lighthouse").FailExec.Add(new QuestExec { Id = "lower-flag", CapabilityId = "world.transform.raise", ArgumentsJson = "{\"TargetRole\":\"flag\",\"Height\":-1}" });
            }, x =>
            {
                Click(x, "harbour"); Click(x, "tower"); Click(x, "generator");
                Check(x.Trace.Any(line => line.Contains("world.light.set: success")), "action accepted with zero intensity");
                Check(x.Run.Find("light").State == QuestStatus.Active && x.Run.Find("report").State == QuestStatus.Waiting, "success result cannot fake actual lit fact");
                Click(x, "harbour"); Check(x.Run.Find("report").State == QuestStatus.Waiting, "early report event not consumed");
                var flagY = x.Bindings.Single(b => b.Role == "flag").Entity.transform.position.y;
                Click(x, "weather"); Check(x.Run.Find("lighthouse").State == QuestStatus.Failed && x.Run.Find("light").State == QuestStatus.Cancelled, "failure and scope cleanup");
                Check(Mathf.Approximately(x.Bindings.Single(b => b.Role == "flag").Entity.transform.position.y, flagY - 1), "fail Exec has real world effect");
                File.WriteAllLines(Output + "/failure-trace.txt", x.Trace);
            });
            Check(service.ActiveRunCount == 0, "no retained scene runs");
            app.StopApplication(); Check(service.IsDisposed, "application shutdown owns service disposal");
        }
        private static void PrepareAdoptedContent()
        {
            Directory.CreateDirectory(Output);
            var host = UnityEngine.Object.FindAnyObjectByType<LighthouseScenario>();
            var book = ScriptableObject.CreateInstance<QuestBookAsset>(); book.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                book.Id = "review-to-play"; book.Title = "Offline adoption to world"; book.Quests = LighthouseProposalExample.BatteryOnly();
                var before = book.ExportDefinitions(); var proposal = LighthouseProposalExample.Create(book);
                var review = QuestProposalReview.Prepare(book, host, proposal);
                Check(book.ExportDefinitions() == before, "review-to-play preview is non-mutating");
                QuestProposalReview.Apply(review, host);
                File.WriteAllText(Output + "/adopted-definition.json", book.ExportDefinitions());
                File.WriteAllText(Output + "/adoption-review.txt", "OFFLINE PROTOCOL FIXTURE — NOT A MODEL RESPONSE\n" + review.Diff);
            }
            finally { UnityEngine.Object.DestroyImmediate(book); }
        }
        private static void Click(LighthouseScenario scenario, string role) => scenario.Interact(scenario.Bindings.Single(x => x.Role == role).Entity);
        private static void Capture(Camera camera)
        {
            Directory.CreateDirectory(Output);
            var rt = new RenderTexture(1440, 900, 24); var previous = RenderTexture.active; var target = camera.targetTexture;
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                var pixels = new Texture2D(1440, 900, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); pixels.Apply();
                File.WriteAllBytes(Output + "/lighthouse.png", pixels.EncodeToPNG()); UnityEngine.Object.Destroy(pixels);
            }
            finally { camera.targetTexture = target; RenderTexture.active = previous; rt.Release(); UnityEngine.Object.Destroy(rt); }
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception("Check failed: " + message); }
        private static void Finish(bool success, string message)
        {
            SessionState.SetBool(Flag, false); Directory.CreateDirectory(Output); File.WriteAllText(Output + "/result.txt", message);
            if (success) Debug.Log(message); else Debug.LogError(message);
            if (SessionState.GetBool(Flag + ".interactive", false))
            { SessionState.SetBool(Flag + ".interactive", false); EditorApplication.isPlaying = false; }
            else EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
