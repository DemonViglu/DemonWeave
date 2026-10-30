using System;
using System.Linq;
using DemonWeave.Quests;
using UnityEngine.UIElements;
namespace DemonWeave.Editor
{
    public sealed partial class QuestEditorWindow
    {
        private string detailSection = "概览", routeSelection, routeSection = "条件";
        private void Tabs(VisualElement parent, string[] names, string active, Action<string> select)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8, marginBottom = 16 } }; parent.Add(row);
            foreach (var name in names)
            {
                var button = new Button(() => select(name)) { text = name }; button.style.flexGrow = 1; button.style.height = 30;
                if (name == active) { button.style.backgroundColor = new UnityEngine.Color(.22f,.33f,.42f); button.style.color = new UnityEngine.Color(1,.83f,.5f); } row.Add(button);
            }
        }
        private void StageChoice(VisualElement parent, string label, string value, Action<string> changed)
        {
            var stages = book.Quests.First(q => q.Id == selectedId).Stages;
            Choice(parent, label, stages.Select(s => s.Id).ToList(), value, changed, id => stages.FirstOrDefault(s => s.Id == id)?.Title ?? id);
        }
        private void BuildStages(VisualElement parent, QuestDefinition quest)
        {
            var stage = quest.Stages.FirstOrDefault(s => s.Id == selectedStageId);
            if (stage == null)
            {
                parent.Add(Heading("任务阶段"));
                foreach (var item in quest.Stages) parent.Add(new Button(() => { selectedStageId = item.Id; detailSection = "目标"; BuildInspector(); }) { text = item.Title + "  →" });
                parent.Add(new Button(() => Edit("添加阶段", () => quest.Stages.Add(new QuestStage { Id = "stage-" + Guid.NewGuid().ToString("N").Substring(0,8), Title = "新阶段" }))) { text = "+ 添加阶段" }); return;
            }
            var panel = new VisualElement { name = "stage-" + stage.Id }; parent.Add(panel);
            if (detailSection == "目标") BuildObjectives(panel, stage.Objectives, stage.Completion, value => stage.Completion = value, "达成目标");
            else if (detailSection == "条件")
            {
                Text(panel, "阶段名称", stage.Title, value => Edit("修改阶段名称", () => stage.Title = value));
                var required = new Toggle("完成任务必须达成此阶段") { value = stage.Required }; required.RegisterValueChangedCallback(e => Edit("修改阶段要求", () => stage.Required = e.newValue)); panel.Add(required);
                panel.Add(Heading("先完成哪些阶段"));
                foreach (var other in quest.Stages.Where(s => s != stage))
                {
                    var check = new Toggle(other.Title) { value = stage.After.Contains(other.Id) };
                    check.RegisterValueChangedCallback(e => Edit("修改前置", () => { if (e.newValue) { if (!stage.After.Contains(other.Id)) stage.After.Add(other.Id); } else stage.After.Remove(other.Id); })); panel.Add(check);
                }
                panel.Add(Heading("额外开放条件")); BuildCondition(panel, stage.OpenWhen, value => stage.OpenWhen = value, 0);
                panel.Add(new Button(() => { selectedStageId = null; Edit("删除阶段", () => quest.Stages.Remove(stage)); }) { text = "删除阶段" });
            }
            else if (detailSection == "行为") { BuildExecs(panel, stage.BeginExec, "阶段开始时"); BuildExecs(panel, stage.FinishExec, "目标达成后"); }
            else if (detailSection == "路线")
            {
                if (!stage.Routes.Any(r => r.Id == routeSelection)) routeSelection = stage.Routes.FirstOrDefault()?.Id;
                if (stage.Routes.Count == 0) panel.Add(new HelpBox("此阶段没有剧情选路。玩家战败后的回档在“恢复”页查看。", HelpBoxMessageType.Info));
                else
                {
                    Choice(panel, "编辑路线", stage.Routes.Select(r => r.Id).ToList(), routeSelection, value => { routeSelection = value; BuildInspector(); }, id => stage.Routes.FirstOrDefault(r => r.Id == id)?.Title ?? id);
                    var route = stage.Routes.Single(r => r.Id == routeSelection);
                    Tabs(panel, new[] { "条件", "目标", "行为", "改选" }, routeSection, value => { routeSection = value; BuildInspector(); });
                    if (routeSection == "条件") { Text(panel, "路线名称", route.Title, value => Edit("修改路线", () => route.Title = value)); BuildCondition(panel, route.AvailableWhen, value => route.AvailableWhen = value, 0); }
                    else if (routeSection == "目标") BuildObjectives(panel, route.Objectives, route.Completion, value => route.Completion = value, "附加目标");
                    else if (routeSection == "行为") BuildExecs(panel, route.SelectedExec, "选择此路线时");
                    else
                    {
                        foreach (var policy in stage.Switches.Where(s => s.From == route.Id).ToArray())
                        {
                            Choice(panel, "允许改选为", stage.Routes.Where(r => r.Id != route.Id).Select(r => r.Id).ToList(), policy.To, value => Edit("修改切换目标", () => policy.To = value));
                            BuildCondition(panel, policy.When, value => policy.When = value, 0);
                            panel.Add(new Button(() => Edit("删除改选", () => stage.Switches.Remove(policy))) { text = "删除此改选" });
                        }
                        panel.Add(new Button(() => Edit("添加改选", () => stage.Switches.Add(new RouteSwitch { Id = Guid.NewGuid().ToString("N"), From = route.Id }))) { text = "+ 允许改选" });
                    }
                    panel.Add(new Button(() => Edit("删除路线", () => stage.Routes.Remove(route))) { text = "删除当前路线" });
                }
                panel.Add(new Button(() => Edit("添加路线", () => stage.Routes.Add(new QuestRoute { Id = "route-" + Guid.NewGuid().ToString("N").Substring(0,8), Title = "新路线" }))) { text = "+ 添加路线" });
            }
        }
    }
}
