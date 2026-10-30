using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemonViglu.Dialogue.Unity
{
    [CreateAssetMenu(menuName = "Dialogue/对话", fileName = "Conversation")]
    public sealed class DialogueAsset : ScriptableObject
    {
        [Serializable] public sealed class Option
        {
            public string Text = "选项";
            public string Next;
            public string Condition;
            public string Command;
        }
        [Serializable] public sealed class Line
        {
            [HideInInspector] public string Id = Guid.NewGuid().ToString("N");
            public DialogueCharacter Speaker;
            [TextArea(3, 8)] public string Text = "请输入台词";
            public string Next;
            public List<Option> Options = new();
        }
        public List<Line> Lines = new() { new Line() };

        public DialogueGraph BuildGraph()
        {
            if (Lines == null || Lines.Count == 0) throw new ArgumentException("对话至少需要一句台词。", name);
            var nodes = new List<DialogueNode>();
            foreach (var line in Lines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.Text)) throw new ArgumentException("对话存在空台词。", name);
                var choices = new List<DialogueChoice>();
                foreach (var option in line.Options)
                {
                    if (option == null || string.IsNullOrWhiteSpace(option.Text)) throw new ArgumentException("选项文字不能为空。", name);
                    choices.Add(new DialogueChoice(choices.Count.ToString(), option.Text, Target(option.Next), Target(option.Condition), Target(option.Command)));
                }
                nodes.Add(new DialogueNode(line.Id, line.Text, choices.Count == 0 ? Target(line.Next) : null, choices.ToArray()));
            }
            return new DialogueGraph(Lines[0].Id, nodes.ToArray());
        }
        public Line FindLine(string id) => Lines.Find(line => line != null && line.Id == id);
        private static string Target(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
