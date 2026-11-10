using System;
using System.Collections.Generic;
using System.Linq;

namespace DemonWeave.Quests
{
    public readonly struct QuestDiagnostic
    {
        public string Path { get; }
        public string Code { get; }
        public string Message { get; }
        public QuestDiagnostic(string path, string code, string message) { Path = path; Code = code; Message = message; }
        public override string ToString() => Path + " [" + Code + "] " + Message;
    }

    public static class QuestValidation
    {
        public static IReadOnlyList<QuestDiagnostic> Validate(IReadOnlyList<QuestDefinition> definitions,
            Func<string, bool> hasLogic = null)
        {
            var errors = new List<QuestDiagnostic>();
            void Error(string path, string code, string message) => errors.Add(new QuestDiagnostic(path, code, message));
            if (definitions == null || definitions.Count == 0) { Error("quests", "required", "At least one quest is required."); return errors; }
            var byId = new Dictionary<string, QuestDefinition>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id)) { Error("quests", "id.required", "Every quest needs a stable ID."); continue; }
                if (byId.ContainsKey(definition.Id)) Error("quests[" + definition.Id + "]", "id.duplicate", "Quest IDs must be unique.");
                else byId.Add(definition.Id, definition);
            }
            void Reference(string id, string path)
            { if (string.IsNullOrEmpty(id) || !byId.ContainsKey(id)) Error(path, "quest.missing", "Referenced quest does not exist: " + id); }
            void StageReference(string id, QuestStatus state, string path, HashSet<string> stages)
            {
                if (stages == null || !stages.Contains(id ?? "")) Error(path + ".subject", "stage.missing", "Referenced local stage does not exist or is not allowed here: " + id);
                if (!Enum.IsDefined(typeof(QuestStatus), state)) Error(path + ".state", "stage.state", "Stages use Waiting/Active/Completed/Cancelled.");
            }
            void Condition(QuestCondition rule, string path, int depth, HashSet<QuestCondition> ancestors, HashSet<string> stages = null)
            {
                if (rule == null) { Error(path, "condition.required", "Use an explicit empty All for an unconditional accept."); return; }
                if (depth > 16 || !ancestors.Add(rule)) { Error(path, "condition.recursion", "Condition nesting is cyclic or exceeds 16."); return; }
                if (!Enum.IsDefined(typeof(ConditionKind), rule.Kind)) Error(path + ".kind", "kind.unknown", "Unsupported condition kind.");
                else if (rule.Kind == ConditionKind.QuestState)
                { Reference(rule.Subject, path + ".subject"); if (!Enum.IsDefined(typeof(QuestStatus), rule.State)) Error(path + ".state", "state.invalid", "Unknown quest state."); }
                else if (rule.Kind == ConditionKind.WorldFact) { if (string.IsNullOrWhiteSpace(rule.Subject)) Error(path, "world.fact", "Choose a world fact."); }
                else if (rule.Kind == ConditionKind.ExternalQuestCompleted) { if (string.IsNullOrWhiteSpace(rule.Subject) || string.IsNullOrWhiteSpace(rule.Key)) Error(path, "world.quest", "Choose a source collection and Quest."); }
                else if (rule.Kind == ConditionKind.StageState) StageReference(rule.Subject, rule.State, path, stages);
                else if (rule.Kind == ConditionKind.StageResult) {
                    StageReference(rule.Subject, QuestStatus.Completed, path, stages);
                    if (string.IsNullOrWhiteSpace(rule.Key)) Error(path + ".key", "result.required", "Select a declared result.");
                }
                if (rule.Kind == ConditionKind.All || rule.Kind == ConditionKind.Any)
                {
                    if (rule.Children == null) Error(path + ".children", "required", "Children cannot be null.");
                    else for (var i = 0; i < rule.Children.Count; i++) Condition(rule.Children[i], path + ".children[" + i + "]", depth + 1, ancestors, stages);
                    if (rule.Kind == ConditionKind.Any && (rule.Children == null || rule.Children.Count == 0)) Error(path, "any.empty", "Any requires at least one branch.");
                }
                else if (rule.Children != null && rule.Children.Count > 0) Error(path + ".children", "leaf.children", "A leaf condition cannot contain ignored children.");
                ancestors.Remove(rule);
            }
            HashSet<string> ValidateStages(QuestDefinition definition, string path)
            {
                if (!Enum.IsDefined(typeof(RuleCombination), definition.StageCompletion)) Error(path, "completion.invalid", "Use All or Any terminal completion.");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                if (definition.Stages == null) { Error(path, "required", "Stages cannot be null."); return ids; }
                foreach (var stage in definition.Stages)
                    if (stage == null || string.IsNullOrWhiteSpace(stage.Id) || !ids.Add(stage.Id)) Error(path, "stage.id", "Stages need unique nonempty IDs.");
                foreach (var stage in definition.Stages.Where(s => s != null))
                {
                    var p = path + "[" + stage.Id + "]";
                    if (string.IsNullOrWhiteSpace(stage.Title)) Error(p + ".title", "required", "Stage title required.");
                    if (stage.After == null) Error(p + ".after", "required", "After cannot be null.");
                    else foreach (var id in stage.After)
                        if (!ids.Contains(id ?? "") || id == stage.Id) Error(p + ".after", "stage.reference", "After must reference another local stage.");
                    bool Cycle(string id, HashSet<string> chain)
                    {
                        if (!chain.Add(id)) return true;
                        var target = definition.Stages.FirstOrDefault(s => s?.Id == id);
                        bool cyclic = target?.After != null && target.After.Concat(Results(target.OpenWhen).Select(r => r.Subject)).Where(a => a != null && ids.Contains(a)).Any(a => Cycle(a, chain));
                        chain.Remove(id); return cyclic;
                    }
                    if (!string.IsNullOrEmpty(stage.Id) && Cycle(stage.Id, new HashSet<string>())) Error(p + ".after", "stage.cycle", "After dependencies contain a cycle.");
                    IEnumerable<QuestCondition> Results(QuestCondition c) {
                        if (c == null) yield break;
                        if (c.Kind == ConditionKind.StageResult) yield return c;
                        if (c.Children != null) foreach (var child in c.Children) foreach (var r in Results(child)) yield return r;
                    }
                    foreach (var input in Results(stage.OpenWhen).Concat(Results(stage.RunWhen)).Concat(Results(stage.InterruptWhen))) {
                        var source = definition.Stages.FirstOrDefault(s => s?.Id == input.Subject);
                        if (source == stage || source?.Results?.Contains(input.Key) != true) Error(p + ".openWhen", "result.missing", "Result source or output is missing: " + input.Subject + "/" + input.Key);
                    }
                    if (stage.After != null) foreach (var dependency in stage.After) {
                        var source = definition.Stages.FirstOrDefault(n => n?.Id == dependency);
                        if (source?.UsesLogicExecution() == true && source.Results?.Count == 0)
                            Error(p + ".after", "result.missing", "This dependency has no completion output: " + dependency);
                    }
                    if (!Enum.IsDefined(typeof(QuestControl), stage.Control)) Error(p, "control.invalid", "Unknown control node.");
                    if (stage.Control != QuestControl.None && (stage.Results?.Count != 1 || stage.Results[0] != "Next" || !string.IsNullOrEmpty(stage.LogicId)))
                        Error(p, "control.contract", "Control nodes use one Next result and no logic implementation.");
                    if (stage.Control == QuestControl.All && Results(stage.OpenWhen).GroupBy(r => r.Subject).Any(g => g.Select(r => r.Key).Distinct().Count() > 1))
                        Error(p, "control.exclusive", "AND cannot wait for mutually exclusive results from the same node.");
                    if (!string.IsNullOrEmpty(stage.FinishResult) && stage.Results?.Contains(stage.FinishResult) != true) Error(p, "result.terminal", "Finish must use a declared result.");
                    if (stage.UsesLogicExecution() && stage.Results?.Count == 0 && stage.Required)
                        Error(p, "result.terminal", "A node logic with no outputs cannot connect to Finish.");
                    if (stage.Results?.Count > 1 && stage.Required && string.IsNullOrEmpty(stage.FinishResult)) Error(p, "result.terminal", "Choose which result reaches Finish.");
                    Condition(stage.RunWhen, p + ".runWhen", 0, new HashSet<QuestCondition>(), ids);
                    if (stage.InterruptWhen != null) Condition(stage.InterruptWhen, p + ".interruptWhen", 0, new HashSet<QuestCondition>(), ids);
                    Condition(stage.OpenWhen, p + ".openWhen", 0, new HashSet<QuestCondition>(), ids);
                    if (stage.Results == null || stage.Results.Any(string.IsNullOrWhiteSpace) || stage.Results.Distinct().Count() != stage.Results.Count) Error(p + ".results", "result.invalid", "Results must be unique nonempty names.");
                    if (stage.Parameters == null) Error(p + ".parameters", "required", "Parameters must be a JSON object.");
                    if (stage.Control == QuestControl.None && (string.IsNullOrWhiteSpace(stage.LogicId) || hasLogic != null && !hasLogic(stage.LogicId)))
                        Error(p + ".logicId", "logic.required", "Select an available logic implementation.");
                }
                return ids;
            }
            foreach (var definition in byId.Values)
            {
                var path = "quests[" + definition.Id + "]";
                if (string.IsNullOrWhiteSpace(definition.Title)) Error(path + ".title", "required", "A human-readable title is required.");
                if (!string.IsNullOrEmpty(definition.ParentId)) Reference(definition.ParentId, path + ".parentId");
                var parentIds = new HashSet<string> { definition.Id }; var parent = definition.ParentId;
                while (!string.IsNullOrEmpty(parent) && byId.TryGetValue(parent, out var ancestor))
                { if (!parentIds.Add(parent)) { Error(path + ".parentId", "parent.cycle", "Parent ownership must not form a cycle."); break; } parent = ancestor.ParentId; }
                Condition(definition.Accept, path + ".accept", 0, new HashSet<QuestCondition>());
                Condition(definition.RunWhen, path + ".runWhen", 0, new HashSet<QuestCondition>());
                if (definition.InterruptWhen != null) Condition(definition.InterruptWhen, path + ".interruptWhen", 0, new HashSet<QuestCondition>());
                ValidateStages(definition, path + ".stages");
                if (!(definition.Stages?.Any(s => s != null && s.Required) ?? false))
                    Error(path, "finish.empty", "Connect at least one declared output to Finish.");
            }
            return errors;
        }
    }
}
