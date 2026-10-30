using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using DemonWeave.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    public sealed class QuestProposalWindow : EditorWindow
    {
        private QuestBookAsset book;
        private MonoBehaviour hostObject;
        private IQuestAuthoringHost Host => hostObject as IQuestAuthoringHost;
        private TextField request, proposal;
        private Label status, preview;
        private Button apply, generate;
        private PreparedQuestChange prepared;
        private CancellationTokenSource pending;
        private string initialProposal;
        public static void Open(QuestBookAsset book, MonoBehaviour host, string proposalJson = null)
        {
            var window = GetWindow<QuestProposalWindow>("任务 · AI局部修改");
            window.book = book; window.hostObject = host; window.initialProposal = proposalJson;
            window.minSize = new Vector2(850, 650); window.CreateGUI(); window.Show();
        }
        public void CreateGUI()
        {
            rootVisualElement.Clear(); prepared = null;
            if (!book || Host == null) { rootVisualElement.Add(new HelpBox("从任务编排窗口选择任务集和场景宿主后打开。", HelpBoxMessageType.Info)); return; }
            var scroll = new ScrollView { style = { flexGrow = 1, paddingLeft = 16, paddingRight = 16, paddingTop = 12 } }; rootVisualElement.Add(scroll);
            scroll.Add(new Label("AI 提议 → 局部差异 → 校验 → 明确采用") { style = { fontSize = 20, marginBottom = 12 } });
            scroll.Add(new HelpBox("此窗口不运行生成代码，也不修改运行中的任务。生成或粘贴提案不会自动保存内容；采用前会复核版本、对象、事实和行为参数。离线示例不是模型调用。", HelpBoxMessageType.Info));
            request = new TextField("作者需求") { multiline = true, value = "管理员拒绝借电池时，增加修理发电机的替代路线，保留灯塔实际亮起后才能交付的规则。" };
            request.style.minHeight = 70; scroll.Add(request);
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            buttons.Add(new Button(() => { EditorGUIUtility.systemCopyBuffer = BuildPrompt(); status.text = "已复制当前定义、能力目录和提案格式，可交给外部 AI。没有复制密钥。"; }) { text = "复制 AI 上下文" });
            generate = new Button(Generate) { text = "调用本机配置的模型" }; buttons.Add(generate);
            buttons.Add(new Button(() => pending?.Cancel()) { text = "取消调用" }); scroll.Add(buttons);
            status = new Label("配置：DEMONWEAVE_LLM_ENDPOINT / DEMONWEAVE_LLM_MODEL / DEMONWEAVE_LLM_API_KEY。密钥只读环境变量，不保存到资产。") { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8, marginBottom = 8 } }; scroll.Add(status);
            proposal = new TextField("提案 JSON") { multiline = true, value = initialProposal ?? "" }; initialProposal = null;
            proposal.style.minHeight = 170; proposal.RegisterValueChangedCallback(_ => { prepared = null; apply?.SetEnabled(false); }); scroll.Add(proposal);
            scroll.Add(new Button(Review) { text = "检查并预览局部变更" });
            preview = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 12, marginBottom = 12 } }; scroll.Add(preview);
            apply = new Button(() => {
                try { QuestProposalReview.Apply(prepared, Host); prepared = null; apply.SetEnabled(false); status.text = "已采用并保存，可 Undo。返回编排窗口查看关系，下一次 Play 使用新定义。"; }
                catch (Exception e) { status.text = e.Message; apply.SetEnabled(false); }
            }) { text = "采用已审查变更并保存" }; apply.SetEnabled(false); scroll.Add(apply);
        }
        private void Review()
        {
            try { prepared = QuestProposalReview.Prepare(book, Host, proposal.value); preview.text = prepared.Summary + "\n\n" + prepared.Diff; status.text = "校验通过。请审阅下方差异；尚未修改定义。"; apply.SetEnabled(!EditorApplication.isPlaying); }
            catch (Exception e) { prepared = null; preview.text = ""; status.text = "拒绝提案：" + e.Message; apply.SetEnabled(false); }
        }
        private string BuildPrompt()
        {
            var catalog = Host.CreateCapabilities();
            return "You are editing a local game quest definition. Return ONLY one JSON object; do not output C# or markdown. " +
                "Use schema 1: {Schema:1,BookId:string,BaseRevision:string,Summary:string,Operations:[{Op:'add'|'set'|'remove',QuestId:string,Field?:string,Value?:JSON}]}. " +
                "add uses a complete new quest as Value with matching Id and no Field; remove uses only Op/QuestId. " +
                "set replaces ONE field of an existing quest. Allowed fields: Title,ParentId,Accept,FinishCombination,FailCombination,Finish,Fail,BeginExec,FinishExec,FailExec,CancelChildrenOnEnd,Stages. " +
                "For an existing stage prefer Op:'set-stage', QuestId, StageId, Field, Value. Allowed stage fields: Title,Required,After,OpenWhen,Objectives,Completion,BeginExec,FinishExec,Routes,Switches. This edits one stage field without replacing all stages. Stages belong inside a quest; never split them into unrelated quests. Each stage's Objectives are its completion evidence, All by default; do not duplicate them in quest Finish. " +
                "Routes are explicit exclusive choices; switching requires From/To/When. World consequences belong to registered capabilities and actual facts, not fabricated completion. " +
                "Preserve unrelated content and stable IDs. Use only provided roles, facts/events and capabilities. " +
                "A completed action/dialogue is not a completed quest. Quest state dependencies are not execution order. " +
                "Use named enums exactly as current definitions. If no implementation exists, explain in Summary and return Operations:[]; never invent executable abilities.\n" +
                JsonConvert.SerializeObject(new {
                    BookId = book.Id, BaseRevision = QuestProposalReview.Revision(book), AuthorRequest = request.value,
                    CurrentQuests = JArray.Parse(book.ExportDefinitions()),
                    Bindings = Host.BindingEntries.Select(b => new { b.Role, ObjectName = b.Entity ? b.Entity.DisplayName : "missing" }),
                    Signals = Host.Signals,
                    Capabilities = catalog.Entries.Select(c => new { c.Id, c.Label, Parameters = JObject.Parse(c.DefaultJson()) })
                }, Formatting.Indented);
        }
        private static string Setting(string name) => Environment.GetEnvironmentVariable(name) ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        private async void Generate()
        {
            if (pending != null) return;
            var endpoint = Setting("DEMONWEAVE_LLM_ENDPOINT"); var model = Setting("DEMONWEAVE_LLM_MODEL"); var key = Setting("DEMONWEAVE_LLM_API_KEY");
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(key))
            { status.text = "缺少本机模型配置。仍可复制上下文给 AI，或粘贴离线提案审查；没有发送网络请求。"; return; }
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.IsLoopback && uri.Scheme == "http")))
            { status.text = "模型地址需为 HTTPS chat/completions 完整地址，本机回环服务可用 HTTP。"; return; }
            pending = new CancellationTokenSource(); var cancellation = pending; generate.SetEnabled(false); status.text = "正在生成提案；尚未修改任务。";
            try
            {
                var prompt = BuildPrompt();
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60), MaxResponseContentBufferSize = 400000 })
                using (var message = new HttpRequestMessage(HttpMethod.Post, uri))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    message.Content = new StringContent(JsonConvert.SerializeObject(new { model, messages = new[] { new { role = "user", content = prompt } }, temperature = .2 }), Encoding.UTF8, "application/json");
                    using (var response = await client.SendAsync(message, cancellation.Token))
                    {
                        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("模型请求失败，HTTP " + (int)response.StatusCode + "。未记录响应正文或密钥。");
                        var body = await response.Content.ReadAsStringAsync(); cancellation.Token.ThrowIfCancellationRequested();
                        var value = (string)JObject.Parse(body)["choices"]?[0]?["message"]?["content"];
                        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("响应没有提案正文。");
                        if (!this) return;
                        proposal.value = value; status.text = "已收到真实模型响应；点击检查并预览，尚未采用。";
                    }
                }
            }
            catch (OperationCanceledException) { if (this) status.text = "调用已取消或超时，任务未修改。"; }
            catch (Exception e) { if (this) status.text = e is InvalidOperationException ? e.Message : "调用失败；请检查模型端点与配置。任务未修改。"; }
            finally { if (ReferenceEquals(pending, cancellation)) pending = null; cancellation.Dispose(); if (this) generate?.SetEnabled(true); }
        }
        private void OnDisable() { pending?.Cancel(); }
    }
}
