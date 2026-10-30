using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DemonWeave.Editor;
using DemonWeave.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.AreaRescue.Editor
{
    // Bounded regression for editing the real author control, Undo, and authored outcome serialization.
    public static class QuestAuthoringVerification
    {
        public static void Run()
        {
            try
            {
                EditorSceneManager.OpenScene(AreaRescueSetup.ScenePath);
                var host = UnityEngine.Object.FindAnyObjectByType<AreaRescueScenario>();
                host.Encounter = UnityEngine.Object.Instantiate(host.Encounter);
                QuestEditorWindow.Open(host.Book, host);
                var window = Resources.FindObjectsOfTypeAll<QuestEditorWindow>().Single();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(QuestEditorWindow).GetField("resourceSelection", flags).SetValue(window, "encounter-config");
                typeof(QuestEditorWindow).GetMethod("BuildInspector", flags).Invoke(window, null);
                var field = window.rootVisualElement.Q<IntegerField>("resource-EnemyCount");
                if (field == null) throw new Exception("Author count control missing.");
                int previous = host.Encounter.EnemyCount;
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                field.value = previous + 1;
                if (host.Encounter.EnemyCount != previous + 1) throw new Exception("Author control failed to commit its serialized value.");
                field.value = 999;
                if (!window.rootVisualElement.Query<HelpBox>().ToList().Any(box => box.style.display.value != DisplayStyle.None && box.text.Contains("出生点")))
                    throw new Exception("Invalid author count did not show its inline diagnostic.");
                Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group);
                if (host.Encounter.EnemyCount != previous) throw new Exception("Author edit Undo failed.");
                var outcome = host.Signals.Single(s => s.Key == "player-defeated");
                var branch = QuestOutcomeAuthoring.CreateBranch(outcome, host.CreateCapabilities().Find("checkpoint.wait-retry"));
                var clone = UnityEngine.Object.Instantiate(host.Book); clone.Quests.Single().Stages.Add(branch);
                clone.Quests = QuestBookAsset.ParseDefinitions(clone.ExportDefinitions());
                var errors = QuestAuthoringChecks.Validate(clone, host);
                if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
                if (clone.Quests.Single().Stages.Last().FinishExec.Single().CapabilityId != "checkpoint.wait-retry") throw new Exception("Outcome branch lost its action on serialization.");
                typeof(QuestEditorWindow).GetField("resourceSelection", flags).SetValue(window, host.DoorRole);
                typeof(QuestEditorWindow).GetMethod("BuildInspector", flags).Invoke(window, null);
                var originalDoor = host.Bindings.Single(b => b.Role == host.DoorRole).Entity;
                window.rootVisualElement.schedule.Execute(() => {
                    try
                    {
                        int index = host.Bindings.FindIndex(b => b.Role == host.DoorRole);
                        var bindingField = window.rootVisualElement.Q<PropertyField>($"resource-Bindings.Array.data[{index}].Entity").Q<ObjectField>();
                        if (bindingField == null) throw new Exception("Native object author control did not bind.");
                        Undo.IncrementCurrentGroup(); int bindingGroup = Undo.GetCurrentGroup();
                        bindingField.value = host.Bindings.Single(b => b.Role == host.ZoneRole).Entity;
                        window.rootVisualElement.schedule.Execute(() => {
                            try
                            {
                                if (host.Bindings.Single(b => b.Role == host.DoorRole).Entity == originalDoor) throw new Exception("Object control did not update the scene binding.");
                                if (!window.rootVisualElement.Query<HelpBox>().ToList().Any(box => box.style.display.value != DisplayStyle.None && box.text.Contains("Animator"))) throw new Exception("Wrong door binding was not diagnosed inline.");
                                Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(bindingGroup);
                                if (host.Bindings.Single(b => b.Role == host.DoorRole).Entity != originalDoor) throw new Exception("Object binding Undo failed.");
                                window.Close(); Directory.CreateDirectory("Work/TEMP");
                                File.WriteAllText("Work/TEMP/author-controls-result.txt", "PASS: count and object author controls commit; invalid count and Animator bindings diagnose inline; Undo restores both; outcome creation serializes and validates.");
                                EditorApplication.Exit(0);
                            }
                            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
                        }).StartingIn(150);
                    }
                    catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
                }).StartingIn(150);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
