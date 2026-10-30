using System;
using System.Collections.Generic;
using System.Linq;
using DemonViglu.Startup.Unity;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.Messenger
{
    public sealed class MessengerScenario : MonoBehaviour, IQuestWorld, IQuestAuthoringHost
    {
        public AppHost ApplicationHost;
        public QuestBookAsset Book;
        public List<EntityBinding> Bindings = new List<EntityBinding>();
        public Transform SafePoint;
        public bool NegotiationSucceeds;
        public bool Accepted { get; private set; }
        public bool NegotiationRejected { get; private set; }
        public bool GuardHostile { get; private set; }
        public bool EnvoyAlive { get; private set; } = true;
        public int Coins { get; private set; } = 10;
        public QuestRun Run { get; private set; }
        public string Feedback { get; private set; } = "Read the contact notice to begin.";
        private readonly HashSet<string> clues = new HashSet<string>();
        private readonly List<string> trace = new List<string>();
        private QuestBindings world;
        private CapabilityCatalog capabilities;
        private EntityBinding[] runtimeBindings;
        public QuestBookAsset BookAsset { get => Book; set => Book = value; }
        public List<EntityBinding> BindingEntries => Bindings;
        public QuestRun ActiveRun => Run;
        public IReadOnlyList<string> Activity => trace;
        public IReadOnlyList<WorldSignal> Signals => new[] {
            new WorldSignal("contact", "accepted", "已接取寻找使者"),
            new WorldSignal("clue-a", "found", "已调查遗留物"), new WorldSignal("clue-b", "found", "已取得目击证词"),
            new WorldSignal("guard", "rejected", "谈判被明确拒绝"), new WorldSignal("guard", "hostile", "守卫实际敌对"),
            new WorldSignal("envoy", "safe", "使者实际脱险"), new WorldSignal("envoy", "handed-over", "使者安全交给接头人"),
            new WorldSignal("envoy", "dead", "使者死亡")
        }.Concat(Bindings.Select(b => new WorldSignal(b.Role, "interacted", "与对象交互", true))).ToArray();
        [Serializable] public sealed class NoParameters { }
        [Serializable] public sealed class NegotiationParameters
        {
            [CapabilityParameter("谈判押金")] public int Deposit = 2;
        }
        private bool LocationKnown => clues.Contains("clue-a") && clues.Contains("clue-b");
        private Transform Envoy => world?.Find("envoy") ? world.Find("envoy").transform : null;
        private Transform Contact => world?.Find("contact") ? world.Find("contact").transform : null;
        private void Start() => StartScenario();
        public void StartScenario()
        {
            Close(); trace.Clear();
            try
            {
                if (!ApplicationHost || ApplicationHost.Kernel == null || !Book || !SafePoint) throw new ArgumentException("Bind Startup, book and safe point.");
                runtimeBindings = Bindings.Select(b => new EntityBinding { Role = b.Role, Entity = b.Entity }).ToArray();
                world = new QuestBindings(runtimeBindings); capabilities = CreateCapabilities();
                var errors = QuestValidation.Validate(Book.Quests, r => world.Find(r), capabilities.Validate,
                    (r, k, e) => Signals.Any(s => s.Role == r && s.Key == k && s.IsEvent == e) ? "" : "Unknown signal: " + r + "." + k);
                if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
                Run = ApplicationHost.Kernel.App.Services.Get<QuestService>().Open(Book.Quests, this); Run.Refresh(); Pump();
            }
            catch (Exception e) { Feedback = e.Message; Close(); }
        }
        public bool? ReadFact(string role, string key)
        {
            if (world == null || !world.Find(role)) return null;
            if (role == "contact" && key == "accepted") return Accepted;
            if ((role == "clue-a" || role == "clue-b") && key == "found") return clues.Contains(role);
            if (role == "guard" && key == "rejected") return NegotiationRejected;
            if (role == "guard" && key == "hostile") return GuardHostile;
            if (role == "envoy" && key == "dead") return !EnvoyAlive;
            if (role == "envoy" && key == "safe") return EnvoyAlive && Envoy && SafePoint && (Envoy.parent == Contact || Vector3.Distance(Envoy.position, SafePoint.position) < .5f);
            if (role == "envoy" && key == "handed-over") return EnvoyAlive && Envoy && Contact && Envoy.parent == Contact;
            return null;
        }
        public CapabilityCatalog CreateCapabilities()
        {
            var bindings = new QuestBindings(Bindings); bindings.Require("guard"); bindings.Require("envoy"); bindings.Require("contact");
            var catalog = new CapabilityCatalog();
            catalog.Register<NegotiationParameters>("messenger.negotiate", "与守卫谈判", p => p.Deposit < 0 || p.Deposit > 10 ? "押金必须在 0–10 之间。" : "", p => {
                if (!LocationKnown || GuardHostile || !EnvoyAlive) return new ActionResult(false, "Negotiation is not possible in the actual world.");
                if (Coins < p.Deposit) return new ActionResult(false, "Not enough coins for the configured deposit.");
                Coins -= p.Deposit; // Already-paid costs survive a later route change.
                NegotiationRejected = !NegotiationSucceeds;
                if (NegotiationSucceeds) Envoy.position = SafePoint.position;
                return new ActionResult(true, NegotiationRejected ? "The guard refused and kept the deposit. Try the tunnel route." : "Guard released the envoy to safety.");
            }, typeof(MessengerScenario));
            catalog.Register<NoParameters>("messenger.make-hostile", "潜入暴露：守卫转为敌对", _ => "", _ => {
                if (!LocationKnown) return new ActionResult(false, "Location not confirmed.");
                GuardHostile = true; Tint(bindings.Require("guard"), new Color(.85f, .08f, .06f));
                return new ActionResult(true, "The guard spotted your plan and turned hostile. Use the tunnel to rescue the envoy.");
            }, typeof(MessengerScenario));
            return catalog;
        }
        public bool SelectRoute(string quest, string stage, string route)
        {
            if (Run == null) return false;
            bool accepted = Run.ChooseRoute(quest, stage, route, "player-choice", out var reason);
            Feedback = accepted ? "Route selected: " + route : "Choice refused: " + reason;
            Record(Feedback); Pump(); return accepted;
        }
        public void Interact(QuestEntity entity)
        {
            if (Run == null || Run.IsClosed || !entity) return;
            var role = runtimeBindings.FirstOrDefault(b => b.Entity == entity)?.Role;
            if (role == null) return;
            if (role == "contact")
            {
                Accepted = true;
                if (ReadFact("envoy", "safe") == true) { Envoy.SetParent(Contact, true); Envoy.localPosition = new Vector3(1.2f, 0, 0); Feedback = "Envoy safely handed over."; }
                else Feedback = "Find the envoy and bring them back alive.";
            }
            else if (role == "clue-a" || role == "clue-b")
            { clues.Add(role); Tint(entity, new Color(.1f, .7f, .4f)); Feedback = "Clue confirmed. Location " + (LocationKnown ? "known." : "still uncertain."); }
            else if (role == "tunnel")
            {
                if (!LocationKnown || !GuardHostile || !EnvoyAlive) Feedback = "The infiltration plan has not created an opening.";
                else { Envoy.position = SafePoint.position; Feedback = "Envoy rescued to the safe area. Handover is still required."; }
            }
            else Feedback = "Choose your rescue route in the journal.";
            Record(Feedback); Run.Report(new QuestWorldEvent(Guid.NewGuid(), role, "interacted")); Pump();
        }
        private void Pump()
        {
            int budget = 256;
            while (Run != null && Run.TryRead(out var notice))
            {
                if (--budget == 0) throw new InvalidOperationException("Output budget exceeded.");
                Record(notice.QuestId + "/" + notice.StageId + "/" + notice.RouteId + " " + notice.Kind + " " + notice.State + " " + notice.Detail);
                if (notice.Kind != QuestNoticeKind.Exec) continue;
                if (!Run.MayExecute(notice)) { Record("Skipped obsolete behaviour: " + notice.Execution.Id); continue; }
                var result = capabilities.Execute(notice.Execution.CapabilityId, notice.Execution.ArgumentsJson);
                Feedback = result.Message; Record("Behaviour " + notice.Execution.CapabilityId + ": " + result.Success + " / " + result.Message); Run.Refresh();
            }
        }
        private static void Tint(QuestEntity entity, Color color)
        { foreach (var renderer in entity.GetComponentsInChildren<Renderer>()) { var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); block.SetColor("_Color", color); renderer.SetPropertyBlock(block); } }
        private void Record(string text) { if (trace.Count >= 120) trace.RemoveAt(0); trace.Add(text); }
        public void Close() { Run?.Dispose(); Run = null; world = null; capabilities = null; runtimeBindings = null; }
        private void OnDisable() => Close();
    }
}
