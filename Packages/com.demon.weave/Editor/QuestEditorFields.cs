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
            if (condition == null || depth > 16) { parent.Add(new HelpBox("条件为空或嵌套过深。", HelpBoxMessageType.Error)); return; }
            var group = new VisualElement { style = { paddingLeft = 10, borderLeftWidth = 2, borderLeftColor = new Color(.25f, .4f, .55f), marginBottom = 10 } }; parent.Add(group);
            var kinds = new List<string> { "全部满足（AND）", "任一满足（OR）", "任务状态", "世界事实", "内部阶段状态（接取条件不可用）" };
            var kind = condition.Kind == ConditionKind.All ? kinds[0] : condition.Kind == ConditionKind.Any ? kinds[1] : condition.Kind == ConditionKind.QuestState ? kinds[2] : condition.Kind == ConditionKind.StageState ? kinds[4] : kinds[3];
            Choice(group, "条件类型", kinds, kind, value => Edit("改变条件类型", () => {
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
                    group.Add(new Button(() => Edit("移除条件", () => condition.Children.RemoveAt(index))) { text = "移除此条件" });
                }
                group.Add(new Button(() => Edit("添加条件", () => condition.Children.Add(new QuestCondition { Kind = ConditionKind.QuestState }))) { text = "+ 条件 / 条件组" });
            }
            else if (condition.Kind == ConditionKind.QuestState)
            {
                QuestChoice(group, "前置任务", condition.Subject, value => Edit("修改前置任务", () => condition.Subject = value), false, selectedId);
                StateChoice(group, condition.State, value => Edit("修改前置状态", () => condition.State = value));
            }
            else if (condition.Kind == ConditionKind.StageState)
            {
                StageChoice(group, "内部阶段", condition.Subject, value => Edit("修改阶段引用", () => condition.Subject = value));
                StateChoice(group, condition.State, value => Edit("修改阶段状态", () => condition.State = value));
            }
            else
            {
                RoleChoice(group, "对象", condition.Subject, value => Edit("修改事实对象", () => { condition.Subject = value; condition.Key = ""; }));
                SignalChoice(group, condition.Subject, condition.Key, false, value => Edit("修改事实", () => condition.Key = value));
                var expected = new Toggle("要求事实成立") { value = condition.Expected }; expected.RegisterValueChangedCallback(e => Edit("修改事实要求", () => condition.Expected = e.newValue)); group.Add(expected);
            }
        }
        private void StateChoice(VisualElement parent, QuestStatus state, Action<QuestStatus> changed)
        { Choice(parent, "要求状态", Enum.GetValues(typeof(QuestStatus)).Cast<QuestStatus>().Select(s => s.ToString()).ToList(), state.ToString(), value => changed((QuestStatus)Enum.Parse(typeof(QuestStatus), value)), value => QuestAuthoringChecks.Status((QuestStatus)Enum.Parse(typeof(QuestStatus), value))); }
        private void SignalChoice(VisualElement parent, string role, string value, bool isEvent, Action<string> changed)
        {
            var signals = Host?.Signals.Where(s => s.Role == role && s.IsEvent == isEvent).ToArray() ?? Array.Empty<WorldSignal>();
            Choice(parent, isEvent ? "交互事件" : "世界事实", signals.Select(s => s.Key).Distinct().ToList(), value, changed,
                key => signals.Any(s => s.Key == key) ? signals.First(s => s.Key == key).Label : "请选择 / " + key);
        }
        private void BuildContents(VisualElement parent, QuestDefinition quest, bool failure)
        {
            var list = failure ? quest.Fail : quest.Finish;
            var join = failure ? quest.FailCombination : quest.FinishCombination;
            BuildObjectives(parent, list, join, value => { if (failure) quest.FailCombination = value; else quest.FinishCombination = value; },
                failure ? "失败要求" : quest.Stages.Count > 0 ? "额外完成约束（可留空）" : "完成目标");
        }
        private void BuildObjectives(VisualElement parent, List<QuestContent> list, RuleCombination join, Action<RuleCombination> change, string title)
        {
            var foldout = new Foldout { text = title, value = true }; parent.Add(foldout);
            Choice(foldout, "目标组合", new List<string> { "全部满足", "任一满足" }, join == RuleCombination.All ? "全部满足" : "任一满足",
                value => Edit("修改目标组合", () => change(value == "全部满足" ? RuleCombination.All : RuleCombination.Any)));
            foreach (var content in list.ToArray())
            {
                var row = new VisualElement { style = { marginTop = 8, marginBottom = 12, paddingLeft = 10, borderLeftWidth = 2, borderLeftColor = new Color(.3f, .4f, .35f) } }; foldout.Add(row);
                Text(row, "要求说明", content.Label, value => Edit("修改要求说明", () => content.Label = value));
                var names = new List<string> { "事件次数", "当前事实", "任务状态", "内部阶段状态" };
                Choice(row, "判定类型", names, names[(int)content.Kind], value => Edit("修改完成判定", () => {
                    content.Kind = (ContentKind)names.IndexOf(value); content.Subject = ""; content.Key = ""; content.Required = 1;
                }));
                if (content.Kind == ContentKind.QuestState)
                {
                    QuestChoice(row, "目标任务", content.Subject, value => Edit("修改目标任务", () => content.Subject = value));
                    StateChoice(row, content.State, value => Edit("修改目标状态", () => content.State = value));
                }
                else if (content.Kind == ContentKind.StageState)
                {
                    StageChoice(row, "内部阶段", content.Subject, value => Edit("修改阶段引用", () => content.Subject = value));
                    StateChoice(row, content.State, value => Edit("修改阶段状态", () => content.State = value));
                }
                else
                {
                    RoleChoice(row, "对象", content.Subject, value => Edit("修改目标对象", () => { content.Subject = value; content.Key = ""; }));
                    SignalChoice(row, content.Subject, content.Key, content.Kind == ContentKind.EventCount, value => Edit("修改目标信号", () => content.Key = value));
                    if (content.Kind == ContentKind.EventCount)
                    {
                        var count = new IntegerField("需要次数") { value = content.Required, isDelayed = true };
                        count.RegisterValueChangedCallback(e => Edit("修改需要次数", () => content.Required = e.newValue)); row.Add(count);
                    }
                    else
                    {
                        var expected = new Toggle("要求事实成立") { value = content.Expected };
                        expected.RegisterValueChangedCallback(e => Edit("修改事实要求", () => content.Expected = e.newValue)); row.Add(expected);
                    }
                }
                row.Add(new Button(() => Edit("移除要求", () => list.Remove(content))) { text = "移除此要求" });
            }
            foldout.Add(new Button(() => Edit("添加要求", () => list.Add(new QuestContent { Id = Guid.NewGuid().ToString("N"), Label = "新要求" }))) { text = "+ 要求" });
        }
        private void BuildExecs(VisualElement parent, List<QuestExec> executions, string title)
        {
            var foldout = new Foldout { text = title, value = executions.Count > 0 }; parent.Add(foldout);
            CapabilityCatalog catalog;
            try { catalog = Host?.CreateCapabilities(); }
            catch (Exception e) { foldout.Add(new HelpBox(e.Message, HelpBoxMessageType.Error)); return; }
            foreach (var exec in executions.ToArray())
            {
                var entry = catalog?.Find(exec.CapabilityId);
                Choice(foldout, "行为", catalog?.Entries.Select(x => x.Id).ToList() ?? new List<string>(), exec.CapabilityId,
                    value => Edit("更换行为", () => { exec.CapabilityId = value; exec.ArgumentsJson = catalog.Find(value)?.DefaultJson() ?? "{}"; }), id => catalog?.Find(id)?.Label ?? id);
                if (entry != null) BuildParameters(foldout, exec, entry);
                else foldout.Add(new HelpBox("选择已注册行为；缺少的能力需先实现代码。", HelpBoxMessageType.Warning));
                foldout.Add(new Button(() => Edit("移除行为", () => executions.Remove(exec))) { text = "移除此行为" });
            }
            var add = new Button(() => {
                var entry = catalog?.Entries.FirstOrDefault();
                if (entry != null) Edit("添加行为", () => executions.Add(new QuestExec { Id = Guid.NewGuid().ToString("N"), CapabilityId = entry.Id, ArgumentsJson = entry.DefaultJson() }));
            }) { text = "+ 已有行为" }; add.SetEnabled(catalog != null && catalog.Entries.Count > 0); foldout.Add(add);
        }
        private void BuildParameters(VisualElement parent, QuestExec exec, CapabilityDescriptor descriptor)
        {
            JObject parameters;
            try { parameters = JObject.Parse(exec.ArgumentsJson); }
            catch (Exception e) { parent.Add(new HelpBox(e.Message, HelpBoxMessageType.Error)); parent.Add(new Button(() => Edit("重置无效参数", () => exec.ArgumentsJson = descriptor.DefaultJson())) { text = "使用此行为的默认参数" }); return; }
            void Set(string name, object value) => Edit("修改行为参数", () => { parameters[name] = JToken.FromObject(value); exec.ArgumentsJson = parameters.ToString(Formatting.None); });
            foreach (var field in descriptor.ParametersType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var attribute = field.GetCustomAttribute<CapabilityParameterAttribute>(); var label = attribute?.Label ?? field.Name;
                var token = parameters[field.Name] ?? JToken.FromObject(field.GetValue(Activator.CreateInstance(descriptor.ParametersType)) ?? "");
                if (attribute?.EntityRole == true) RoleChoice(parent, label, token?.Value<string>() ?? "", value => Set(field.Name, value));
                else if (field.FieldType == typeof(float))
                { var control = new FloatField(label) { value = token?.Value<float>() ?? 0, isDelayed = true }; control.RegisterValueChangedCallback(e => Set(field.Name, e.newValue)); parent.Add(control); }
                else if (field.FieldType == typeof(int))
                { var control = new IntegerField(label) { value = token?.Value<int>() ?? 0, isDelayed = true }; control.RegisterValueChangedCallback(e => Set(field.Name, e.newValue)); parent.Add(control); }
                else if (field.FieldType == typeof(bool))
                { var control = new Toggle(label) { value = token?.Value<bool>() ?? false }; control.RegisterValueChangedCallback(e => Set(field.Name, e.newValue)); parent.Add(control); }
                else if (field.FieldType == typeof(string)) Text(parent, label, token?.Value<string>() ?? "", value => Set(field.Name, value));
                else parent.Add(new Label(label + "：此类型尚无参数控件。"));
            }
            var usages = book.Quests.Where(q => QuestAuthoringChecks.Executions(q).Any(x => x.CapabilityId == exec.CapabilityId)).Select(q => q.Title);
            var implementation = new Foldout { text = "实现与引用", value = false }; parent.Add(implementation);
            implementation.Add(new Label("引用任务：" + string.Join("、", usages)) { style = { whiteSpace = WhiteSpace.Normal, fontSize = 11, marginTop = 4 } });
            implementation.Add(new Button(() => {
                var name = descriptor.ImplementationType?.Name;
                if (name == null) return;
                foreach (var guid in AssetDatabase.FindAssets(name + " t:MonoScript"))
                { var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid)); if (script && script.GetClass() == descriptor.ImplementationType) { AssetDatabase.OpenAsset(script); break; } }
            }) { text = "查看行为实现代码" });
        }
    }
}
