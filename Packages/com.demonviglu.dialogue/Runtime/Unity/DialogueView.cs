using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DemonViglu.Dialogue.Unity
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class DialogueView : MonoBehaviour
    {
        public Text SpeakerName;
        public Text Body;
        public Image Portrait;
        public Transform ChoicesRoot;
        public Button ChoiceTemplate;
        public Button ContinueButton;
        public Button CancelButton;
        private readonly List<Button> choices = new();
        private CanvasGroup group;
        private DialoguePlayer owner;
        private bool cursorSaved;
        private bool previousCursorVisible;
        private CursorLockMode previousCursorLock;

        private void Awake() { group = GetComponent<CanvasGroup>(); SetVisible(false); }
        public bool TryOpen(DialoguePlayer player)
        {
            if (owner != null && owner != player)
            { Debug.LogWarning("[Dialogue] 此窗口已有正在播放的对话。", this); return false; }
            if (!isActiveAndEnabled || Body == null || SpeakerName == null || ChoicesRoot == null ||
                ChoiceTemplate == null || ChoiceTemplate.GetComponentInChildren<Text>(true) == null || ContinueButton == null || CancelButton == null)
            { Debug.LogError("[Dialogue] View未启用或缺少UI引用。", this); return false; }
            group ??= GetComponent<CanvasGroup>();
            owner = player;
            previousCursorVisible = Cursor.visible; previousCursorLock = Cursor.lockState; cursorSaved = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            SetVisible(true);
            return true;
        }
        public void Show(DialoguePlayer player, DialogueNode line, DialogueCharacter character, long revision)
        {
            if (owner != player) return;
            SpeakerName.text = character != null ? character.DisplayName : "";
            SpeakerName.color = character != null ? character.NameColor : Color.white;
            Body.text = line.Text;
            var scroll = Body.GetComponentInParent<ScrollRect>();
            if (scroll != null) { Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1; }
            if (Portrait != null)
            { Portrait.sprite = character != null ? character.Portrait : null; Portrait.gameObject.SetActive(Portrait.sprite != null); }
            ChoiceTemplate.gameObject.SetActive(false);
            while (choices.Count < line.Choices.Count) choices.Add(Instantiate(ChoiceTemplate, ChoicesRoot));
            for (int i = 0; i < choices.Count; i++)
            {
                var button = choices[i];
                button.onClick.RemoveAllListeners();
                button.gameObject.SetActive(i < line.Choices.Count);
                if (i >= line.Choices.Count) continue;
                var choice = line.Choices[i];
                button.GetComponentInChildren<Text>(true).text = choice.Text;
                button.onClick.AddListener(() => player.Choose(choice.Id, revision));
            }
            ContinueButton.gameObject.SetActive(line.Choices.Count == 0);
            var label = ContinueButton.GetComponentInChildren<Text>(true);
            if (label != null) label.text = line.NextNodeId == null ? "结束对话" : "继续";
            ContinueButton.onClick.RemoveAllListeners();
            ContinueButton.onClick.AddListener(() => player.Continue(revision));
            CancelButton.onClick.RemoveAllListeners(); CancelButton.onClick.AddListener(player.Cancel);
        }
        public void Close(DialoguePlayer player)
        {
            if (owner != player) return;
            owner = null;
            ContinueButton.onClick.RemoveAllListeners(); CancelButton.onClick.RemoveAllListeners();
            foreach (var button in choices) button.onClick.RemoveAllListeners();
            SetVisible(false);
            if (cursorSaved) { Cursor.lockState = previousCursorLock; Cursor.visible = previousCursorVisible; cursorSaved = false; }
        }
        private void SetVisible(bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1 : 0; group.interactable = visible; group.blocksRaycasts = visible;
        }
        private void OnDisable() { if (owner != null) owner.Cancel(); }
    }
}
