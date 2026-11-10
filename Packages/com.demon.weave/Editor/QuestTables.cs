using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DemonWeave.Quests;
using DemonWeave.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace DemonWeave.Editor
{
    // CSV is a record projection of the same definition, not another executable representation.
    public static class QuestTables
    {
        private static readonly Dictionary<string,string[]> Columns = new Dictionary<string,string[]> {
            ["Quests"] = Split("QuestId,Order,Title,ParentId,CancelChildrenOnEnd,StageCompletion,StartComment,FinishComment"),
            ["Nodes"] = Split("QuestId,NodeId,Order,LogicId,Parameters,Title,Required,Results,Comment,Control,FinishResult"),
            ["Links"] = Split("QuestId,FromNode,Output,ToNode,Order"),
            ["Conditions"] = Split("QuestId,NodeId,ConditionId,ParentId,Order,Kind,Subject,Key,Expected,State,Slot")
        };
        private static string[] Split(string text) => text.Split(',');
        private sealed class Row : Dictionary<string,string>
        {
            public string Source;
            public string Get(string key) => TryGetValue(key, out var value) ? value : "";
            public int Number(string key) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : throw new FormatException(Source + "." + key + ": expected a nonnegative integer.");
        }
        private static Row Owner(string quest, string node = "", string slot = "")
            => new Row { ["QuestId"] = quest, ["NodeId"] = node, ["Slot"] = slot };
        private static Row Copy(Row row) { var result = new Row(); foreach(var pair in row) result[pair.Key] = pair.Value; return result; }
        private static void Scalars(Row row, JObject source, params string[] fields)
        { foreach(var field in fields) row[field] = source[field]?.Type == JTokenType.String ? (string)source[field] : source[field]?.ToString(Formatting.None) ?? ""; }
        public static void Export(QuestBookAsset book, string directory)
        {
            var tables = Columns.Keys.ToDictionary(k => k, _ => new List<Row>());
            void Add(string table, Row row) => tables[table].Add(row);
            void Conditions(JObject condition, Row owner, string parent = "", int order = 0)
            {
                if (condition == null) throw new FormatException("Cannot export a null condition.");
                var row = Copy(owner); row["ConditionId"] = parent == "" ? "root" : parent + "." + order; row["ParentId"] = parent; row["Order"] = order.ToString();
                Scalars(row, condition, "Kind", "Subject", "Key", "Expected", "State"); Add("Conditions", row);
                int index = 0; foreach(var child in (JArray)condition["Children"]) Conditions((JObject)child, owner, row["ConditionId"], index++);
            }
            int qi=0;
            foreach(JObject quest in JArray.Parse(book.ExportDefinitions()))
            {
                string q=(string)quest["Id"]; var owner=Owner(q); owner["Order"]=(qi++).ToString();
                Scalars(owner,quest,"Title","ParentId","CancelChildrenOnEnd","StageCompletion","StartComment","FinishComment"); Add("Quests",owner);
                Conditions((JObject)quest["Accept"],Owner(q));
                Conditions((JObject)quest["RunWhen"],Owner(q,slot:"RunWhen"));
                if(quest["InterruptWhen"] is JObject qiRule) Conditions(qiRule,Owner(q,slot:"InterruptWhen"));
                int ni=0;
                foreach(JObject node in (JArray)quest["Stages"])
                {
                    string n=(string)node["Id"]; var nr=Owner(q,n); nr["Order"]=(ni++).ToString(); Scalars(nr,node,"LogicId","Parameters","Title","Required","Results","Control","FinishResult"); nr["Comment"]=node["Comment"]?.ToString(Formatting.None) ?? "null"; Add("Nodes",nr);
                    Conditions((JObject)node["OpenWhen"],Owner(q,n));
                    Conditions((JObject)node["RunWhen"],Owner(q,n,slot:"RunWhen"));
                    if(node["InterruptWhen"] is JObject niRule) Conditions(niRule,Owner(q,n,slot:"InterruptWhen"));
                    int li=0; foreach(string previous in (JArray)node["After"]) Add("Links",new Row { ["QuestId"]=q,["FromNode"]=previous,["Output"]="Completed",["ToNode"]=n,["Order"]=(li++).ToString() });
                }
            }
            Directory.CreateDirectory(directory);
            foreach(var table in tables) Write(Path.Combine(directory,table.Key+".csv"),Columns[table.Key],table.Value);
            File.WriteAllText(Path.Combine(directory,"collection.json"),new JObject { ["Schema"]=3,["CollectionId"]=book.Id,["Title"]=book.Title }.ToString(),new UTF8Encoding(false));
        }
        public static List<QuestDefinition> Import(string directory)
        {
            var manifest=JObject.Parse(File.ReadAllText(Path.Combine(directory,"collection.json")));
            if ((int?)manifest["Schema"]!=3) throw new FormatException("collection.json: unsupported schema.");
            var tables=Columns.ToDictionary(p=>p.Key,p=>Read(Path.Combine(directory,p.Key+".csv"),p.Value));
            var consumed=new HashSet<Row>();
            Row[] Take(string table, Func<Row,bool> match)
            { var result=tables[table].Where(match).OrderBy(r=>r.Number("Order")).ToArray(); foreach(var row in result) { if(!consumed.Add(row)) throw new FormatException(row.Source+": ambiguous owner."); } return result; }
            bool Same(Row r, string q,string n="",string slot="") => r.Get("QuestId")==q && r.Get("NodeId")==n && r.Get("Slot")==slot;
            JObject Values(Row row,string idField,params string[] fields)
            {
                var result=new JObject(); if(idField!=null) result["Id"]=row.Get(idField);
                foreach(var field in fields)
                {
                    var value=row.Get(field);
                    if(field=="Required" || field=="Expected" || field=="CancelChildrenOnEnd") {
                        if(!bool.TryParse(value,out var flag)) throw new FormatException(row.Source+"."+field+": expected true or false."); result[field]=flag;
                    } else result[field]=value;
                }
                return result;
            }
            JObject Condition(string q,string n="",string slot="")
            {
                var rows=Take("Conditions",r=>Same(r,q,n,slot));
                if(rows.Length==0 && slot=="InterruptWhen") return null;
                if(rows.Length==0 && slot=="RunWhen") return new JObject { ["Kind"]="All",["Subject"]="",["Key"]="",["Expected"]=true,["State"]="Completed",["Children"]=new JArray() };
                var map=new Dictionary<string,JObject>(); var roots=new List<JObject>();
                foreach(var row in rows) {
                    if(string.IsNullOrWhiteSpace(row.Get("ConditionId")) || map.ContainsKey(row.Get("ConditionId"))) throw new FormatException(row.Source+": duplicate or missing ConditionId.");
                    var obj=Values(row,null,"Kind","Subject","Key","Expected","State"); obj["Children"]=new JArray(); map.Add(row.Get("ConditionId"),obj);
                }
                foreach(var row in rows) {
                    var obj=map[row.Get("ConditionId")]; string parent=row.Get("ParentId");
                    if(parent=="") roots.Add(obj);
                    else {
                        var seen=new HashSet<string>(); var cursor=row;
                        while(cursor.Get("ParentId")!="") {
                            if(!seen.Add(cursor.Get("ConditionId"))) throw new FormatException(row.Source+": condition cycle.");
                            cursor=rows.SingleOrDefault(r=>r.Get("ConditionId")==cursor.Get("ParentId")) ?? throw new FormatException(row.Source+": missing parent condition.");
                        }
                        ((JArray)map[parent]["Children"]).Add(obj);
                    }
                }
                if(roots.Count!=1) throw new FormatException("Conditions.csv: expected one root for "+q+"/"+n);
                return roots[0];
            }
            var quests=new JArray();
            foreach(var row in Take("Quests",_=>true))
            {
                string q=row.Get("QuestId"); var quest=Values(row,"QuestId","Title","ParentId","CancelChildrenOnEnd");
                quest["StageCompletion"]=row.Get("StageCompletion"); quest["StartComment"]=row.Get("StartComment"); quest["FinishComment"]=row.Get("FinishComment"); quest["Accept"]=Condition(q); quest["RunWhen"]=Condition(q,slot:"RunWhen"); quest["InterruptWhen"]=Condition(q,slot:"InterruptWhen");
                var nodes=new JArray(); quest["Stages"]=nodes;
                foreach(var nr in Take("Nodes",x=>x.Get("QuestId")==q)) {
                    string n=nr.Get("NodeId");
                    var node=Values(nr,"NodeId","LogicId","Title","Required"); node["Parameters"]=JObject.Parse(nr.Get("Parameters")); node["Control"]=nr.Get("Control"); node["FinishResult"]=nr.Get("FinishResult"); node["Results"]=JArray.Parse(nr.Get("Results")); node["Comment"]=JToken.Parse(nr.Get("Comment")); node["OpenWhen"]=Condition(q,n); node["RunWhen"]=Condition(q,n,slot:"RunWhen"); node["InterruptWhen"]=Condition(q,n,slot:"InterruptWhen");
                    node["After"]=new JArray(Take("Links",x=>x.Get("QuestId")==q && x.Get("ToNode")==n).Select(link=> {
                        if(link.Get("Output")!="Completed") throw new FormatException(link.Source+".Output: use Completed; fact gates belong in Conditions.csv."); return link.Get("FromNode");
                    }));
                    nodes.Add(node);
                }
                quests.Add(quest);
            }
            foreach(var row in tables.Values.SelectMany(x=>x)) if(!consumed.Contains(row)) throw new FormatException(row.Source+": owner does not exist or slot is invalid.");
            var definitions=QuestBookAsset.ParseDefinitions(quests.ToString(Formatting.None));
            var runtime=QuestBookAsset.RuntimeDefinitions(definitions);
            if(runtime.Count>0) { var errors=QuestValidation.Validate(runtime); if(errors.Count>0) throw new FormatException(string.Join("\n",errors)); }
            return definitions;
        }
        private static void Write(string path,string[] columns,List<Row> rows)
        {
            string Cell(string value) => "\""+(value??"").Replace("\"","\"\"")+"\"";
            var text=new StringBuilder(); text.AppendLine(string.Join(",",columns.Select(Cell)));
            foreach(var row in rows) text.AppendLine(string.Join(",",columns.Select(c=>Cell(row.Get(c)))));
            File.WriteAllText(path,text.ToString(),new UTF8Encoding(true));
        }
        private static List<Row> Read(string path,string[] columns)
        {
            var text=File.ReadAllText(path); var records=new List<List<string>>(); var record=new List<string>(); var cell=new StringBuilder(); bool quoted=false,closed=false;
            void EndCell() { record.Add(cell.ToString()); cell.Clear(); closed=false; }
            for(int i=0;i<text.Length;i++) {
                char c=text[i];
                if(quoted) { if(c=='"') { if(i+1<text.Length && text[i+1]=='"') {cell.Append('"');i++;} else {quoted=false;closed=true;} } else cell.Append(c); continue; }
                if(c==',') {EndCell();continue;}
                if(c=='\r'||c=='\n') {EndCell();records.Add(record);record=new List<string>();if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;continue;}
                if(closed) throw new FormatException(path+": characters after quoted cell.");
                if(c=='"') {if(cell.Length!=0)throw new FormatException(path+": quote inside unquoted cell.");quoted=true;} else cell.Append(c);
            }
            if(quoted) throw new FormatException(path+": unterminated quoted cell.");
            if(cell.Length>0||closed||record.Count>0){EndCell();records.Add(record);}
            if(records.Count==0 || !columns.Where(c => c!="StageCompletion" && c!="Results" && c!="Comment" && c!="StartComment" && c!="FinishComment" && c!="Control" && c!="FinishResult" && !(c=="Slot" && Path.GetFileName(path)=="Conditions.csv")).SequenceEqual(records[0].Where(c => c!="StageCompletion" && c!="Results" && c!="Comment" && c!="StartComment" && c!="FinishComment" && c!="Control" && c!="FinishResult" && !(c=="Slot" && Path.GetFileName(path)=="Conditions.csv"))) || records[0].Distinct().Count()!=records[0].Count || records[0].Any(c=>!columns.Contains(c))) throw new FormatException(path+": unexpected columns. Export the current schema first.");
            var rows=new List<Row>(); for(int i=1;i<records.Count;i++) {
                if(records[i].Count!=records[0].Count) throw new FormatException(path+" record "+(i+1)+": wrong column count.");
                var row=new Row {Source=Path.GetFileName(path)+" record "+(i+1)}; foreach(var column in columns) { int index=records[0].IndexOf(column); row.Add(column,index>=0?records[i][index]:column=="Results"?"[]":column=="Comment"?"null":column=="StageCompletion"?"All":column=="Control"?"None":""); } rows.Add(row);
            }
            return rows;
        }
    }
}
