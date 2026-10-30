using UnityEngine;

namespace DemonViglu.UI
{
    [DisallowMultipleComponent]
    public abstract class UiView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;

        public string UiId { get; private set; } = string.Empty;
        public bool IsVisible { get; private set; }
        public bool IsFocused { get; private set; }
        private IUiService _service;

        internal void Initialize(string uiId, IUiService service)
        {
            UiId = uiId;
            _service = service;
            if (_canvasGroup == null)
            {
                Debug.LogError(
                    $"[UiView] {uiId} 缺少显式 CanvasGroup；请在 Prefab 中绑定。",
                    this);
            }
            ApplyInputState(false);
        }

        internal void Show()
        {
            if (IsVisible) return;
            gameObject.SetActive(true);
            IsVisible = true;
            OnShow();
        }

        internal void Hide()
        {
            if (!IsVisible)
            {
                gameObject.SetActive(false);
                return;
            }

            Blur();
            OnHide();
            IsVisible = false;
            gameObject.SetActive(false);
        }

        internal void Focus()
        {
            if (!IsVisible || IsFocused) return;
            IsFocused = true;
            ApplyInputState(true);
            OnFocus();
        }

        internal void Blur()
        {
            if (!IsFocused) return;
            IsFocused = false;
            ApplyInputState(false);
            OnBlur();
        }

        internal void Close()
        {
            var wasVisible = IsVisible;
            Hide();
            OnClose(wasVisible);
        }

        protected virtual void OnShow() { }
        protected virtual void OnHide() { }
        protected virtual void OnFocus() { }
        protected virtual void OnBlur() { }
        protected virtual void OnClose(bool wasVisible) { }
        internal void RequestBack() => OnBackRequested();
        protected virtual void OnBackRequested() => _service.CloseOrReturn(UiId);

        private void ApplyInputState(bool focused)
        {
            if (_canvasGroup == null) return;
            _canvasGroup.interactable = focused;
            _canvasGroup.blocksRaycasts = focused;
        }
    }
}
