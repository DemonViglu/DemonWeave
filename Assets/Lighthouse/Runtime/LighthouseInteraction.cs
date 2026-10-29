using System.Linq;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Lighthouse
{
    public sealed class LighthouseInteraction : MonoBehaviour
    {
        public LighthouseScenario Scenario;
        public Camera Camera;
        public UIDocument Document;
        private Label tasks, interaction;
        private void Start()
        {
            var root = Document.rootVisualElement;
            var panel = new VisualElement(); panel.pickingMode = PickingMode.Ignore;
            panel.style.position = Position.Absolute; panel.style.left = 24; panel.style.top = 24; panel.style.width = 360;
            panel.style.paddingLeft = 18; panel.style.paddingRight = 18; panel.style.paddingTop = 18; panel.style.paddingBottom = 18;
            panel.style.backgroundColor = new Color(.04f, .07f, .1f, .94f); root.Add(panel);
            var heading = new Label("LIGHT BEFORE THE STORM"); heading.style.fontSize = 21; heading.style.color = new Color(1, .75f, .3f); panel.Add(heading);
            var help = new Label("Click the harbour notice, then inspect the tower.\nBorrow the battery OR repair the generator.");
            help.style.whiteSpace = WhiteSpace.Normal; help.style.color = Color.white; help.style.marginTop = 10; panel.Add(help);
            tasks = new Label(); tasks.style.whiteSpace = WhiteSpace.Normal; tasks.style.color = new Color(.8f, .85f, .9f); tasks.style.marginTop = 14; panel.Add(tasks);
            interaction = new Label(); interaction.style.whiteSpace = WhiteSpace.Normal; interaction.style.color = new Color(.6f, .85f, .85f); interaction.style.marginTop = 14; panel.Add(interaction);
        }
        private void Update()
        {
            if (tasks == null) return;
            var run = Scenario.Run;
            tasks.text = run == null ? Scenario.Error : string.Join("\n", run.Instances.Select(x => x.Title + " — " + x.State));
            var entity = Pick(Input.mousePosition);
            if (entity && Input.GetMouseButtonDown(0)) Scenario.Interact(entity);
            interaction.text = (entity ? "Click / " + entity.DisplayName + "\n" : "") + Scenario.LastInteraction;
        }
        public QuestEntity Pick(Vector3 position) => Camera && Physics.Raycast(Camera.ScreenPointToRay(position), out var hit, 200)
            ? hit.collider.GetComponentInParent<QuestEntity>() : null;
    }
}
