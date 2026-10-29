using System;
using System.Collections.Generic;
using System.Linq;
using DemonViglu.Startup.Unity;
using DemonWeave.Quests;
using DemonWeave.Unity;
using DemonWeave.SampleGameplay;
using UnityEngine;
using Newtonsoft.Json;

namespace DemonWeave.Lighthouse
{
    // Business adapter. No quest IDs in input/ability code: it reports real object state and interactions.
    public sealed class LighthouseScenario : MonoBehaviour, IQuestWorld, IQuestAuthoringHost
    {
        public AppHost ApplicationHost;
        public QuestBookAsset Book;
        public List<EntityBinding> Bindings = new List<EntityBinding>();
        public Transform Battery;
        public Transform BatterySocket => Receiver ? Receiver.BatterySocket : null;
        private LighthouseReceiver Receiver
        {
            get
            {
                var target = world != null ? world.Find("tower") : Bindings.FirstOrDefault(b => b.Role == "tower")?.Entity;
                return target ? target.GetComponent<LighthouseReceiver>() : null;
            }
        }
        public Transform GeneratorRotor;
        public bool KeeperWillLend = true;
        public QuestRun Run { get; private set; }
        public string Error { get; private set; } = "";
        public string LastInteraction { get; private set; } = "";
        public IReadOnlyList<string> Trace => trace;
        public bool Accepted { get; private set; }
        public bool FaultKnown { get; private set; }
        public bool GeneratorRunning { get; private set; }
        public bool Storm { get; private set; }
        private QuestBindings world;
        private EntityBinding[] runtimeBindings = Array.Empty<EntityBinding>();
        private CapabilityCatalog capabilities;
        public QuestBookAsset BookAsset { get => Book; set => Book = value; }
        public List<EntityBinding> BindingEntries => Bindings;
        public QuestRun ActiveRun => Run;
        public IReadOnlyList<string> Activity => trace;
        public IReadOnlyList<WorldSignal> Signals => new[] {
            new WorldSignal("harbour", "accepted", "已接取巡检"),
            new WorldSignal("tower", "faultKnown", "已查明灯塔故障"),
            new WorldSignal("keeper", "batteryInstalled", "电池已实际安装"),
            new WorldSignal("generator", "running", "发电机正在供电"),
            new WorldSignal("tower", "lit", "灯塔实际亮起"),
            new WorldSignal("weather", "storm", "暴雨已到达")
        }.Concat(Bindings.Select(b => new WorldSignal(b.Role, "interacted", "与对象交互", true))).ToArray();
        private readonly List<string> trace = new List<string>();

        [Serializable] public sealed class LightParameters
        {
            [JsonProperty(Required = Required.Always), CapabilityParameter("目标对象", EntityRole = true)] public string TargetRole = "tower";
            [JsonProperty(Required = Required.Always), CapabilityParameter("亮度（0–12）")] public float Intensity = 5;
        }
        [Serializable] public sealed class MoveParameters
        {
            [JsonProperty(Required = Required.Always), CapabilityParameter("目标对象", EntityRole = true)] public string TargetRole = "flag";
            [JsonProperty(Required = Required.Always), CapabilityParameter("抬升高度")] public float Height = 1;
        }

