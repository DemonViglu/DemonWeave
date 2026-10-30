using System.Collections.Generic;
using System.Linq;
using DemonWeave.Editor;
using DemonWeave.Quests;
using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.AreaRescue.Editor
{
    public sealed class RescueAuthoringProfile : QuestAuthoringProfile
    {
        public override bool Supports(IQuestAuthoringHost host) => host is AreaRescueScenario;
        public override IReadOnlyList<QuestAuthoringResource> Resources(IQuestAuthoringHost host)
        {
            var h = (AreaRescueScenario)host;
            var items = new List<QuestAuthoringResource>();
            void Slot(string role, string label, string description)
            {
                int index = h.Bindings.FindIndex(b => b.Role == role);
                if (index >= 0) items.Add(new QuestAuthoringResource { Id = role, Label = label, Description = description, Owner = h, PropertyPath = $"Bindings.Array.data[{index}].Entity" });
            }
            Slot(h.ZoneRole, "入口区域", "玩家进入此区域后开始任务。");
            Slot(h.DoorRole, "牢门", "Timeline 实际驱动的对象；需要 Animator。");
            items.Add(new QuestAuthoringResource { Id = "opening", Label = "开门演出", Owner = h, PropertyPath = "Opening", Description = "镜头与动作时序在 Timeline 内编辑。更换资产后需匹配门轨道与释放信号。" });
            items.Add(new QuestAuthoringResource { Id = "encounter-config", Label = "守卫与同伴生成配置", Owner = h, PropertyPath = "Encounter", Settings = new[] { "EnemyCount", "EnemyPrefab", "CompanionPrefab" }, SettingLabels = new[] { "守卫人数", "守卫模板", "同伴模板" } });
            items.Add(new QuestAuthoringResource { Id = "spawn-points", Label = "出生位置", Owner = h.Spawner, Settings = new[] { "SpawnPoints", "CompanionPoint" }, SettingLabels = new[] { "守卫出生点", "同伴出生点" } });
            items.Add(new QuestAuthoringResource { Id = "guards", Label = "本次生成的守卫组", ProducerCapability = "encounter.prepare-opening", Description = "由本次遭遇生成并追踪。只统计该组确认死亡的守卫；场景中其他敌人不计入。" });
            items.Add(new QuestAuthoringResource { Id = "companion", Label = "本次生成的同伴", ProducerCapability = "encounter.prepare-opening", Description = "运行时从当前遭遇的 companion 槽位解析；不需要填写实例 ID。" });
            var presentation = Object.FindObjectsByType<RescuePresentation>(FindObjectsSortMode.None).FirstOrDefault(p => p.Scenario == h);
            if (presentation) items.Add(new QuestAuthoringResource { Id = "dialogue", Label = "解救对白", Owner = presentation, PropertyPath = "RescueDialogue", Description = "选择对白资产；正常读完后才提供任务完成结果。" });
            return items;
        }
        public override IEnumerable<string> ResourcesFor(IQuestAuthoringHost host, QuestStage stage)
        {
            var h = (AreaRescueScenario)host;
            if (stage.Objectives.Any(o => o.Subject == h.ZoneRole)) yield return h.ZoneRole;
            if (stage.BeginExec.Any(e => e.CapabilityId == "encounter.prepare-opening"))
                foreach (var id in new[] { h.DoorRole, "opening", "encounter-config", "spawn-points", "guards", "companion" }) yield return id;
            if (stage.Objectives.Any(o => o.Key == "cleared")) yield return "guards";
            if (stage.Objectives.Any(o => o.Key == "rescued" || o.Key == "dialogue-finished"))
                foreach (var id in new[] { "companion", "dialogue" }) yield return id;
        }
        public override string ValidateResource(IQuestAuthoringHost host, string id)
        {
            var h = (AreaRescueScenario)host;
            if (id == h.DoorRole) return h.ValidateDoorBinding();
            if (id == "encounter-config") return h.Encounter ? h.Encounter.Validate(h.Spawner ? h.Spawner.SpawnPoints.Length : 0) : "请选择生成配置。";
            return "";
        }
        public override string ProducerFor(IQuestAuthoringHost host, QuestCondition condition)
            => condition.Kind == ConditionKind.Fact && condition.Expected && Outcomes(host, "encounter.prepare-opening").Any(s => s.Role == condition.Subject && s.Key == condition.Key) ? "encounter.prepare-opening" : null;
        public override IEnumerable<WorldSignal> Outcomes(IQuestAuthoringHost host, string capability)
            => capability == "encounter.prepare-opening" ? host.Signals.Where(s => s.Key == "prepared" || s.Key == "cleared" || s.Key == "player-defeated")
                : capability == "companion.allow-rescue" ? host.Signals.Where(s => s.Key == "rescued" || s.Key == "dialogue-finished") : Enumerable.Empty<WorldSignal>();
    }
}
