using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DemonViglu.UI
{
    [DisallowMultipleComponent]
    public sealed class UiRoot : MonoBehaviour, IUiService
    {
        [SerializeField] private Canvas _canvas;
        [SerializeField] private GraphicRaycaster _raycaster;
        [SerializeField] private Transform _hudLayer;
        [SerializeField] private Transform _screenLayer;
        [SerializeField] private Transform _modalLayer;
        [SerializeField] private GameObject _modalInputBlocker;
        [SerializeField] private EventSystem _eventSystem;
        [SerializeField] private InputSystemUIInputModule _inputModule;

        private readonly Dictionary<string, ViewState> _views = new(StringComparer.Ordinal);
        private readonly List<string> _stack = new();
        private UiCatalog _catalog;
        private readonly Dictionary<string, UiCatalog.Entry> _registered = new(StringComparer.Ordinal);
        private bool _initialized;
        private bool _blocksGameplayPointer;
        public bool BlocksGameplayInput => isActiveAndEnabled && _blocksGameplayPointer;
        private void OnDisable() { _blocksGameplayPointer = false; }
        private void OnEnable() { if (_initialized) RefreshPresentation(); }

        private void Update()
        {
            if (_initialized && _inputModule.cancel?.action?.WasPressedThisFrame() == true)
                Back();
        }

        public bool Initialize(UiCatalog catalog)
        {
            if (_initialized)
                return ReferenceEquals(_catalog, catalog);

            _catalog = catalog;
            if (!ValidateConfiguration(out var error))
            {
                Debug.LogError($"[UiRoot] {error}", this);
                return false;
            }

            _modalInputBlocker.SetActive(false);
            _initialized = true;
            return true;
        }

        public bool Show(string uiId)
        {
            if (!TryResolve(uiId, out var state)) return false;

            if (state.Entry.StackPolicy == UiStackPolicy.HideOther && !state.Open)
                SuppressOtherViews(state);

            state.Open = true;
            if (state.Entry.StackPolicy != UiStackPolicy.Normal)
            {
                _stack.Remove(uiId);
                _stack.Add(uiId);
            }

            RefreshPresentation();
            return true;
        }

        public bool Register(UiCatalog.Entry entry)
        {
            if (!_initialized || entry == null || string.IsNullOrWhiteSpace(entry.UiId) || entry.UiId != entry.UiId.Trim() ||
                entry.Prefab == null || entry.Prefab.GetComponentsInChildren<UiView>(true).Length != 1)
                return false;
            if (_catalog.TryGet(entry.UiId, out var existing) || _registered.TryGetValue(entry.UiId, out existing))
                return existing.Prefab == entry.Prefab && existing.Layer == entry.Layer && existing.Modal == entry.Modal &&
                    existing.BlocksInput == entry.BlocksInput && existing.StackPolicy == entry.StackPolicy;
            _registered.Add(entry.UiId, entry);
            return true;
        }

        public bool Hide(string uiId)
        {
            if (!_views.TryGetValue(uiId, out var state) || !state.Open)
                return false;

            CloseState(state, false);
            return true;
        }

        public bool CloseOrReturn(string uiId)
        {
            if (!_views.TryGetValue(uiId, out var state) || !state.Open)
                return false;

            CloseState(state, true);
            return true;
        }

        public bool Back()
        {
            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                var uiId = _stack[i];
                if (_views.TryGetValue(uiId, out var state) && state.Open)
                {
                    state.View.RequestBack();
                    return true;
                }
            }

            return false;
        }

        public bool IsShown(string uiId)
        {
            return _views.TryGetValue(uiId, out var state)
                   && state.Open
                   && state.SuppressionCount == 0;
        }

        public bool TryGetView(string uiId, out UiView view)
        {
            if (_views.TryGetValue(uiId, out var state))
            {
                view = state.View;
                return true;
            }

            view = null;
            return false;
        }

        private bool TryResolve(string uiId, out ViewState state)
        {
            state = null;
            if (!_initialized)
            {
                Debug.LogError("[UiRoot] UI Service 尚未初始化。", this);
                return false;
            }

            if (string.IsNullOrWhiteSpace(uiId) ||
                (!_catalog.TryGet(uiId, out var entry) && !_registered.TryGetValue(uiId, out entry)))
            {
                Debug.LogError($"[UiRoot] Ui Catalog 未注册：{uiId}", this);
                return false;
            }

            if (_views.TryGetValue(uiId, out state))
                return true;

            var parent = ResolveLayer(entry.Layer);
            var instance = Instantiate(entry.Prefab, parent, false);
            var view = instance.GetComponentInChildren<UiView>(true);
            if (view == null)
            {
                Debug.LogError($"[UiRoot] {uiId} Prefab 缺少 UiView。", instance);
                Destroy(instance);
                return false;
            }

            instance.name = entry.Prefab.name;
            view.Initialize(uiId, this);
            view.Hide();
            state = new ViewState(entry, instance, view);
            _views.Add(uiId, state);
            return true;
        }

        private void CloseState(ViewState state, bool close)
        {
            state.Open = false;
            _stack.Remove(state.Entry.UiId);
            RestoreSuppressedViews(state);
            if (close)
                state.View.Close();
            else
                state.View.Hide();
            RefreshPresentation();
        }

        private void SuppressOtherViews(ViewState owner)
        {
            owner.SuppressedIds.Clear();
            foreach (var pair in _views)
            {
                var other = pair.Value;
                if (!other.Open || other.Entry.Layer == UiLayer.Hud)
                    continue;

                other.SuppressionCount++;
                owner.SuppressedIds.Add(pair.Key);
            }
        }

        private void RestoreSuppressedViews(ViewState owner)
        {
            foreach (var uiId in owner.SuppressedIds)
            {
                if (_views.TryGetValue(uiId, out var other))
                    other.SuppressionCount = Mathf.Max(0, other.SuppressionCount - 1);
            }
            owner.SuppressedIds.Clear();
        }

        private void RefreshPresentation()
        {
            string focusedStackId = null;
            ViewState focusedStackState = null;
            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                if (_views.TryGetValue(_stack[i], out var candidate)
                    && candidate.Open
                    && candidate.SuppressionCount == 0)
                {
                    focusedStackId = _stack[i];
                    focusedStackState = candidate;
                    break;
                }
            }

            var blocksUnderlyingInput = focusedStackState?.Entry.BlocksInput == true;
            _blocksGameplayPointer = blocksUnderlyingInput;

            var hasBlockingModal = false;
            foreach (var state in _views.Values)
            {
                var shouldShow = state.Open && state.SuppressionCount == 0;
                if (shouldShow)
                {
                    state.View.Show();
                    var shouldFocus = state.Entry.StackPolicy == UiStackPolicy.Normal
                                          && !blocksUnderlyingInput
                                      || state.Entry.UiId == focusedStackId;
                    if (shouldFocus) state.View.Focus();
                    else state.View.Blur();

                    if (state.Entry.Modal && state.Entry.BlocksInput)
                        hasBlockingModal = true;
                }
                else
                {
                    state.View.Hide();
                }
            }

            _modalInputBlocker.SetActive(hasBlockingModal);
            if (hasBlockingModal)
                _modalInputBlocker.transform.SetAsFirstSibling();
        }

        private Transform ResolveLayer(UiLayer layer)
        {
            return layer switch
            {
                UiLayer.Hud => _hudLayer,
                UiLayer.Screen => _screenLayer,
                UiLayer.Modal => _modalLayer,
                _ => _screenLayer
            };
        }

        private bool ValidateConfiguration(out string error)
        {
            if (_catalog == null)
            {
                error = "缺少显式 Ui Catalog。";
                return false;
            }
            if (!_catalog.Validate(out error)) return false;
            if (_canvas == null || _raycaster == null)
            {
                error = "缺少显式 Canvas 或 GraphicRaycaster。";
                return false;
            }
            if (_hudLayer == null || _screenLayer == null || _modalLayer == null)
            {
                error = "HUD、Screen、Modal 三层必须全部显式绑定。";
                return false;
            }
            if (_modalInputBlocker == null)
            {
                error = "缺少显式 Modal 输入遮罩。";
                return false;
            }
            if (_eventSystem == null || _inputModule == null)
            {
                error = "缺少唯一 EventSystem 或 InputSystemUIInputModule。";
                return false;
            }
            if (_inputModule.actionsAsset == null)
            {
                error = "InputSystemUIInputModule 缺少 UI Actions Asset。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private sealed class ViewState
        {
            public readonly UiCatalog.Entry Entry;
            public readonly GameObject Instance;
            public readonly UiView View;
            public readonly List<string> SuppressedIds = new();
            public bool Open;
            public int SuppressionCount;

            public ViewState(UiCatalog.Entry entry, GameObject instance, UiView view)
            {
                Entry = entry;
                Instance = instance;
                View = view;
            }
        }
    }
}
