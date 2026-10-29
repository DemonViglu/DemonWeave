using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DemonWeave.Editor;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Lighthouse.Editor
{
    public static class LighthouseAuthoringVerification
    {
        public static void RunBatch()
        {
            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(LighthouseSetup.ScenePath);
                Run(); EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        [MenuItem("DemonWeave/验证作者变更合同")]
        public static void Run()
        {
            const string output = "Work/TEMP/quest-authoring-20261029"; Directory.CreateDirectory(output);
            var source = UnityEngine.Object.FindAnyObjectByType<LighthouseScenario>();
            if (!source) throw new InvalidOperationException("Open the lighthouse scene first.");
            var book = ScriptableObject.CreateInstance<QuestBookAsset>(); book.Id = "authoring-check"; book.Title = "Transient verification"; book.Quests = LighthouseProposalExample.BatteryOnly();
            book.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                VerifyAuthorControls(source);
                var host = new HostView(source, book); var catalog = host.CreateCapabilities();
                Check(catalog.Validate("world.light.set", "{\"TargetRole\":\"tower\",\"Intensity\":2}") == "", "valid typed parameters");
                Check(catalog.Validate("world.light.set", "{\"TargetRole\":\"tower\"}") != "", "missing parameter rejected");
                Check(catalog.Validate("world.light.set", "{\"TargetRole\":\"tower\",\"Intensity\":2,\"typo\":1}") != "", "unknown parameter rejected");
                Check(catalog.Validate("world.light.set", "{\"TargetRole\":\"tower\",\"Intensity\":\"2\"}") != "", "wrong JSON parameter type is not coerced");
                var proposal = LighthouseProposalExample.Create(book); var original = book.ExportDefinitions();
                var change = QuestProposalReview.Prepare(book, host, proposal);
                Check(book.ExportDefinitions() == original && !book.Quests.Any(q => q.Id == "generator"), "preview does not edit content");
                var originalTitle = book.Title; book.Title = "Changed while reviewing";
                Check(Rejects(() => QuestProposalReview.Apply(change, host)), "stale document rejected"); book.Title = originalTitle;
                var previous = host.BindingEntries[0].Entity; host.BindingEntries[0].Entity = host.BindingEntries[1].Entity;
                Check(Rejects(() => QuestProposalReview.Apply(change, host)), "changed binding invalidates review"); host.BindingEntries[0].Entity = previous;
                QuestProposalReview.Apply(change, host);
                Check(book.Quests.Any(q => q.Id == "generator") && QuestAuthoringChecks.Dependencies(book.Quests.Single(q => q.Id == "light").Accept).Any(c => c.Subject == "generator"), "atomic task + dependency adoption");
                Check(Rejects(() => QuestProposalReview.Apply(change, host)), "proposal cannot apply twice");
                var adopted = book.ExportDefinitions(); book.OnBeforeSerialize(); book.OnAfterDeserialize(); Check(book.ExportDefinitions() == adopted, "JSON reload preserves definition");
                Check(QuestAuthoringChecks.Validate(book, host).Count == 0, "adopted content passes host validation");
                var beforeInvalid = book.ExportDefinitions();
                // Unknown operations/fields are covered by an actual malformed local proposal, not a model fixture claim.
                var bad = Newtonsoft.Json.Linq.JObject.Parse(proposal); bad["BaseRevision"] = QuestProposalReview.Revision(book); bad["Operations"][0]["Op"] = "execute-code";
                Check(Rejects(() => QuestProposalReview.Prepare(book, host, bad.ToString())), "unrecognized action rejected");
                Check(book.ExportDefinitions() == beforeInvalid, "rejected proposal leaves content unchanged");
                File.WriteAllText(output + "/review.txt", change.Summary + "\n" + change.Diff);
                File.WriteAllText(output + "/result.txt", "PASS: UI Toolkit field/change and graph connection callbacks + Undo; strict typed arguments; preview/no mutation; stale content and bindings rejected; atomic local adoption; replay rejection; JSON reload; host validation. UI callbacks were driven programmatically, not a native object-picker mouse claim. Offline proposal only, no live model call.");
                Debug.Log("DemonWeave authoring contract PASS (offline proposal; no live model).");
            }
            catch (Exception e) { File.WriteAllText(output + "/result.txt", "FAIL: " + e); throw; }
            finally { UnityEngine.Object.DestroyImmediate(book); }
        }
        private static void VerifyAuthorControls(LighthouseScenario source)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play before testing author controls.");
            var window = EditorWindow.GetWindow<QuestEditorWindow>(); window.CreateGUI();
            var original = source.Bindings.Single(b => b.Role == "tower").Entity;
            var alternate = source.GetComponentsInChildren<QuestEntity>().Single(e => e.DisplayName == "Tower B");
            var objectField = window.rootVisualElement.Query<ObjectField>().ToList().Single(f => f.label == "tower");
            objectField.value = alternate;
            Check(source.Bindings.Single(b => b.Role == "tower").Entity == alternate, "author object field updates scene binding");
            Undo.PerformUndo();
            Check(source.Bindings.Single(b => b.Role == "tower").Entity == original, "binding Undo restores object");
            var initialJson = source.Book.ExportDefinitions();
            var title = window.rootVisualElement.Query<TextField>().ToList().Single(f => f.label == "任务名称");
            var previous = title.value; title.value = previous + " / verification";
            Check(source.Book.ExportDefinitions() != initialJson, "author field changes shared JSON definition");
            Undo.PerformUndo(); Check(source.Book.ExportDefinitions() == initialJson, "definition Undo restores JSON cache");
            var graph = window.rootVisualElement.Q<GraphView>();
            var from = graph.nodes.First(n => n.title == source.Book.Quests.Single(q => q.Id == "generator").Title);
            var to = graph.nodes.First(n => n.title == source.Book.Quests.Single(q => q.Id == "battery").Title);
            var output = from.outputContainer.Q<Port>(); var input = to.inputContainer.Q<Port>();
            graph.graphViewChanged(new GraphViewChange { edgesToCreate = new List<Edge> { output.ConnectTo(input) } });
            Check(QuestAuthoringChecks.Dependencies(source.Book.Quests.Single(q => q.Id == "battery").Accept).Any(c => c.Subject == "generator"), "graph connection edits actual condition");
            Undo.PerformUndo(); Check(source.Book.ExportDefinitions() == initialJson, "graph connection Undo restores rules");
        }
        private static bool Rejects(Action action) { try { action(); return false; } catch (Exception) { return true; } }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private sealed class HostView : IQuestAuthoringHost
        {
            private readonly LighthouseScenario source;
            public HostView(LighthouseScenario source, QuestBookAsset book) { this.source = source; BookAsset = book; BindingEntries = source.Bindings.Select(b => new EntityBinding { Role = b.Role, Entity = b.Entity }).ToList(); }
            public QuestBookAsset BookAsset { get; set; }
            public List<EntityBinding> BindingEntries { get; }
            public QuestRun ActiveRun => null;
            public IReadOnlyList<string> Activity => Array.Empty<string>();
            public IReadOnlyList<WorldSignal> Signals => source.Signals;
            public CapabilityCatalog CreateCapabilities() => source.CreateCapabilities();
        }
    }
}
