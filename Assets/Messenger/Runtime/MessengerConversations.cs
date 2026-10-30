using System;
using System.Collections.Generic;
using DemonViglu.Dialogue.Unity;
using DemonWeave.Unity;
using UnityEngine;

namespace DemonWeave.Messenger
{
    public enum ConversationCommandKind { Interact, ChooseRoute, AcceptAssignment, HandOverEnvoy }
    [CreateAssetMenu(menuName = "DemonWeave/Messenger/对白与玩法绑定")]
    public sealed class MessengerConversations : ScriptableObject
    {
        public QuestBookAsset AuthoringBook;
        [Serializable] public sealed class Entry
        {
            public string Role;
            public DialogueAsset Conversation;
        }
        [Serializable] public sealed class Command
        {
            public string Id;
            public ConversationCommandKind Kind;
            public string Role;
            public string QuestId;
            public string StageId;
            public string RouteId;
        }
        public List<Entry> Conversations = new List<Entry>();
        public List<Command> Commands = new List<Command>();
        [Serializable] public sealed class FactRequirement
        {
            public string Role;
            public string Key;
            public bool Expected = true;
        }
        [Serializable] public sealed class Condition
        {
            public string Id;
            public List<FactRequirement> All = new List<FactRequirement>();
        }
        public List<Condition> Conditions = new List<Condition>();
    }
}
