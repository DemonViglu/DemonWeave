using System.Linq;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Messenger
{
    public sealed class MessengerInteraction : MonoBehaviour
    {
        public MessengerScenario Scenario;
        public Camera Camera;
        public UIDocument Document;
        private Label state, feedback;
        private VisualElement choices;
        private VisualElement journal;
        private string signature;
        private void Start()
        {
            var panel = new VisualElement(); panel.style.position = Position.Absolute; panel.style.left = 20; panel.style.top = 20; panel.style.width = 380;
            journal = panel;
            panel.style.paddingLeft = 18; panel.style.paddingRight = 18; panel.style.paddingTop = 16; panel.style.paddingBottom = 16;
            panel.style.backgroundColor = new Color(.04f, .06f, .09f, .95f); Document.rootVisualElement.Add(panel);
            var title = new Label("THE MISSING ENVOY / ONE QUEST"); title.style.fontSize = 20; title.style.color = new Color(1, .75f, .3f); panel.Add(title);
            state = new Label(); state.style.whiteSpace = WhiteSpace.Normal; state.style.color = Color.white; state.style.marginTop = 12; panel.Add(state);
            choices = new VisualElement(); choices.style.marginTop = 12; panel.Add(choices);
            feedback = new Label(); feedback.style.whiteSpace = WhiteSpace.Normal; feedback.style.color = new Color(.6f, .85f, .9f); feedback.style.marginTop = 12; panel.Add(feedback);
        }
        private void Update()
        {
            if (state == null) return;
            var run = Scenario.Run;
            state.text = run == null ? "No active quest." : string.Join("\n", run.Instances.Select(q => q.Title + " — " + q.State + "\n" + string.Join("\n", q.Stages.Select(s => "  " + s.Title + " — " + s.State))));
            feedback.text = Scenario.Feedback + "\nCoins: " + Scenario.Coins + " / Hostile: " + Scenario.GuardHostile;
            if (run == null) return;
            var current = string.Join("|", run.Instances.SelectMany(q => q.Stages.Select(s => q.DefinitionId + s.Id + s.State + s.SelectedRouteId)));
            if (signature != current)
            {
                signature = current; choices.Clear();
                foreach (var quest in run.Instances)
                    foreach (var stage in quest.Stages.Where(s => s.State == QuestStatus.Active))
                        foreach (var route in quest.DefinitionSnapshot().Stages.Single(s => s.Id == stage.Id).Routes)
                        { var q = quest.DefinitionId; var s = stage.Id; var r = route.Id; choices.Add(new Button(() => Scenario.SelectRoute(q, s, r)) { text = route.Title }); }
            }
            if (!Input.GetMouseButtonDown(0)) return;
            // World input does not pass through the journal's buttons.
            var point = RuntimePanelUtils.ScreenToPanel(Document.rootVisualElement.panel, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            if (journal.worldBound.Contains(point)) return;
            if (Physics.Raycast(Camera.ScreenPointToRay(Input.mousePosition), out var hit, 200)) Scenario.Interact(hit.collider.GetComponentInParent<QuestEntity>());
        }
    }
}