        private void Start() => StartScenario();
        public void StartScenario()
        {
            Close(); Error = ""; trace.Clear();
            try
            {
                if (!ApplicationHost || ApplicationHost.Kernel == null) throw new InvalidOperationException("Startup AppHost must be initialized and explicitly bound.");
                if (!Book || !Battery || !BatterySocket || !GeneratorRotor) throw new InvalidOperationException("Scene requires its book, battery, socket and generator rotor.");
                runtimeBindings = Bindings.Select(b => new EntityBinding { Role = b.Role, Entity = b.Entity }).ToArray();
                world = new QuestBindings(runtimeBindings);
                capabilities = CreateCapabilities();
                var errors = QuestValidation.Validate(Book.Quests, role => world.Find(role), capabilities.Validate,
                    (role, key, isEvent) => Signals.Any(s => s.Role == role && s.Key == key && s.IsEvent == isEvent) ? "" : "Unknown fact/event: " + role + "." + key);
                if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
                Run = ApplicationHost.Kernel.App.Services.Get<QuestService>().Open(Book.Quests, this);
                Run.Refresh(); Pump();
            }
            catch (Exception e) { Error = e.Message; Record("Invalid: " + Error); Close(); }
        }
        public bool? ReadFact(string role, string key)
        {
            if (world == null || !world.Find(role)) return null;
            if (role == "harbour" && key == "accepted") return Accepted;
            if (role == "tower" && key == "faultKnown") return FaultKnown;
            if (role == "keeper" && key == "batteryInstalled") return Battery && BatterySocket && Battery.parent == BatterySocket;
            if (role == "generator" && key == "running") return GeneratorRunning && GeneratorRotor;
            if (role == "tower" && key == "lit")
            {
                var lamp = Receiver ? Receiver.Lamp : null;
                return lamp ? lamp.enabled && lamp.intensity > 0 : (bool?)null;
            }
            if (role == "weather" && key == "storm") return Storm;
            return null;
        }
        public void Interact(QuestEntity entity)
        {
            if (Run == null || Run.IsClosed || !entity || !isActiveAndEnabled) return;
            var roles = runtimeBindings.Where(x => x.Entity == entity).Select(x => x.Role).ToArray();
            if (roles.Length == 0) return;
            var role = roles[0];
            LastInteraction = entity.DisplayName;
            if (role == "harbour") Accepted = true;
            else if (role == "tower" && Accepted) FaultKnown = true;
            else if (role == "keeper")
            {
                if (!FaultKnown) LastInteraction = "Inspect the tower first.";
                else if (!KeeperWillLend) LastInteraction = "Keeper refused. The generator is another route.";
                else if (Battery && BatterySocket)
                { Battery.SetParent(BatterySocket, false); Battery.localPosition = Vector3.zero; LastInteraction = "Battery transferred and installed."; }
            }
            else if (role == "generator")
            {
                if (!FaultKnown) LastInteraction = "Inspect the tower first.";
                else { GeneratorRunning = true; LastInteraction = "Generator repaired; rotor running."; }
            }
            else if (role == "weather") { Storm = true; LastInteraction = "Storm arrived."; }
            Record("World: " + LastInteraction);
            Run.Report(new QuestWorldEvent(Guid.NewGuid(), role, "interacted")); Pump();
        }
        public CapabilityCatalog CreateCapabilities()
        {
            var bindings = new QuestBindings(Bindings);
            var tower = bindings.Find("tower");
            var receiver = tower ? tower.GetComponent<LighthouseReceiver>() : null;
            if (!receiver || !receiver.Ready) throw new ArgumentException("tower 绑定需要 LighthouseReceiver，并配置自身的电池插槽与灯光。 ");
            var catalog = new CapabilityCatalog();
            catalog.Register<LightParameters>("world.light.set", "设置灯光",
                p => {
                    var target = bindings.Find(p.TargetRole);
                    if (!target || !target.GetComponentInChildren<Light>(true)) return "目标对象需要 Light 组件。";
                    return float.IsNaN(p.Intensity) || float.IsInfinity(p.Intensity) || p.Intensity < 0 || p.Intensity > 12 ? "亮度必须在 0–12 之间。" : "";
                },
                p => {
                    if (ReadFact("keeper", "batteryInstalled") != true && ReadFact("generator", "running") != true)
                        return new ActionResult(false, "No actual power supply. State/plan claims are insufficient.");
                    var action = ScriptableObject.CreateInstance<SetSignalLight>();
                    try { action.TargetRole = p.TargetRole; action.Intensity = p.Intensity; return action.Execute(bindings); }
                    finally { Destroy(action); }
                }, typeof(LighthouseScenario));
            catalog.Register<MoveParameters>("world.transform.raise", "抬升场景物体",
                p => !bindings.Find(p.TargetRole) ? "目标对象缺失。" :
                    float.IsNaN(p.Height) || float.IsInfinity(p.Height) || Math.Abs(p.Height) > 10 ? "高度必须在 -10–10 之间。" : "",
                p => {
                    var action = ScriptableObject.CreateInstance<MoveWorldObject>();
                    try { action.TargetRole = p.TargetRole; action.Offset = Vector3.up * p.Height; return action.Execute(bindings); }
                    finally { Destroy(action); }
                }, typeof(LighthouseScenario));
            return catalog;
        }
        public void RefreshWorld() { if (Run == null) return; Run.Refresh(); Pump(); }
        private void Pump()
        {
            // Quest does not run behaviour. The host consumes lifecycle requests, then reads actual world facts.
            // Bounded by finite lifecycle transitions; no graph/task scheduler is introduced.
            int budget = 512;
            while (Run != null && Run.TryRead(out var notice))
            {
                if (--budget <= 0) throw new InvalidOperationException("Lifecycle output budget exceeded.");
                var title = Run.Find(notice.QuestId)?.Title ?? notice.QuestId;
                Record(title + " / " + notice.Kind + " / " + notice.State + " / " + notice.Detail);
                if (notice.Kind != QuestNoticeKind.Exec) continue;
                if (!Run.MayExecute(notice)) { Record("Skipped stale Exec: " + notice.Execution.Id); continue; }
                try
                {
                    var result = capabilities.Execute(notice.Execution.CapabilityId, notice.Execution.ArgumentsJson);
                    Record("Exec " + notice.Execution.CapabilityId + ": " + (result.Success ? "success" : "failed") + " / " + result.Message);
                }
                catch (Exception e) { Record("Exec failed: " + e.Message); }
                Run.Refresh();
            }
        }
        private void Update() { if (GeneratorRunning && GeneratorRotor) GeneratorRotor.Rotate(Vector3.forward, 100 * Time.deltaTime); }
        private void Record(string message) { if (trace.Count >= 150) trace.RemoveAt(0); trace.Add(message); }
        public void Close() { Run?.Dispose(); Run = null; world = null; capabilities = null; runtimeBindings = Array.Empty<EntityBinding>(); }
        private void OnDisable() => Close();
    }
}
