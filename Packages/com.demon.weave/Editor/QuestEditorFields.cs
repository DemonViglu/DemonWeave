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
        private void BuildCondition(VisualElement parent, QuestCondition condition, Action<QuestCondition> replace, int depth)
        {
            if (condition == null || depth > 16) { parent.Add(new HelpBox("Missing condition or nesting limit exceeded.", HelpBoxMessageType.Error)); return; }
            var group = new VisualElement { style = { paddingLeft = 10, borderLeftWidth = 2, borderLeftColor = new Color(.25f, .4f, .55f), marginBottom = 10 } }; parent.Add(group);
            var kinds = new List<string> { "All (AND)", "Any (OR)", "Quest State", "Object Condition", "Node State (not valid for Quest activation)" };
            var kind = condition.Kind == ConditionKind.All ? kinds[0] : condition.Kind == ConditionKind.Any ? kinds[1] : condition.Kind == ConditionKind.QuestState ? kinds[2] : condition.Kind == ConditionKind.StageState ? kinds[4] : kinds[3];
            Choice(group, "Condition Type", kinds, kind, value => Edit("Change Condition Type", () => {
                var nextKind = value == kinds[0] ? ConditionKind.All : value == kinds[1] ? ConditionKind.Any : value == kinds[2] ? ConditionKind.QuestState : value == kinds[4] ? ConditionKind.StageState : ConditionKind.Fact;
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
                group.Add(new Button(() => Edit("Add Condition", () => condition.Children.Add(new QuestCondition { Kind = ConditionKind.Fact }))) { text = "+ Condition / Group" });
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
            else
            {
                BuildObjectCondition(group, condition.Subject, condition.Key, false, (role, key) => Edit("Change Condition", () => { condition.Subject = role; condition.Key = key; }));
                var expected = new Toggle("Expected True") { value = condition.Expected }; expected.RegisterValueChangedCallback(e => Edit("Change Expected Fact", () => condition.Expected = e.newValue)); group.Add(expected);
            }
        }
        private void StateChoice(VisualElement parent, QuestStatus state, Action<QuestStatus> changed)
        { Choice(parent, "State", Enum.GetValues(typeof(QuestStatus)).Cast<QuestStatus>().Select(s => s.ToString()).ToList(), state.ToString(), value => changed((QuestStatus)Enum.Parse(typeof(QuestStatus), value)), value => QuestAuthoringChecks.Status((QuestStatus)Enum.Parse(typeof(QuestStatus), value))); }
        // Conditions and objectives author the same object + predicate reference.
        private void BuildObjectCondition(VisualElement parent, string role, string key, bool isEvent, Action<string, string> changed)
        {
            var node = SelectedNode;
            if (node != null) role = node.EntityRole;
            else RoleChoice(parent, "Entity", role, nextRole => {
                var available = Host?.Signals.Where(s => s.Role == nextRole && s.IsEvent == isEvent).ToArray() ?? Array.Empty<WorldSignal>();
                var nextKey = available.Any(s => s.Key == key) ? key : available.Length == 1 ? available[0].Key : "";
                changed(nextRole, nextKey);
            }, true);
            SignalChoice(parent, role, key, isEvent, nextKey => changed(node == null ? role : "", nextKey));
            if (Host != null && !string.IsNullOrEmpty(role) && !Host.Signals.Any(s => s.Role == role && s.IsEvent == isEvent))
                parent.Add(new HelpBox(isEvent ? "No declared events on this object." : "No [QuestCondition] methods on this object. Mark a public bool method on its component.", HelpBoxMessageType.Info));
        }
        private string ConditionLabel(string role, string key) => Host == null ? key : Host.Signals.FirstOrDefault(s => s.Role == (SelectedNode?.EntityRole ?? role) && s.Key == key).Label ?? key;
        private void SignalChoice(VisualElement parent, string role, string value, bool isEvent, Action<string> changed)
        {
            var signals = Host?.Signals.Where(s => s.Role == role && s.IsEvent == isEvent).ToArray() ?? Array.Empty<WorldSignal>();
            var row = ItemRow(parent); var selection = new VisualElement { style = { flexGrow = 1 } }; row.Add(selection);
            Choice(selection, isEvent ? "Event" : "Condition", signals.Select(s => s.Key).Distinct().ToList(), value, changed,
                key => signals.Any(s => s.Key == key) ? signals.First(s => s.Key == key).Label : "Select / " + key);
            var implementation = signals.FirstOrDefault(s => s.Key == value).ImplementationType;
            IconAction(row, "{}", "Open condition method", () => OpenImplementation(implementation, signals.FirstOrDefault(s => s.Key == value).MethodName)).SetEnabled(implementation != null);
        }
        private void BuildContents(VisualElement parent, QuestDefinition quest, bool failure)
        {
            var list = failure ? quest.Fail : quest.Finish;
            var join = failure ? quest.FailCombination : quest.FinishCombination;
            BuildObjectives(parent, list, join, value => { if (failure) quest.FailCombination = value; else quest.FinishCombination = value; },
                failure ? "Failure Conditions" : quest.Stages.Count > 0 ? "Additional Completion Rules" : "Objectives");
        }
        private void BuildObjectives(VisualElement parent, List<QuestContent> list, RuleCombination join, Action<RuleCombination> change, string title)
        {
            var foldout = new Foldout { text = title, value = true }; parent.Add(foldout);
            Choice(foldout, "Completion", new List<string> { "All", "Any" }, join == RuleCombination.All ? "All" : "Any",
                value => Edit("Change Completion", () => change(value == "All" ? RuleCombination.All : RuleCombination.Any)));
            foreach (var content in list.ToArray())
            {
                var row = new VisualElement { style = { marginTop = 8, marginBottom = 12, paddingLeft = 10, borderLeftWidth = 2, borderLeftColor = new Color(.3f, .4f, .35f) } }; foldout.Add(row);
                Text(row, "Label", content.Label, value => Edit("Change Label", () => content.Label = value));
                var names = new List<string> { "Event Count", "Fact", "Quest State", "Node State" };
                Choice(row, "Rule Type", names, names[(int)content.Kind], value => Edit("Change Completion Rule", () => {
                    content.Kind = (ContentKind)names.IndexOf(value); content.Subject = ""; content.Key = ""; content.Required = 1;
                }));
                if (content.Kind == ContentKind.QuestState)
                {
                    QuestChoice(row, "Target Quest", content.Subject, value => Edit("Change Target Quest", () => content.Subject = value));
                    StateChoice(row, content.State, value => Edit("Change Objective State", () => content.State = value));
                }
                else if (content.Kind == ContentKind.StageState)
                {
                    StageChoice(row, "Node", content.Subject, value => Edit("Change Node Reference", () => content.Subject = value));
                    StateChoice(row, content.State, value => Edit("Change Node State", () => content.State = value));
                }
                else
                {
                    BuildObjectCondition(row, content.Subject, content.Key, content.Kind == ContentKind.EventCount,
                        (role, key) => Edit("Change Objective Condition", () => { content.Subject = role; content.Key = key; content.Label = ConditionLabel(role, key); }));
                    if (content.Kind == ContentKind.EventCount)
                    {
                        var count = new IntegerField("Count") { value = content.Required, isDelayed = true };
                        count.RegisterValueChangedCallback(e => Edit("Change Count", () => content.Required = e.newValue)); row.Add(count);
                    }
                    else
                    {
                        var expected = new Toggle("Expected True") { value = content.Expected };
                        expected.RegisterValueChangedCallback(e => Edit("Change Expected Fact", () => content.Expected = e.newValue)); row.Add(expected);
                    }
                }
                row.Add(new Button(() => Edit("Remove Objective", () => list.Remove(content))) { text = "Remove Objective" });
            }
            foldout.Add(new Button(() => Edit("Add Objective", () => list.Add(new QuestContent { Id = Guid.NewGuid().ToString("N"), Label = "New Objective" }))) { text = "+ Objective" });
        }
        private void BuildExecs(VisualElement parent, List<QuestExec> executions, string title)
        {
            CapabilityCatalog catalog;
            try { catalog = Host?.CreateCapabilities(); }
            catch (Exception e) { parent.Add(new HelpBox(e.Message, HelpBoxMessageType.Error)); return; }
            var header = ItemRow(parent); header.style.marginTop = 8;
            header.Add(new Label(title) { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } });
            IconAction(header, "+", "Add Action", () => Edit("Add Action", () => executions.Add(new QuestExec { Id = Guid.NewGuid().ToString("N") })));
            foreach (var exec in executions.ToArray())
            {
                JObject arguments;
                try { arguments = JObject.Parse(exec.ArgumentsJson); }
                catch (Exception e) { parent.Add(new HelpBox(e.Message, HelpBoxMessageType.Error)); continue; }
                var entry = catalog?.Find(exec.CapabilityId, SelectedNode?.EntityRole ?? arguments[CapabilityCatalog.TargetKey]?.Value<string>());
                var target = SelectedNode?.EntityRole ?? arguments[CapabilityCatalog.TargetKey]?.Value<string>()
                    ?? (entry?.TargetRoles.Count == 1 ? entry.TargetRoles[0] : "");
                if (SelectedNode == null) RoleChoice(parent, "Entity", target, role => Edit("Change Action Entity", () => {
                    arguments[CapabilityCatalog.TargetKey] = role;
                    if (Host?.CreateCapabilities().Find(exec.CapabilityId, role)?.TargetRoles.Contains(role) != true) exec.CapabilityId = "";
                    exec.ArgumentsJson = arguments.ToString(Formatting.None);
                }), true);
                var row = ItemRow(parent);
                var options = catalog?.Entries.Where(c => c.TargetRoles.Contains(target)).ToArray() ?? Array.Empty<CapabilityDescriptor>();
                var choose = new Button(() => PickCatalog("Actions on selected object", options.Select(chosen => new QuestCatalogPicker.Entry {
                    Id = chosen.Id, Label = chosen.Label, Choose = () => Edit("Change Action", () => { exec.CapabilityId = chosen.Id; exec.ArgumentsJson = DefaultArguments(chosen, target); })
                }))) { text = (entry?.Label ?? "Select Action…") + "  ▾", name = "choose-action-" + exec.Id, tooltip = exec.CapabilityId };
                choose.style.flexGrow = 1; choose.style.unityTextAlign = TextAnchor.MiddleLeft; choose.SetEnabled(options.Length > 0); row.Add(choose);
                IconAction(row, "{}", "Open action method", () => OpenImplementation(entry)).SetEnabled(entry != null);
                IconAction(row, "×", "Remove Action", () => Edit("Remove Action", () => executions.Remove(exec)));
                if (entry != null)
                {
                    BuildParameters(parent, exec, entry);
                    var checkedArgs = JObject.Parse(exec.ArgumentsJson); if (SelectedNode != null) checkedArgs[CapabilityCatalog.TargetKey] = SelectedNode.EntityRole;
                    var error = catalog.Validate(exec.CapabilityId, checkedArgs.ToString(Formatting.None));
                    if (!string.IsNullOrEmpty(error)) parent.Add(new HelpBox(error, HelpBoxMessageType.Error));
                }
                else parent.Add(new HelpBox(string.IsNullOrEmpty(target) ? "Select a target object first." : "Choose a [QuestAction] method on this object. Only marked methods are listed.", HelpBoxMessageType.Info));
            }
        }
        private string DefaultArguments(CapabilityDescriptor descriptor, string entity)
        {
            var args = JObject.Parse(descriptor.DefaultJson(entity));
            if (SelectedNode != null) args.Remove(CapabilityCatalog.TargetKey);
            return args.ToString(Formatting.None);
        }
        private static void OpenImplementation(CapabilityDescriptor descriptor)
            => OpenImplementation(descriptor?.ImplementationType, descriptor?.MethodName);
        private static void OpenImplementation(Type implementation, string methodName = null)
        {
            var name = implementation?.Name;
            if (name == null) return;
            foreach (var guid in AssetDatabase.FindAssets(name + " t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script && (script.GetClass() == implementation || script.name == name)) {
                    int line = 1;
                    if (!string.IsNullOrEmpty(methodName)) {
                        var lines = script.text.Split('\n');
                        var found = Array.FindIndex(lines, text => System.Text.RegularExpressions.Regex.IsMatch(text, @"\b" + System.Text.RegularExpressions.Regex.Escape(methodName) + @"\s*\("));
                        if (found >= 0) line = found + 1;
                    }
                    AssetDatabase.OpenAsset(script, line); break;
                }
            }
        }
        private void BuildParameters(VisualElement parent, QuestExec exec, CapabilityDescriptor descriptor)
        {
            JObject parameters;
            try { parameters = JObject.Parse(exec.ArgumentsJson); }
            catch (Exception e) { parent.Add(new HelpBox(e.Message, HelpBoxMessageType.Error)); parent.Add(new Button(() => Edit("Reset Parameters", () => exec.ArgumentsJson = DefaultArguments(descriptor, SelectedNode?.EntityRole))) { text = "Reset Parameters" }); return; }
            void Set(string name, object value) => Edit("Change Action Parameters", () => { parameters[name] = JToken.FromObject(value); exec.ArgumentsJson = parameters.ToString(Formatting.None); });
            foreach (var field in descriptor.Arguments)
            {
                var label = field.Label;
                var token = parameters[field.Name] ?? JToken.FromObject(field.Default ?? "");
                if (field.EntityRole) RoleChoice(parent, label, token?.Value<string>() ?? "", value => Set(field.Name, value));
                else if (field.Type == typeof(float))
                { var control = new FloatField(label) { value = token?.Value<float>() ?? 0, isDelayed = true }; control.RegisterValueChangedCallback(e => Set(field.Name, e.newValue)); parent.Add(control); }
                else if (field.Type == typeof(double))
                { var control = new DoubleField(label) { value = token?.Value<double>() ?? 0, isDelayed = true }; control.RegisterValueChangedCallback(e => Set(field.Name, e.newValue)); parent.Add(control); }
                else if (field.Type == typeof(int))
                { var control = new IntegerField(label) { value = token?.Value<int>() ?? 0, isDelayed = true }; control.RegisterValueChangedCallback(e => Set(field.Name, e.newValue)); parent.Add(control); }
                else if (field.Type == typeof(bool))
                { var control = new Toggle(label) { value = token?.Value<bool>() ?? false }; control.RegisterValueChangedCallback(e => Set(field.Name, e.newValue)); parent.Add(control); }
                else if (field.Type == typeof(string)) Text(parent, label, token?.Value<string>() ?? "", value => Set(field.Name, value));
                else parent.Add(new Label(label + ": no editor for this parameter type."));
            }
        }
    }
}
