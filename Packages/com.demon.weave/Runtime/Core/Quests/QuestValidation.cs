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
            void Condition(QuestCondition rule, string path, int depth, HashSet<QuestCondition> ancestors)
            {
                if (rule == null) { Error(path, "condition.required", "Use an explicit empty All for an unconditional accept."); return; }
                if (depth > 16 || !ancestors.Add(rule)) { Error(path, "condition.recursion", "Condition nesting is cyclic or exceeds 16."); return; }
                if (!Enum.IsDefined(typeof(ConditionKind), rule.Kind)) Error(path + ".kind", "kind.unknown", "Unsupported condition kind.");
                else if (rule.Kind == ConditionKind.QuestState)
                { Reference(rule.Subject, path + ".subject"); if (!Enum.IsDefined(typeof(QuestStatus), rule.State)) Error(path + ".state", "state.invalid", "Unknown quest state."); }
                else if (rule.Kind == ConditionKind.Fact) FactOrEvent(rule.Subject, rule.Key, path);
                if (rule.Kind == ConditionKind.All || rule.Kind == ConditionKind.Any)
                {
                    if (rule.Children == null) Error(path + ".children", "required", "Children cannot be null.");
                    else for (var i = 0; i < rule.Children.Count; i++) Condition(rule.Children[i], path + ".children[" + i + "]", depth + 1, ancestors);
                    if (rule.Kind == ConditionKind.Any && (rule.Children == null || rule.Children.Count == 0)) Error(path, "any.empty", "Any requires at least one branch.");
                }
                else if (rule.Children != null && rule.Children.Count > 0) Error(path + ".children", "leaf.children", "A leaf condition cannot contain ignored children.");
                ancestors.Remove(rule);
            }
            void Contents(List<QuestContent> records, string path, HashSet<string> ids)
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
                    else FactOrEvent(record.Subject, record.Key, p, record.Kind == ContentKind.EventCount);
                    if (record.Required < 1 || (record.Kind != ContentKind.EventCount && record.Required != 1))
                        Error(p + ".required", "count.invalid", "Event count must be positive; fact/state content uses count 1.");
                }
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
                Contents(definition.Finish, path + ".finish", contentIds); Contents(definition.Fail, path + ".fail", contentIds);
                if (definition.Finish == null || definition.Finish.Count == 0) Error(path + ".finish", "finish.empty", "Completion must have an explicit rule.");
                var execIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var phase in new[] { ("beginExec", definition.BeginExec), ("finishExec", definition.FinishExec), ("failExec", definition.FailExec) })
                {
                    if (phase.Item2 == null) { Error(path + "." + phase.Item1, "required", "Exec list cannot be null."); continue; }
                    foreach (var exec in phase.Item2)
                    {
                        var p = path + "." + phase.Item1;
                        if (exec == null || string.IsNullOrWhiteSpace(exec.Id)) { Error(p, "id.required", "Exec needs a stable ID."); continue; }
                        p += "[" + exec.Id + "]";
                        if (!execIds.Add(exec.Id)) Error(p, "id.duplicate", "Exec IDs must be unique within a quest.");
                        if (string.IsNullOrWhiteSpace(exec.CapabilityId)) Error(p, "capability.required", "Select a registered capability.");
                        else if (validateCapability != null)
                        {
                            try { var error = validateCapability(exec.CapabilityId, exec.ArgumentsJson); if (!string.IsNullOrEmpty(error)) Error(p, "capability.invalid", error); }
                            catch (Exception e) { Error(p, "capability.invalid", e.Message); }
                        }
                    }
                }
            }
            return errors;
        }
    }
}
