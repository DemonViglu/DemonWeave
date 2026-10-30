namespace DemonViglu.UI
{
    public interface IUiService
    {
        bool Register(UiCatalog.Entry entry);
        bool Show(string uiId);
        bool Hide(string uiId);
        bool CloseOrReturn(string uiId);
        bool Back();
        bool IsShown(string uiId);
        bool TryGetView(string uiId, out UiView view);
    }
}
