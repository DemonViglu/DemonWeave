using System.Linq;
using DemonWeave.Quests;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    public sealed partial class QuestEditorWindow
    {
        private VisualElement breadcrumbs;
        private void OpenQuest(string id)
        {
            selectedStageId = null; selectedId = id; showStages = true;
            detailSection = "Overview"; Rebuild();
        }
        private void OpenCollection()
        {
            showStages = false; selectedStageId = null; Rebuild();
        }
        private void UpdateBreadcrumbs()
        {
            if (breadcrumbs == null) return;
            breadcrumbs.Clear();
            var collection = new Button(OpenCollection) {
                text = book ? "Quest Collection / " + (string.IsNullOrEmpty(book.Title) ? book.name : book.Title) : "Quest Collection",
                tooltip = "Return to the Quest Collection", name = "collection-breadcrumb"
            };
            breadcrumbs.Add(collection);
            var quest = book?.Quests.FirstOrDefault(q => q.Id == selectedId);
            if (showStages && quest != null) {
                breadcrumbs.Add(new Label(" / "));
                breadcrumbs.Add(new Label("Quest / " + quest.Title));
            }
        }
    }
}
