using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DemonViglu.Startup.Unity;
using DemonWeave.Startup;
using DemonWeave.Quests;
using DemonWeave.Unity;
using DemonWeave.World;
using UnityEngine;
using UnityEngine.Timeline;

namespace DemonWeave.AreaRescue
{
    public sealed class AreaRescueScenario : MonoBehaviour, IQuestWorld, IQuestAuthoringHost
    {
        public AppHost ApplicationHost;
        public QuestBookAsset Book;
        public List<EntityBinding> Bindings = new List<EntityBinding>();
        public CombatActor Player;
        public EncounterDefinition Encounter;
        public TimelineAsset Opening;
        public AnimationTrack DoorTrack;
        public SignalAsset ReleaseSignal;
        public string DoorRole = "door";
        public string ZoneRole = "zone";
        public string EncounterRole = "encounter";
        public EncounterSpawner Spawner;
        public TimelineOperation Timeline;
        public QuestRun Run { get; private set; }
        public Guid AttemptId => context?.Id ?? Guid.Empty;
        public bool HasEntered { get; private set; }
        public bool Prepared { get; private set; }
        public bool RescueAvailable { get; private set; }
        public bool Rescued { get; private set; }
        public bool DialogueFinished { get; private set; }
        public bool Recovering { get; private set; }
        public string Feedback { get; private set; } = "Enter the courtyard to find your companion.";
        public int RecoveryCount { get; private set; }
        public event Action AttemptEnding;
        private readonly List<string> trace = new List<string>();
        private AttemptBindings context;
        private CapabilityCatalog capabilities;
        private SafeCheckpoint checkpoint;
        private bool preparationStarted;
        private bool encounterDefeated, watchingPlayer;
        private string companionRole;
        private Coroutine recovery;
        private IDisposable applicationStopping;
        public QuestBookAsset BookAsset { get => Book; set => Book = value; }
        public List<EntityBinding> BindingEntries => Bindings;
        public QuestRun ActiveRun => Run;
        public IReadOnlyList<string> Activity => trace;
        public IReadOnlyList<WorldSignal> Signals => new[] {
            new WorldSignal(ZoneRole, "entered", "玩家进入指定区域"),
            new WorldSignal(EncounterRole, "prepared", "生成与开门演出完成"),
            new WorldSignal(EncounterRole, "cleared", "本次遭遇必需敌人全部死亡"),
            new WorldSignal(EncounterRole, "player-defeated", "本次遭遇中玩家死亡"),
            new WorldSignal(EncounterRole, "rescued", "同伴已释放"),
            new WorldSignal(EncounterRole, "dialogue-finished", "解救对白正常结束")
        };
        [Serializable] public sealed class PrepareParameters
        { [CapabilityParameter("演出超时（秒）")] public float Timeout = 20; }
        [Serializable] public sealed class EmptyParameters { }
        private void Start()
        {
            try
            {
                ValidateScene();
                checkpoint = new SafeCheckpoint(Player.transform, Bindings.Single(b => b.Role == DoorRole).Entity.transform);
                applicationStopping = ApplicationHost.Events.Subscribe<WeaveApplicationStopping>(_ => StopScenario());
                OpenAttempt();
            }
            catch (Exception exception) { Feedback = exception.Message; Debug.LogError(Feedback, this); StopScenario(); }
        }
        private void ValidateScene()
        {
            if (!ApplicationHost || !ApplicationHost.IsRunning || !Book || !Player || !Encounter || !Timeline || !Timeline.Director || !Opening || !DoorTrack || !ReleaseSignal)
                throw new ArgumentException("Bind Startup, quest, player, encounter, Timeline and release signal.");
            var world = new QuestBindings(Bindings);
            var zone = world.Require(ZoneRole).GetComponent<RescueZone>();
            if (!zone || zone.Scenario != this) throw new ArgumentException("区域槽位需绑定本任务宿主的 RescueZone。");
            Spawner = world.Require(EncounterRole).GetComponent<EncounterSpawner>();
            if (!Spawner) throw new ArgumentException("遭遇槽位需包含 EncounterSpawner 与出生点。");
            var doorError = ValidateDoorBinding();
            if (!string.IsNullOrEmpty(doorError)) throw new ArgumentException(doorError);
            if (!Opening.GetOutputTracks().Contains(DoorTrack)) throw new ArgumentException("Door track does not belong to the selected Timeline.");
            var errors = QuestValidation.Validate(Book.Quests, role => world.Find(role), CreateCapabilities().Validate,
                (role, key, isEvent) => Signals.Any(s => s.Role == role && s.Key == key && !isEvent) ? "" : "Unknown world fact: " + role + "." + key);
            if (errors.Count != 0) throw new ArgumentException(string.Join("\n", errors));
        }
        public string ValidateDoorBinding()
        {
            var door = Bindings?.FirstOrDefault(b => b != null && b.Role == DoorRole)?.Entity;
            if (!door) return "开门演出：请绑定门对象。";
            if (door.GetComponent<Animator>()) return "";
            return $"开门演出：槽位 {DoorRole} 当前绑定场景 {door.gameObject.scene.name} 中的“{door.name}”，该对象自身没有 Animator。请在场景宿主的门绑定处定位对象；将槽位改绑到实际被动画控制的对象，或为当前门添加 Animator。子对象上的 Animator 不会自动代替当前对象。";
        }
        private void OpenAttempt()
        {
            context = new AttemptBindings();
            foreach (var binding in Bindings) context.Bind(binding.Role, binding.Entity.gameObject);
            companionRole = Encounter.CompanionRole;
            HasEntered = Prepared = RescueAvailable = Rescued = DialogueFinished = preparationStarted = false;
            encounterDefeated = false;
            capabilities = CreateCapabilities();
            Run = ApplicationHost.Services.Get<QuestService>().Open(Book.Quests, this);
            Record("Attempt opened " + context.Id); Run.Refresh(); Pump();
        }
        public CapabilityCatalog CreateCapabilities()
        {
            var catalog = new CapabilityCatalog();
            catalog.Register<PrepareParameters>("encounter.prepare-opening", "预生成遭遇并播放开门演出", p => !float.IsFinite(p.Timeout) || p.Timeout <= 0 ? "Timeout must be positive." : "", p => {
                if (context == null || context.IsClosed || Recovering) return new ActionResult(false, "No active attempt.");
                if (preparationStarted) return new ActionResult(true, "Preparation already requested.");
                preparationStarted = true;
                try
                {
                    var door = context.Find(DoorRole);
                    if (!door) throw new InvalidOperationException("Bound door is missing.");
                    Spawner.Prepare(Encounter, context);
                    Player.Died += OnPlayerDied; watchingPlayer = true;
                    Timeline.Director.SetGenericBinding(DoorTrack, door.GetComponent<Animator>());
                    Timeline.Begin(Opening, ReleaseSignal, p.Timeout);
                    Feedback = "The gate is opening. Stay ready.";
                    return new ActionResult(true, "Request accepted; completion is observed separately.");
                }
                catch (Exception exception) { Timeline.Cancel(); Spawner.Cancel(); return new ActionResult(false, exception.Message); }
            }, typeof(AreaRescueScenario));
            catalog.Register<EmptyParameters>("checkpoint.restore", "恢复进入前检查点", _ => "", _ => {
                RestoreCheckpoint(); return new ActionResult(true, "Checkpoint restore requested.");
            }, typeof(AreaRescueScenario));
            catalog.Register<EmptyParameters>("checkpoint.wait-retry", "结束遭遇，等待玩家重试", _ => "", _ => {
                CloseAttempt(); Feedback = "Defeated. Select RETRY CHECKPOINT to try again.";
                return new ActionResult(true, "Waiting for retry.");
            }, typeof(AreaRescueScenario));
            catalog.Register<EmptyParameters>("companion.allow-rescue", "允许解救本次生成的同伴", _ => "", _ => {
                if (!Companion) return new ActionResult(false, "This attempt has no companion.");
                RescueAvailable = true; Feedback = "The guards are down. Speak to your companion.";
                return new ActionResult(true, "Rescue interaction available.");
            }, typeof(AreaRescueScenario));
            return catalog;
        }
        public GameObject Companion => context?.Find(companionRole);
        public bool? ReadFact(string role, string key)
        {
            if (context == null || !context.Find(role)) return null;
            if (role == ZoneRole && key == "entered") return HasEntered;
            if (role != EncounterRole) return null;
            switch (key)
            {
                case "prepared": return Prepared;
                case "cleared": return !encounterDefeated && Spawner.IsCleared;
                case "player-defeated": return encounterDefeated;
                case "rescued": return Rescued && Companion;
                case "dialogue-finished": return DialogueFinished;
                default: return null;
            }
        }
        public void EnterZone(RescueZone zone, CombatActor actor)
        {
            if (Recovering || HasEntered || Run == null || actor != Player || !zone || context.Find(ZoneRole) != zone.gameObject) return;
            HasEntered = true; Record("Bound zone entered"); Run.Refresh(); Pump();
        }
        private void Update()
        {
            if (Recovering || Run == null || Run.IsClosed) return;
            if (preparationStarted)
            {
                if (Timeline.ReleaseRequested && Spawner.IsReady && !Spawner.Attempt.IsReleased) Spawner.Release();
                Prepared = Spawner.IsReady && Spawner.Attempt.IsReleased && Timeline.State == TimelineOperationState.Completed;
                if (!string.IsNullOrEmpty(Spawner.Error)) Feedback = "Preparation failed: " + Spawner.Error;
                else if (Timeline.State == TimelineOperationState.Failed) Feedback = Timeline.Error;
                else if (Spawner.Attempt?.HasUnavailableRequiredMember == true) Feedback = "A required guard disappeared without a confirmed defeat. Restore the checkpoint.";
            }
            Run.Refresh(); Pump();
        }
        public bool BeginRescue(Guid expectedAttempt)
        {
            if (Recovering || expectedAttempt != AttemptId || !RescueAvailable || !Companion || Rescued) return false;
            Rescued = true; Companion.transform.position = Player.transform.position + Vector3.right * 1.5f;
            Run.Refresh(); Pump(); return true;
        }
        public bool FinishRescueDialogue(Guid expectedAttempt)
        {
            if (Recovering || expectedAttempt != AttemptId || !Rescued || !Companion || DialogueFinished) return false;
            DialogueFinished = true; Feedback = "Your companion is safe."; Run.Refresh(); Pump();
            if (watchingPlayer && Run?.Instances.All(q => q.State == QuestStatus.Completed) == true)
            { Player.Died -= OnPlayerDied; watchingPlayer = false; }
            return true;
        }
        private void OnPlayerDied(CombatActor actor)
        {
            if (!watchingPlayer || actor != Player || context == null || context.IsClosed || Recovering) return;
            encounterDefeated = true;
            Record("Encounter result: player defeated");
            // The content definition decides what follows. Death itself never calls checkpoint restore.
            Run?.Refresh(); Pump();
        }
        public void RestoreCheckpoint()
        {
            if (Recovering || checkpoint == null || !ApplicationHost || !ApplicationHost.IsRunning) return;
            Recovering = true; RecoveryCount++; Feedback = "You fell. Returning to the courtyard checkpoint…";
            CloseAttempt(); recovery = StartCoroutine(RestoreNextFrame());
        }
        private IEnumerator RestoreNextFrame()
        {
            yield return null; // Let cancelled dynamic objects leave the Unity world before reopening.
            var controller = Player.GetComponent<CharacterController>();
            if (controller) controller.enabled = false;
            checkpoint.Restore(); Player.ResetHealth(); Player.CanTakeDamage = true;
            if (controller) controller.enabled = true;
            Physics.SyncTransforms();
            Recovering = false; OpenAttempt(); Feedback = "Checkpoint restored. Enter the courtyard to try again."; recovery = null;
        }
        private void Pump()
        {
            int budget = 128;
            while (Run != null && Run.TryRead(out var notice))
            {
                if (--budget == 0) throw new InvalidOperationException("Quest output budget exceeded.");
                Record(notice.StageId + " " + notice.Kind + " " + notice.Detail);
                if (notice.Kind != QuestNoticeKind.Exec || !Run.MayExecute(notice)) continue;
                var result = capabilities.Execute(notice.Execution.CapabilityId, notice.Execution.ArgumentsJson);
                Record(notice.Execution.CapabilityId + ": " + result.Message);
                if (!result.Success) Feedback = result.Message;
            }
        }
        private void Record(string text) { if (trace.Count >= 120) trace.RemoveAt(0); trace.Add(text); }
        private void CloseAttempt()
        {
            if (watchingPlayer && Player) Player.Died -= OnPlayerDied;
            watchingPlayer = false;
            context?.Dispose(); Run?.Dispose(); Run = null;
            try { AttemptEnding?.Invoke(); }
            finally { Timeline?.Cancel(); Spawner?.Cancel(); context = null; capabilities = null; }
        }
        private void StopScenario()
        {
            applicationStopping?.Dispose(); applicationStopping = null;
            if (Player) Player.Died -= OnPlayerDied;
            if (recovery != null) StopCoroutine(recovery); recovery = null;
            CloseAttempt();
        }
        private void OnDisable() => StopScenario();
    }
}
