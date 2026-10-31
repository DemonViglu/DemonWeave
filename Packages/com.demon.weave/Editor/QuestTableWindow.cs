using System;
using System.IO;
using System.Linq;
using DemonWeave.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DemonWeave.Editor
{
    public sealed class QuestTableWindow : EditorWindow
    {
        private PreparedQuestChange prepared;
        private IQuestAuthoringHost host;
        public static PreparedQuestChange Prepare(QuestBookAsset book, IQuestAuthoringHost host, string directory)
        {
            var manifest = JObject.Parse(File.ReadAllText(Path.Combine(directory,"collection.json")));
            if ((string)manifest["CollectionId"] != book.Id) throw new InvalidOperationException("CollectionId does not match the selected asset.");
            var definitions = QuestTables.Import(directory);
            var types = QuestNodeCatalog.Read();
            foreach(var quest in definitions) foreach(var node in quest.Stages)
                if(node.TypeId != "core.stage" && !types.ContainsKey(node.TypeId)) throw new InvalidOperationException("Nodes.csv: unknown TypeId '"+node.TypeId+"' on "+quest.Id+"/"+node.Id);
            var oldArray = JArray.Parse(book.ExportDefinitions());
            var temp = CreateInstance<QuestBookAsset>(); JArray newArray;
            try { temp.Quests=definitions; newArray=JArray.Parse(temp.ExportDefinitions()); } finally { DestroyImmediate(temp); }
            var operations = new JArray();
            foreach(JObject oldQuest in oldArray)
                if(!newArray.OfType<JObject>().Any(q=>(string)q["Id"]==(string)oldQuest["Id"])) operations.Add(new JObject { ["Op"]="remove",["QuestId"]=oldQuest["Id"] });
            foreach(JObject quest in newArray)
            {
                string id=(string)quest["Id"]; var old=oldArray.OfType<JObject>().SingleOrDefault(q=>(string)q["Id"]==id);
                if(old==null) { operations.Add(new JObject { ["Op"]="add",["QuestId"]=id,["Value"]=quest.DeepClone() }); continue; }
                foreach(var property in quest.Properties().Where(p=>p.Name!="Id"))
                {
                    if(JToken.DeepEquals(old[property.Name],property.Value)) continue;
                    if(property.Name=="Stages" && ((JArray)old["Stages"]).Select(n=>(string)n["Id"]).SequenceEqual(((JArray)property.Value).Select(n=>(string)n["Id"])))
                    {
                        foreach(JObject node in (JArray)property.Value)
                        {
                            var oldNode=((JArray)old["Stages"]).Single(n=>(string)n["Id"]==(string)node["Id"]);
                            foreach(var field in node.Properties().Where(p=>p.Name!="Id"))
                                if(!JToken.DeepEquals(oldNode[field.Name],field.Value)) operations.Add(new JObject { ["Op"]="set-stage",["QuestId"]=id,["StageId"]=node["Id"],["Field"]=field.Name,["Value"]=field.Value.DeepClone() });
                        }
                    }
                    else operations.Add(new JObject { ["Op"]="set",["QuestId"]=id,["Field"]=property.Name,["Value"]=property.Value.DeepClone() });
                }
            }
            if(operations.Count==0) return null;
            var proposal=new JObject { ["Schema"]=1,["BookId"]=book.Id,["BaseRevision"]=QuestProposalReview.Revision(book),["Summary"]="Import CSV records",["Operations"]=operations };
            return QuestProposalReview.Prepare(book,host,proposal.ToString(Formatting.None),tableImport:true);
        }
        public static void Review(QuestBookAsset book, IQuestAuthoringHost host, string directory)
        {
            var change=Prepare(book,host,directory);
            if(change==null) { EditorUtility.DisplayDialog("Import CSV","No changes.","OK"); return; }
            var window=GetWindow<QuestTableWindow>("CSV Changes"); window.prepared=change; window.host=host; window.minSize=new Vector2(640,440); window.CreateGUI(); window.Show();
        }
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            if(prepared==null) { rootVisualElement.Add(new Label("Choose Import CSV from the Quest editor.")); return; }
            var diff=new TextField("Changes") { multiline=true,isReadOnly=true,value=prepared.Diff }; diff.style.flexGrow=1; rootVisualElement.Add(diff);
            rootVisualElement.Add(new Button(()=> {
                try { QuestProposalReview.Apply(prepared,host); Close(); }
                catch(Exception error) { EditorUtility.DisplayDialog("Import rejected",error.Message,"OK"); }
            }) { text="Apply", tooltip="Applies the reviewed definition. Undo is available." });
        }
    }
}
