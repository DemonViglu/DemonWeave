using System;
using System.Linq;
using DemonWeave.Quests;
using UnityEngine.UIElements;
namespace DemonWeave.Editor
{
    public sealed partial class QuestEditorWindow
    {
        private string detailSection = "Overview", routeSelection, routeSection = "Conditions";
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
                parent.Add(Heading("Nodes"));
                foreach (var item in quest.Stages) parent.Add(new Button(() => { selectedStageId = item.Id; detailSection = "Objective"; BuildInspector(); }) { text = item.Title + "  →" });
                parent.Add(new Button(() => AddStage(quest, null)) { text = "+ Node" }); return;
            }
            var panel = new VisualElement { name = "stage-" + stage.Id }; parent.Add(panel);
            if (detailSection == "Objective") BuildObjectives(panel, stage.Objectives, stage.Completion, value => stage.Completion = value, "Objectives");
            else if (detailSection == "Conditions")
            {
                Text(panel, "Node Name", stage.Title, value => Edit("Rename Node", () => stage.Title = value));
                var required = new Toggle("Required for Quest completion") { value = stage.Required }; required.RegisterValueChangedCallback(e => Edit("Change Node Requirement", () => stage.Required = e.newValue)); panel.Add(required);
                panel.Add(Heading("Prerequisites"));
                foreach (var other in quest.Stages.Where(s => s != stage))
                {
                    var check = new Toggle(other.Title) { value = stage.After.Contains(other.Id) };
                    check.RegisterValueChangedCallback(e => Edit("Change Dependency", () => { if (e.newValue) { if (!stage.After.Contains(other.Id)) stage.After.Add(other.Id); } else stage.After.Remove(other.Id); })); panel.Add(check);
                }
                panel.Add(Heading("Additional Conditions")); BuildCondition(panel, stage.OpenWhen, value => stage.OpenWhen = value, 0);
                panel.Add(new Button(() => DeleteStages(quest, new[] { stage.Id })) { text = "Delete Node" });
            }
            else if (detailSection == "Action") { BuildExecs(panel, stage.BeginExec, "On Enter"); BuildExecs(panel, stage.FinishExec, "On Complete"); }
            else if (detailSection == "Routes")
            {
                if (!stage.Routes.Any(r => r.Id == routeSelection)) routeSelection = stage.Routes.FirstOrDefault()?.Id;
                if (stage.Routes.Count == 0) panel.Add(new HelpBox("No routes configured.", HelpBoxMessageType.Info));
                else
                {
                    Choice(panel, "Route", stage.Routes.Select(r => r.Id).ToList(), routeSelection, value => { routeSelection = value; BuildInspector(); }, id => stage.Routes.FirstOrDefault(r => r.Id == id)?.Title ?? id);
                    var route = stage.Routes.Single(r => r.Id == routeSelection);
                    Tabs(panel, new[] { "Conditions", "Objective", "Action", "Switching" }, routeSection, value => { routeSection = value; BuildInspector(); });
                    if (routeSection == "Conditions") { Text(panel, "Route Name", route.Title, value => Edit("Change Route", () => route.Title = value)); BuildCondition(panel, route.AvailableWhen, value => route.AvailableWhen = value, 0); }
                    else if (routeSection == "Objective") BuildObjectives(panel, route.Objectives, route.Completion, value => route.Completion = value, "Additional Objectives");
                    else if (routeSection == "Action") BuildExecs(panel, route.SelectedExec, "On Select");
                    else
                    {
                        foreach (var policy in stage.Switches.Where(s => s.From == route.Id).ToArray())
                        {
                            Choice(panel, "Allow Switch To", stage.Routes.Where(r => r.Id != route.Id).Select(r => r.Id).ToList(), policy.To, value => Edit("Change Switch Target", () => policy.To = value));
                            BuildCondition(panel, policy.When, value => policy.When = value, 0);
                            panel.Add(new Button(() => Edit("Delete Switch", () => stage.Switches.Remove(policy))) { text = "Remove Switch" });
                        }
                        panel.Add(new Button(() => Edit("Add Switch", () => stage.Switches.Add(new RouteSwitch { Id = Guid.NewGuid().ToString("N"), From = route.Id }))) { text = "+ Switch" });
                    }
                    panel.Add(new Button(() => Edit("Delete Route", () => stage.Routes.Remove(route))) { text = "Delete Route" });
                }
                panel.Add(new Button(() => Edit("Add Route", () => stage.Routes.Add(new QuestRoute { Id = "route-" + Guid.NewGuid().ToString("N").Substring(0,8), Title = "New Route" }))) { text = "+ Route" });
            }
        }
    }
}
