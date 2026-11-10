using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DemonWeave.Quests;
using DemonWeave.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    public sealed partial class QuestEditorWindow
    {
        private void BuildExecutionRules(VisualElement parent, QuestCondition running, Action<QuestCondition> setRunning, QuestCondition interrupt, Action<QuestCondition> setInterrupt)
        {
            var section = new Foldout { text = "Execution Rules", value = running.Kind == ConditionKind.WorldFact || interrupt != null };
            parent.Add(section);
            void Fact(string label, QuestCondition rule, Action<QuestCondition> set, bool optional)
            {
                section.Add(QuestFactPicker.Field(rule?.Kind == ConditionKind.WorldFact && !rule.Expected ? label + " (false)" : label,
                    rule?.Kind == ConditionKind.WorldFact ? rule.Subject : "", id => Edit("Change " + label, () =>
                        set(!string.IsNullOrEmpty(id) ? new QuestCondition { Kind = ConditionKind.WorldFact, Subject = id, Expected = rule?.Expected ?? true }
                            : optional ? null : new QuestCondition { Kind = ConditionKind.All }))));
                if (EditorApplication.isPlaying && rule?.Kind == ConditionKind.WorldFact && Host is QuestSceneHost runner)
                {
                    var currentValue = new Label(); section.Add(currentValue);
                    void RefreshValue() { var actual = runner.WorldState ? runner.WorldState.Read(rule.Subject) : null; currentValue.text = "Current value: " + (actual?.ToString() ?? "Unavailable"); }
                    RefreshValue(); currentValue.schedule.Execute(RefreshValue).Every(350);
                }
                if (rule != null && rule.Kind != ConditionKind.WorldFact && !(rule.Kind == ConditionKind.All && rule.Children.Count == 0))
                    section.Add(new HelpBox("A custom condition is configured. Replacing this field replaces that condition.", HelpBoxMessageType.Info));
            }
            section.tooltip = "Controls task progress. Gameplay continues unless its logic explicitly pauses it.";
            Fact("Progress while", running, setRunning, false);
            var sourceKind = interrupt == null ? "None" : interrupt.Kind == ConditionKind.ExternalQuestCompleted ? "Quest completion" : "World Fact";
            Choice(section, "Interrupt source", new List<string> { "None", "World Fact", "Quest completion" }, sourceKind,
                value => Edit("Change interruption source", () => setInterrupt(value == "None" ? null : new QuestCondition { Kind = value == "World Fact" ? ConditionKind.WorldFact : ConditionKind.ExternalQuestCompleted })));
            if (interrupt?.Kind == ConditionKind.ExternalQuestCompleted) BuildCondition(section, interrupt, setInterrupt, 0);
            else if (interrupt != null) Fact("Interrupt when", interrupt, setInterrupt, true);
        }
        private void BuildCondition(VisualElement parent, QuestCondition condition, Action<QuestCondition> replace, int depth)
        {
            if (condition == null || depth > 16) { parent.Add(new HelpBox("Missing condition or nesting limit exceeded.", HelpBoxMessageType.Error)); return; }
            if (condition.Kind == ConditionKind.ExternalQuestCompleted)
            {
                var books = AssetDatabase.FindAssets("t:QuestBookAsset").Select(g => AssetDatabase.LoadAssetAtPath<QuestBookAsset>(AssetDatabase.GUIDToAssetPath(g))).Where(b => b).ToArray();
                var source = books.FirstOrDefault(b => b.Id == condition.Subject);
                var field = new UnityEditor.UIElements.ObjectField("Source collection") { objectType = typeof(QuestBookAsset), value = source, allowSceneObjects = false };
                field.RegisterValueChangedCallback(evt => Edit("Change dependency source", () => { condition.Subject = (evt.newValue as QuestBookAsset)?.Id ?? ""; condition.Key = ""; }));
                parent.Add(field);
                if (source) Choice(parent, "Completed Quest", source.Quests.Select(q => q.Id).ToList(), condition.Key,
                    value => Edit("Change prerequisite", () => condition.Key = value), id => source.Quests.FirstOrDefault(q => q.Id == id)?.Title ?? "Select Quest");
                parent.Add(new Button(() => Edit("Remove dependency", () => replace(new QuestCondition { Kind = ConditionKind.All }))) { text = "Remove" });
                return;
            }
            if (condition.Kind == ConditionKind.WorldFact)
            {
                parent.Add(QuestFactPicker.Field("World fact", condition.Subject,
                    id => Edit("Change world fact", () => condition.Subject = id)));
                var expected = new Toggle("Must be true") { value = condition.Expected }; expected.RegisterValueChangedCallback(evt => Edit("Change fact requirement", () => condition.Expected = evt.newValue)); parent.Add(expected);
                parent.Add(new Button(() => Edit("Remove condition", () => replace(new QuestCondition { Kind = ConditionKind.All }))) { text = "Remove" }); return;
            }
            if (condition.Kind == ConditionKind.StageResult)
            {
                parent.Add(new Label(AuthorCondition(condition)) { tooltip = "Edit this input by connecting result ports on the graph." });
                return;
            }
            var group = new VisualElement { style = { paddingLeft = 10, borderLeftWidth = 2, borderLeftColor = new Color(.25f, .4f, .55f), marginBottom = 10 } }; parent.Add(group);
            var kinds = new List<string> { "All (AND)", "Any (OR)", "Quest State", "Node State (not valid for Quest activation)", "World Fact", "Completed Quest in another collection" };
            var kind = condition.Kind == ConditionKind.All ? kinds[0] : condition.Kind == ConditionKind.Any ? kinds[1] : condition.Kind == ConditionKind.QuestState ? kinds[2] : kinds[3];
            Choice(group, "Condition Type", kinds, kind, value => Edit("Change Condition Type", () => {
                var nextKind = value == kinds[4] ? ConditionKind.WorldFact : value == kinds[5] ? ConditionKind.ExternalQuestCompleted : value == kinds[0] ? ConditionKind.All : value == kinds[1] ? ConditionKind.Any : value == kinds[2] ? ConditionKind.QuestState : ConditionKind.StageState;
                if ((condition.Kind == ConditionKind.All || condition.Kind == ConditionKind.Any) && (nextKind == ConditionKind.All || nextKind == ConditionKind.Any)) condition.Kind = nextKind;
                else replace(new QuestCondition { Kind = nextKind });
            }));
            if (condition.Kind == ConditionKind.All || condition.Kind == ConditionKind.Any)
            {
                for (int i = 0; i < condition.Children.Count; i++)
                {
                    int index = i;
                    BuildCondition(group, condition.Children[i], value => condition.Children[index] = value, depth + 1);
                    group.Add(new Button(() => Edit("Remove Condition", () => condition.Children.RemoveAt(index))) { text = "Remove Condition" });
                }
                group.Add(new Button(() => Edit("Add Condition", () => condition.Children.Add(new QuestCondition { Kind = ConditionKind.WorldFact }))) { text = "+ Condition / Group" });
            }
            else if (condition.Kind == ConditionKind.QuestState)
            {
                QuestChoice(group, "Prerequisite Quest", condition.Subject, value => Edit("Change Prerequisite Quest", () => condition.Subject = value), false, selectedId);
                StateChoice(group, condition.State, value => Edit("Change Required State", () => condition.State = value));
            }
            else if (condition.Kind == ConditionKind.StageState)
            {
                StageChoice(group, "Node", condition.Subject, value => Edit("Change Node Reference", () => condition.Subject = value));
                StateChoice(group, condition.State, value => Edit("Change Node State", () => condition.State = value));
            }
        }
        private void StateChoice(VisualElement parent, QuestStatus state, Action<QuestStatus> changed)
        { Choice(parent, "State", Enum.GetValues(typeof(QuestStatus)).Cast<QuestStatus>().Select(s => s.ToString()).ToList(), state.ToString(), value => changed((QuestStatus)Enum.Parse(typeof(QuestStatus), value)), value => QuestAuthoringChecks.Status((QuestStatus)Enum.Parse(typeof(QuestStatus), value))); }
        private static void OpenImplementation(Type implementation, string methodName = null)
        {
            var name = implementation?.Name;
            if (name == null) return;
            foreach (var guid in AssetDatabase.FindAssets(name + " t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script && (script.GetClass() == implementation || script.name == implementation.Name && script.text.Contains("class " + implementation.Name))) {
                    int line = 1;
                    if (!string.IsNullOrEmpty(methodName)) {
                        var lines = script.text.Split('\n');
                        var found = Array.FindIndex(lines, text => System.Text.RegularExpressions.Regex.IsMatch(text, @"\bpublic\s+[^;{}()]+?\s+" + System.Text.RegularExpressions.Regex.Escape(methodName) + @"\s*\("));
                        if (found >= 0) line = found + 1;
                    }
                    AssetDatabase.OpenAsset(script, line); break;
                }
            }
        }
        private void StageChoice(VisualElement parent, string label, string value, Action<string> changed)
        {
            var stages = book.Quests.First(q => q.Id == selectedId).Stages;
            Choice(parent, label, stages.Select(s => s.Id).ToList(), value, changed, id => stages.FirstOrDefault(s => s.Id == id)?.Title ?? id);
        }
    }
}
