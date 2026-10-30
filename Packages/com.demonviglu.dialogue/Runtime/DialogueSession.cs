using System;
using System.Collections.Generic;

namespace DemonViglu.Dialogue
{
    public interface IDialogueConditions { bool Evaluate(string conditionId); }
    public interface IDialogueCommands
    {
        // False must leave external state unchanged. Implementations must not reenter this session.
        bool TryExecute(string commandId, out string reason);
    }
    public enum DialogueState { Running, Completed, Cancelled, Faulted }
    public readonly struct DialogueOption
    {
        public readonly string Id, Text;
        public readonly bool Enabled;
        public DialogueOption(string id, string text, bool enabled) { Id = id; Text = text; Enabled = enabled; }
    }
    public sealed class DialogueSession
    {
        private readonly DialogueGraph graph;
        private readonly IDialogueConditions conditions;
        private readonly IDialogueCommands commands;
        private bool busy;
        public DialogueNode Current { get; private set; }
        public DialogueState State { get; private set; }
        public long Revision { get; private set; } = 1;
        public Exception LastError { get; private set; }
        public DialogueSession(DialogueGraph graph, IDialogueConditions conditions = null, IDialogueCommands commands = null)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph));
            this.conditions = conditions; this.commands = commands;
            Current = graph.Start;
            State = DialogueState.Running;
        }
        public IReadOnlyList<DialogueOption> GetOptions()
        {
            if (State != DialogueState.Running) return Array.Empty<DialogueOption>();
            if (busy) throw new InvalidOperationException("Dialogue callbacks must not reenter the session.");
            busy = true;
            try
            {
                var result = new List<DialogueOption>();
                foreach (var choice in Current.Choices)
                    result.Add(new DialogueOption(choice.Id, choice.Text, Allowed(choice) && (choice.CommandId == null || commands != null)));
                return result.AsReadOnly();
            }
            catch (Exception error) { Fault(error); throw; }
            finally { busy = false; }
        }
        public bool TryChoose(string choiceId, long expectedRevision, out string reason)
        {
            if (busy || State != DialogueState.Running || expectedRevision != Revision)
            { reason = "Dialogue is busy, ended, or the choice view is stale."; return false; }
            DialogueChoice selected = null;
            foreach (var choice in Current.Choices) if (choice.Id == choiceId) { selected = choice; break; }
            if (selected == null) { reason = "Choice does not exist on the current node."; return false; }
            busy = true;
            try
            {
                if (!Allowed(selected)) { reason = "Choice condition is not satisfied."; return false; }
                if (selected.CommandId != null)
                {
                    if (commands == null) { reason = "Command provider is missing."; return false; }
                    if (!commands.TryExecute(selected.CommandId, out reason)) return false;
                }
                if (selected.NextNodeId == null) State = DialogueState.Completed;
                else
                {
                    Current = graph.Get(selected.NextNodeId);
                    State = DialogueState.Running;
                }
                Revision++;
                reason = string.Empty;
                return true;
            }
            catch (Exception error) { Fault(error); throw; }
            finally { busy = false; }
        }
        public bool Cancel()
        {
            if (busy) throw new InvalidOperationException("Cannot cancel from inside a dialogue callback.");
            if (State != DialogueState.Running) return false;
            State = DialogueState.Cancelled; Revision++; return true;
        }
        // A final line stays visible until the player confirms it.
        public bool TryContinue(long expectedRevision)
        {
            if (busy || State != DialogueState.Running || Revision != expectedRevision || Current.Choices.Count != 0) return false;
            if (Current.NextNodeId == null) State = DialogueState.Completed;
            else Current = graph.Get(Current.NextNodeId);
            Revision++;
            return true;
        }
        private bool Allowed(DialogueChoice choice) => choice.ConditionId == null || conditions?.Evaluate(choice.ConditionId) == true;
        private void Fault(Exception error) { LastError = error; State = DialogueState.Faulted; Revision++; }
    }
}
