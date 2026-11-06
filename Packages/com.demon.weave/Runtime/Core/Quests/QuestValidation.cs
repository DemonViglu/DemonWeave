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
            Func<string, bool> hasRole = null, Func<string, string, string> validateCapability = null,
            Func<string, string, bool, string> validateSignal = null)
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
            void FactOrEvent(string subject, string key, string path, bool isEvent = false)
            {
                if (string.IsNullOrWhiteSpace(subject) || (hasRole != null && !hasRole(subject))) Error(path + ".subject", "binding.missing", "Unknown world role: " + subject);
                if (string.IsNullOrWhiteSpace(key)) Error(path + ".key", "key.required", "A typed fact/event key is required.");
                else if (validateSignal != null)
                { var error = validateSignal(subject, key, isEvent); if (!string.IsNullOrEmpty(error)) Error(path + ".key", "signal.unknown", error); }
            }
            void StageReference(string id, QuestStatus state, string path, HashSet<string> stages)
            {
                if (stages == null || !stages.Contains(id ?? "")) Error(path + ".subject", "stage.missing", "Referenced local stage does not exist or is not allowed here: " + id);
                if (!Enum.IsDefined(typeof(QuestStatus), state) || state == QuestStatus.Failed) Error(path + ".state", "stage.state", "Stages use Waiting/Active/Completed/Cancelled.");
            }
            void Condition(QuestCondition rule, string path, int depth, HashSet<QuestCondition> ancestors, HashSet<string> stages = null)
            {
                if (rule == null) { Error(path, "condition.required", "Use an explicit empty All for an unconditional accept."); return; }
                if (depth > 16 || !ancestors.Add(rule)) { Error(path, "condition.recursion", "Condition nesting is cyclic or exceeds 16."); return; }
                if (!Enum.IsDefined(typeof(ConditionKind), rule.Kind)) Error(path + ".kind", "kind.unknown", "Unsupported condition kind.");
                else if (rule.Kind == ConditionKind.QuestState)
                { Reference(rule.Subject, path + ".subject"); if (!Enum.IsDefined(typeof(QuestStatus), rule.State)) Error(path + ".state", "state.invalid", "Unknown quest state."); }
                else if (rule.Kind == ConditionKind.Fact) FactOrEvent(rule.Subject, rule.Key, path);
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
            void Contents(List<QuestContent> records, string path, HashSet<string> ids, HashSet<string> stages = null)
            {
                if (records == null) { Error(path, "required", "Content list cannot be null."); return; }
                foreach (var record in records)
                {
                    if (record == null || string.IsNullOrWhiteSpace(record.Id)) { Error(path, "id.required", "Content needs a stable ID."); continue; }
                    var p = path + "[" + record.Id + "]";
                    if (!ids.Add(record.Id)) Error(p, "id.duplicate", "Content ID is repeated within this quest.");
                    if (!Enum.IsDefined(typeof(ContentKind), record.Kind)) Error(p + ".kind", "kind.unknown", "Unsupported content kind.");
                    else if (record.Kind == ContentKind.QuestState)
                    { Reference(record.Subject, p + ".subject"); if (!Enum.IsDefined(typeof(QuestStatus), record.State)) Error(p + ".state", "state.invalid", "Unknown quest state."); }
                    else if (record.Kind == ContentKind.StageState) StageReference(record.Subject, record.State, p, stages);
                    else FactOrEvent(record.Subject, record.Key, p, record.Kind == ContentKind.EventCount);
                    if (record.Required < 1 || (record.Kind != ContentKind.EventCount && record.Required != 1))
                        Error(p + ".required", "count.invalid", "Event count must be positive; fact/state content uses count 1.");
                }
            }
            void ExecList(List<QuestExec> records, string path, HashSet<string> ids)
            {
                if (records == null) { Error(path, "required", "Exec list cannot be null."); return; }
                foreach (var exec in records)
                {
                    if (exec == null || string.IsNullOrWhiteSpace(exec.Id)) { Error(path, "id.required", "Exec needs a stable ID."); continue; }
                    var p = path + "[" + exec.Id + "]";
                    if (!ids.Add(exec.Id)) Error(p, "id.duplicate", "Exec ID repeated within scope.");
                    if (string.IsNullOrWhiteSpace(exec.CapabilityId)) Error(p, "capability.required", "Select a registered capability.");
                    else if (validateCapability != null)
                    {
                        try { var error = validateCapability(exec.CapabilityId, exec.ArgumentsJson); if (!string.IsNullOrEmpty(error)) Error(p, "capability.invalid", error); }
                        catch (Exception e) { Error(p, "capability.invalid", e.Message); }
                    }
                }
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
                    foreach (var input in Results(stage.OpenWhen)) {
                        var source = definition.Stages.FirstOrDefault(s => s?.Id == input.Subject);
                        if (source == stage || source?.Results?.Contains(input.Key) != true) Error(p + ".openWhen", "result.missing", "Result source or output is missing: " + input.Subject + "/" + input.Key);
                    }
                    if (!Enum.IsDefined(typeof(QuestControl), stage.Control)) Error(p, "control.invalid", "Unknown control node.");
                    if (stage.Control != QuestControl.None && (stage.Results?.Count != 1 || stage.Results[0] != "Next" || !string.IsNullOrEmpty(stage.EntityRole)))
                        Error(p, "control.contract", "Control nodes use one Next result and no Entity target.");
                    if (stage.Control == QuestControl.All && Results(stage.OpenWhen).GroupBy(r => r.Subject).Any(g => g.Select(r => r.Key).Distinct().Count() > 1))
                        Error(p, "control.exclusive", "AND cannot wait for mutually exclusive results from the same node.");
                    if (!string.IsNullOrEmpty(stage.FinishResult) && stage.Results?.Contains(stage.FinishResult) != true) Error(p, "result.terminal", "Finish must use a declared result.");
                    if (stage.Results?.Count > 1 && stage.Required && string.IsNullOrEmpty(stage.FinishResult)) Error(p, "result.terminal", "Choose which result reaches Finish.");
                    if (stage.Results?.Count > 0 && (stage.Objectives.Count > 0 || stage.BeginExec.Count > 0 || stage.FinishExec.Count > 0 || stage.Routes.Count > 0))
                        Error(p, "entity.mixed", "Entity nodes own execution and completion; remove legacy actions/objectives/routes explicitly.");
                    Condition(stage.OpenWhen, p + ".openWhen", 0, new HashSet<QuestCondition>(), ids);
                    Contents(stage.Objectives, p + ".objectives", new HashSet<string>(), ids);
                    if (stage.Results == null || stage.Results.Any(string.IsNullOrWhiteSpace) || stage.Results.Distinct().Count() != stage.Results.Count) Error(p + ".results", "result.invalid", "Results must be unique nonempty names.");
                    if ((stage.Results == null || stage.Results.Count == 0) && (stage.Objectives == null || stage.Objectives.Count == 0)) Error(p + ".objectives", "stage.empty", "A stage needs actual objectives, not only behaviour calls.");
                    if (!Enum.IsDefined(typeof(RuleCombination), stage.Completion)) Error(p + ".completion", "combination.invalid", "Use All or Any.");
                    var execIds = new HashSet<string>(); ExecList(stage.BeginExec, p + ".beginExec", execIds); ExecList(stage.FinishExec, p + ".finishExec", execIds);
                    if (stage.Routes == null || stage.Switches == null) { Error(p, "required", "Routes and Switches cannot be null."); continue; }
                    var routes = new HashSet<string>();
                    foreach (var route in stage.Routes)
                    {
                        if (route == null || string.IsNullOrWhiteSpace(route.Id) || !routes.Add(route.Id)) { Error(p + ".routes", "route.id", "Routes need unique IDs."); continue; }
                        var rp = p + ".routes[" + route.Id + "]";
                        if (string.IsNullOrWhiteSpace(route.Title)) Error(rp + ".title", "required", "Route title required.");
                        Condition(route.AvailableWhen, rp + ".availableWhen", 0, new HashSet<QuestCondition>(), ids);
                        Contents(route.Objectives, rp + ".objectives", new HashSet<string>(), ids);
                        if (!Enum.IsDefined(typeof(RuleCombination), route.Completion)) Error(rp + ".completion", "combination.invalid", "Use All or Any.");
                        ExecList(route.SelectedExec, rp + ".selectedExec", execIds);
                    }
                    var switches = new HashSet<string>();
                    foreach (var transition in stage.Switches)
                    {
                        if (transition == null || string.IsNullOrWhiteSpace(transition.Id) || !switches.Add(transition.Id)) { Error(p + ".switches", "switch.id", "Switches need unique IDs."); continue; }
                        var sp = p + ".switches[" + transition.Id + "]";
                        if (!routes.Contains(transition.From ?? "") || !routes.Contains(transition.To ?? "") || transition.From == transition.To) Error(sp, "switch.reference", "Switch must connect different existing routes.");
                        Condition(transition.When, sp + ".when", 0, new HashSet<QuestCondition>(), ids);
                    }
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
                if (!Enum.IsDefined(typeof(RuleCombination), definition.FinishCombination) || !Enum.IsDefined(typeof(RuleCombination), definition.FailCombination))
                    Error(path, "combination.invalid", "Use All or Any.");
                var contentIds = new HashSet<string>(StringComparer.Ordinal);
                var stages = ValidateStages(definition, path + ".stages");
                Contents(definition.Finish, path + ".finish", contentIds, stages); Contents(definition.Fail, path + ".fail", contentIds, stages);
                if ((definition.Finish == null || definition.Finish.Count == 0) && !(definition.Stages?.Any(s => s != null && s.Required) ?? false))
                    Error(path + ".finish", "finish.empty", "Require objectives or at least one required stage.");
                var execIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var phase in new[] { ("beginExec", definition.BeginExec), ("finishExec", definition.FinishExec), ("failExec", definition.FailExec) })
                    ExecList(phase.Item2, path + "." + phase.Item1, execIds);
            }
            return errors;
        }
    }
}
