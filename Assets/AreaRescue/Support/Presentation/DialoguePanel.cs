using System;
using DemonViglu.Dialogue;
using DemonViglu.Dialogue.Unity;
using DemonViglu.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DemonWeave.Presentation
{
    // Presentation consumes a dialogue session; world commands remain owned by the caller.
    public sealed class DialoguePanel : UiView
    {
        public Text Speaker;
        public Text Line;
        public Text Status;
        public Image Portrait;
        public Transform Choices;
        public Button ChoicePrefab;
        public Button ContinueButton;
        public Button CancelButton;
        private DialogueSession session;
        private DialogueAsset asset;
        private Action<DialogueState> ended;
        public bool IsRunning => session?.State == DialogueState.Running;
        public DialogueState? State => session?.State;
        public bool Begin(DialogueAsset content, IDialogueConditions conditions, IDialogueCommands commands, Action<DialogueState> onEnd)
        {
            if (IsRunning || !IsVisible || !content) return false;
            if (!Speaker || !Line || !Status || !Choices || !ChoicePrefab || !ContinueButton)
                throw new InvalidOperationException("Bind dialogue text, choice template, choices and continue button in the view prefab.");
            var next = new DialogueSession(content.BuildGraph(), conditions, commands);
            asset = content; session = next; ended = onEnd; Status.text = "";
            if (CancelButton)
            {
                CancelButton.onClick.RemoveAllListeners();
                CancelButton.onClick.AddListener(() => { if (IsFocused && session?.Cancel() == true) End(DialogueState.Cancelled); });
            }
            Render(); return true;
        }
        private void Render()
        {
            foreach (Transform child in Choices) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            if (!IsRunning) return;
            var character = asset.FindLine(session.Current.Id)?.Speaker;
            Speaker.text = character ? character.DisplayName : "";
            Line.text = session.Current.Text;
            if (Portrait) { Portrait.sprite = character ? character.Portrait : null; Portrait.gameObject.SetActive(Portrait.sprite); }
            var revision = session.Revision;
            var currentSession = session;
            var options = session.GetOptions();
            ContinueButton.gameObject.SetActive(options.Count == 0);
            ContinueButton.onClick.RemoveAllListeners();
            ContinueButton.onClick.AddListener(() => {
                if (session != currentSession || !IsFocused) return;
                if (session.TryContinue(revision)) Refresh();
            });
            foreach (var option in options)
            {
                var button = Instantiate(ChoicePrefab, Choices); button.gameObject.SetActive(true);
                button.GetComponentInChildren<Text>().text = option.Text; button.interactable = option.Enabled;
                button.onClick.AddListener(() => {
                    if (session != currentSession || !IsFocused) return;
                    try
                    {
                        if (session.TryChoose(option.Id, revision, out var reason)) { Status.text = ""; Refresh(); }
                        else Status.text = reason;
                    }
                    catch (Exception error) { Status.text = error.Message; End(DialogueState.Faulted); }
                });
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)Choices);
            foreach (var button in Choices.GetComponentsInChildren<Button>())
            {
                var text = button.GetComponentInChildren<Text>(); var size = button.GetComponent<LayoutElement>();
                if (text && size) size.preferredHeight = Mathf.Max(62, text.preferredHeight + 22);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)Choices);
        }
        private void Refresh()
        {
            if (IsRunning) Render(); else if (session != null) End(session.State);
        }
        private void End(DialogueState result)
        {
            var callback = ended; ended = null; callback?.Invoke(result);
        }
        protected override void OnHide()
        {
            if (session?.Cancel() == true) End(DialogueState.Cancelled);
            ContinueButton?.onClick.RemoveAllListeners();
            CancelButton?.onClick.RemoveAllListeners();
        }
        private void OnDisable()
        {
            if (session?.Cancel() == true) End(DialogueState.Cancelled);
        }
    }
}
