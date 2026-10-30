using System;
using System.Linq;
using DemonWeave.Quests;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    public sealed partial class QuestEditorWindow
    {
        private readonly System.Collections.Generic.Dictionary<string, bool> foldStates = new System.Collections.Generic.Dictionary<string, bool>();
        private Foldout RememberFold(string key, string title, bool initial)
        {
            var fold = new Foldout { text = title, value = foldStates.TryGetValue(key, out var open) ? open : initial };
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) foldStates[key] = e.newValue; });
            return fold;
        }
        private void StageChoice(VisualElement parent, string label, string value, Action<string> changed)
        {
            var stages = book.Quests.First(q => q.Id == selectedId).Stages;
            Choice(parent, label, stages.Select(s => s.Id).ToList(), value, changed,
                id => stages.FirstOrDefault(s => s.Id == id)?.Title ?? id);
        }
        private void BuildStages(VisualElement parent, QuestDefinition quest)
        {
            parent.Add(Heading("内部阶段与剧情路线"));
            parent.Add(new HelpBox("阶段目标默认全部达成。前置阶段控制开放；路线需要明确选择。切换路线保留已经发生的世界后果。", HelpBoxMessageType.Info));
            foreach (var stage in quest.Stages.ToArray())
            {
                var panel = RememberFold(quest.Id + "/" + stage.Id, stage.Title, false); parent.Add(panel);
                Text(panel, "阶段名称", stage.Title, value => Edit("修改阶段名称", () => stage.Title = value));
                panel.Add(new Label("标识：" + stage.Id));
                var required = new Toggle("任务完成所必需") { value = stage.Required };
                required.RegisterValueChangedCallback(e => Edit("修改阶段要求", () => stage.Required = e.newValue)); panel.Add(required);
                foreach (var other in quest.Stages.Where(s => s != stage))
                {
                    var prerequisite = new Toggle("等待：" + other.Title) { value = stage.After.Contains(other.Id) };
                    prerequisite.RegisterValueChangedCallback(e => Edit("修改阶段前置", () => {
                        if (e.newValue) { if (!stage.After.Contains(other.Id)) stage.After.Add(other.Id); }
                        else stage.After.Remove(other.Id);
                    })); panel.Add(prerequisite);
                }
                panel.Add(Heading("额外开放条件")); BuildCondition(panel, stage.OpenWhen, value => stage.OpenWhen = value, 0);
                BuildObjectives(panel, stage.Objectives, stage.Completion, value => stage.Completion = value, "共同目标");
                BuildExecs(panel, stage.BeginExec, "阶段开始行为"); BuildExecs(panel, stage.FinishExec, "阶段完成行为");
                foreach (var route in stage.Routes.ToArray())
                {
                    var routePanel = RememberFold(quest.Id + "/" + stage.Id + "/" + route.Id, "路线：" + route.Title, false); panel.Add(routePanel);
                    Text(routePanel, "路线名称", route.Title, value => Edit("修改路线名称", () => route.Title = value));
                    routePanel.Add(new Label("标识：" + route.Id));
                    BuildCondition(routePanel, route.AvailableWhen, value => route.AvailableWhen = value, 0);
                    BuildObjectives(routePanel, route.Objectives, route.Completion, value => route.Completion = value, "路线额外目标（可留空）");
                    BuildExecs(routePanel, route.SelectedExec, "选中路线时的行为");
                    routePanel.Add(new Button(() => Edit("删除路线", () => stage.Routes.Remove(route))) { text = "删除路线（引用须同步修正）" });
                }
                panel.Add(new Button(() => Edit("添加路线", () => stage.Routes.Add(new QuestRoute { Id = "route-" + Guid.NewGuid().ToString("N").Substring(0, 8), Title = "新路线" }))) { text = "+ 路线" });
                foreach (var policy in stage.Switches.ToArray())
                {
                    var policyPanel = RememberFold(quest.Id + "/" + stage.Id + "/switch/" + policy.Id, "允许改选：" + policy.From + " → " + policy.To, false); panel.Add(policyPanel);
                    var ids = stage.Routes.Select(r => r.Id).ToList();
                    Choice(policyPanel, "原路线", ids.ToList(), policy.From, value => Edit("修改切换来源", () => policy.From = value));
                    Choice(policyPanel, "新路线", ids.ToList(), policy.To, value => Edit("修改切换目标", () => policy.To = value));
                    BuildCondition(policyPanel, policy.When, value => policy.When = value, 0);
                    policyPanel.Add(new Button(() => Edit("删除切换规则", () => stage.Switches.Remove(policy))) { text = "删除切换规则" });
                }
                panel.Add(new Button(() => Edit("添加切换规则", () => stage.Switches.Add(new RouteSwitch { Id = Guid.NewGuid().ToString("N") }))) { text = "+ 允许改选的条件" });
                panel.Add(new Button(() => Edit("删除阶段", () => quest.Stages.Remove(stage))) { text = "删除阶段（引用须同步修正）" });
            }
            parent.Add(new Button(() => Edit("添加内部阶段", () => quest.Stages.Add(new QuestStage { Id = "stage-" + Guid.NewGuid().ToString("N").Substring(0, 8), Title = "新阶段" }))) { text = "+ 内部阶段" });
        }
    }
}
