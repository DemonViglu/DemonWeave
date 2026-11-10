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
            var window = GetWindow<QuestProposalWindow>("Quest AI Changes");
            window.book = book; window.hostObject = host; window.initialProposal = proposalJson;
            window.minSize = new Vector2(850, 650); window.CreateGUI(); window.Show();
        }
        public void CreateGUI()
        {
            rootVisualElement.Clear(); prepared = null;
            if (!book) { rootVisualElement.Add(new HelpBox("Open from a Quest Collection. A scene runner is only needed for object references.", HelpBoxMessageType.Info)); return; }
            var scroll = new ScrollView { style = { flexGrow = 1, paddingLeft = 16, paddingRight = 16, paddingTop = 12 } }; rootVisualElement.Add(scroll);
            scroll.Add(new Label("Propose / Review / Validate / Apply") { style = { fontSize = 20, marginBottom = 12 } });
            scroll.Add(new HelpBox("Review the diff before applying. Proposals do not execute code or edit running quests.", HelpBoxMessageType.Info));
            request = new TextField("Request") { multiline = true, value = "Change the defeat handler to wait for retry. Preserve the rescue completion requirements." };
            request.style.minHeight = 70; scroll.Add(request);
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            buttons.Add(new Button(() => { EditorGUIUtility.systemCopyBuffer = BuildPrompt(); status.text = "Copied definition, logic contracts and proposal schema. No secrets copied."; }) { text = "Copy AI Context" });
            generate = new Button(Generate) { text = "Generate Proposal" }; buttons.Add(generate);
            buttons.Add(new Button(() => pending?.Cancel()) { text = "Cancel Request" }); scroll.Add(buttons);
            status = new Label("Environment: DEMONWEAVE_LLM_ENDPOINT / DEMONWEAVE_LLM_MODEL / DEMONWEAVE_LLM_API_KEY. Secrets are not stored in assets.") { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8, marginBottom = 8 } }; scroll.Add(status);
            proposal = new TextField("Proposal JSON") { multiline = true, value = initialProposal ?? "" }; initialProposal = null;
            proposal.style.minHeight = 170; proposal.RegisterValueChangedCallback(_ => { prepared = null; apply?.SetEnabled(false); }); scroll.Add(proposal);
            scroll.Add(new Button(Review) { text = "Review Changes" });
            preview = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 12, marginBottom = 12 } }; scroll.Add(preview);
            apply = new Button(() => {
                try { QuestProposalReview.Apply(prepared, Host); prepared = null; apply.SetEnabled(false); status.text = "Applied and saved. Undo is available. The next Play session uses the new definition."; }
                catch (Exception e) { status.text = e.Message; apply.SetEnabled(false); }
            }) { text = "Apply and Save" }; apply.SetEnabled(false); scroll.Add(apply);
        }
        private void Review()
        {
            try { prepared = QuestProposalReview.Prepare(book, Host, proposal.value); preview.text = AssetDatabase.GetAssetPath(book) + "\n" + prepared.Summary + "\n\n" + prepared.Diff; status.text = "Valid proposal. Review the diff before applying."; apply.SetEnabled(!EditorApplication.isPlaying); }
            catch (Exception e) { prepared = null; preview.text = ""; status.text = "Proposal rejected: " + e.Message; apply.SetEnabled(false); }
        }
        private string BuildPrompt()
        {
            return "Edit a Quest definition using registered logic implementations and their declared Results. Return one JSON object, no C# or markdown. " +
                "Use schema {Schema:1,BookId:string,BaseRevision:string,Summary:string,Operations:[{Op:'add'|'set'|'set-stage'|'remove',QuestId:string,StageId?:string,Field?:string,Value?:JSON}]}. " +
                "add requires a complete Quest with matching Id; remove uses only Op/QuestId. set changes one Quest field; set-stage changes one node field. " +
                "Use fields present in CurrentQuests. Preserve unrelated data and IDs. Nodes execute their logic; no action lists, method names, objectives or routes. " +
                "Use StageResult conditions for output connections, WorldFact for shared state, and QuestState/ExternalQuestCompleted for dependencies. " +
                "Required and FinishResult identify outputs connected to Finish. Control nodes use Next, no LogicId. " +
                "Parameters are per-node JSON values defined by the selected LogicId schema. Use only declared field names, enum names and listed object reference IDs. Never invent references or logic. If new implementation is needed, explain it in Summary and return Operations:[].\n" +
                JsonConvert.SerializeObject(new {
                    BookId = book.Id, BaseRevision = QuestProposalReview.Revision(book), AuthorRequest = request.value,
                    CurrentQuests = JArray.Parse(book.ExportDefinitions()),
                    Logic = QuestLogicCatalog.All.Select(d => new { d.Id, d.Name, d.Results, d.Comment, Defaults = d.Defaults(), Fields = d.Fields.Select(f => new { f.Name, Type = f.FieldType.Name, Required = QuestLogicDescriptor.IsRequired(f), ObjectType = QuestLogicDescriptor.ObjectType(f.FieldType)?.FullName, Enum = f.FieldType.IsEnum ? Enum.GetNames(f.FieldType) : null }) }),
                    Owner = hostObject ? new { Name = hostObject.gameObject.name, Components = hostObject.GetComponents<Component>().Where(c => c).Select(c => c.GetType().FullName) } : null,
                    Bindings = Host?.BindingEntries.Select(b => new { Id = b.Role, Name = b.Target ? b.Target.name : "Missing", Type = b.Target ? b.Target.GetType().FullName : "Missing" })
                }, Formatting.Indented);
        }

        private static string Setting(string name) => Environment.GetEnvironmentVariable(name) ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        private async void Generate()
        {
            if (pending != null) return;
            var endpoint = Setting("DEMONWEAVE_LLM_ENDPOINT"); var model = Setting("DEMONWEAVE_LLM_MODEL"); var key = Setting("DEMONWEAVE_LLM_API_KEY");
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(key))
            { status.text = "Model configuration missing. Copy context or paste an offline proposal. No request was sent."; return; }
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.IsLoopback && uri.Scheme == "http")))
            { status.text = "Use an HTTPS chat/completions endpoint, or HTTP for localhost."; return; }
            pending = new CancellationTokenSource(); var cancellation = pending; generate.SetEnabled(false); status.text = "Generating proposal. Content unchanged.";
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
                        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Model request failed: HTTP " + (int)response.StatusCode + ". Response body and secrets were not logged.");
                        var body = await response.Content.ReadAsStringAsync(); cancellation.Token.ThrowIfCancellationRequested();
                        var value = (string)JObject.Parse(body)["choices"]?[0]?["message"]?["content"];
                        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("No proposal in response.");
                        if (!this) return;
                        proposal.value = value; status.text = "Response received. Review before applying.";
                    }
                }
            }
            catch (OperationCanceledException) { if (this) status.text = "Request cancelled or timed out. Content unchanged."; }
            catch (Exception e) { if (this) status.text = e is InvalidOperationException ? e.Message : "Request failed. Check the model configuration. Content unchanged."; }
            finally { if (ReferenceEquals(pending, cancellation)) pending = null; cancellation.Dispose(); if (this) generate?.SetEnabled(true); }
        }
        private void OnDisable() { pending?.Cancel(); }
    }
}
