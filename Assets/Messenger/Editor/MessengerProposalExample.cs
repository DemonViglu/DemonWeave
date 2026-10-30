using System.Linq;
using DemonWeave.Editor;
using DemonWeave.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace DemonWeave.Messenger.Editor
{
    public static class MessengerProposalExample
    {
        public static string Create(QuestBookAsset book)
        {
            var temporary = ScriptableObject.CreateInstance<QuestBookAsset>();
            try
            {
                temporary.Quests = MessengerSetup.Definitions();
                var rescue = (JArray)JArray.Parse(temporary.ExportDefinitions())[0]["Stages"];
                var stage = rescue.OfType<JObject>().Single(s => (string)s["Id"] == "rescue");
                return new JObject { ["Schema"] = 1, ["BookId"] = book.Id, ["BaseRevision"] = QuestProposalReview.Revision(book),
                    ["Summary"] = "离线协议示例：谈判失败允许潜入，潜入让守卫敌对；共同安全目标和最终交接保持不变。",
                    ["Operations"] = new JArray(new[] { "Routes", "Switches" }.Select(field => new JObject {
                        ["Op"] = "set-stage", ["QuestId"] = "missing-envoy", ["StageId"] = "rescue", ["Field"] = field, ["Value"] = stage[field].DeepClone() }))
                }.ToString(Formatting.Indented);
            }
            finally { Object.DestroyImmediate(temporary); }
        }
        [MenuItem("DemonWeave/使者样本/预览离线路线修改")]
        public static void Open()
        {
            var host = Object.FindAnyObjectByType<MessengerScenario>();
            if (!host) { Debug.LogWarning("先打开失踪使者场景。"); return; }
            QuestProposalWindow.Open(host.Book, host, Create(host.Book));
        }
    }
}
