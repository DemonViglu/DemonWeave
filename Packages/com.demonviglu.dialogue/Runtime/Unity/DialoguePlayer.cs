using System;
using UnityEngine;
using UnityEngine.Events;

namespace DemonViglu.Dialogue.Unity
{
    [DisallowMultipleComponent]
    public sealed class DialoguePlayer : MonoBehaviour
    {
        public DialogueAsset Conversation;
        public DialogueView View;
        public bool PlayOnStart = true;
        public UnityEvent OnStarted = new();
        public UnityEvent OnCompleted = new();
        public UnityEvent OnCancelled = new();
        public bool IsPlaying => session?.State == DialogueState.Running;
        private DialogueSession session;
        private DialogueAsset playingAsset;
        private void Start() { if (PlayOnStart) Play(); }

        public void Play()
        {
            if (IsPlaying || !isActiveAndEnabled) return;
            if (Conversation == null || View == null)
            { Debug.LogError("[Dialogue] 请绑定Conversation和View。", this); return; }
            DialogueSession next;
            try { next = new DialogueSession(Conversation.BuildGraph()); }
            catch (Exception error) { Debug.LogError("[Dialogue] 配置无效：" + error.Message, Conversation); return; }
            if (!View.TryOpen(this)) return;
            playingAsset = Conversation;
            session = next;
            Render();
            OnStarted.Invoke();
        }
        public void Continue(long revision)
        {
            if (session != null && session.TryContinue(revision)) Refresh();
        }
        public void Choose(string id, long revision)
        {
            if (session != null && session.TryChoose(id, revision, out _)) Refresh();
        }
        private void Refresh()
        {
            if (IsPlaying) { Render(); return; }
            View.Close(this);
            OnCompleted.Invoke();
        }
        private void Render() => View.Show(this, session.Current, playingAsset.FindLine(session.Current.Id)?.Speaker, session.Revision);
        public void Cancel()
        {
            if (session?.Cancel() != true) return;
            if (View != null) View.Close(this);
            OnCancelled.Invoke();
        }
        private void OnDisable() => Cancel();
    }
}
