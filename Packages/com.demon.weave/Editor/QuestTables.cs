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
            ["Quests"] = Split("QuestId,Order,Title,ParentId,FinishCombination,FailCombination,CancelChildrenOnEnd"),
            ["Nodes"] = Split("QuestId,NodeId,Order,TypeId,EntityRole,Title,Required,Completion"),
            ["Links"] = Split("QuestId,FromNode,Output,ToNode,Order"),
            ["Routes"] = Split("QuestId,NodeId,RouteId,Order,Title,Completion"),
            ["Switches"] = Split("QuestId,NodeId,SwitchId,Order,From,To"),
            ["Conditions"] = Split("QuestId,NodeId,RouteId,SwitchId,ConditionId,ParentId,Order,Kind,Subject,Key,Expected,State"),
            ["Objectives"] = Split("QuestId,NodeId,RouteId,Slot,ObjectiveId,Order,Label,Kind,Subject,Key,Required,Expected,State"),
            ["Actions"] = Split("QuestId,NodeId,RouteId,Slot,ActionId,Order,CapabilityId"),
            ["Parameters"] = Split("QuestId,NodeId,RouteId,Slot,ActionId,Order,Path,Kind,Value")
        };
        private static string[] Split(string text) => text.Split(',');
        private sealed class Row : Dictionary<string,string>
        {
            public string Source;
            public string Get(string key) => TryGetValue(key, out var value) ? value : "";
            public int Number(string key) => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : throw new FormatException(Source + "." + key + ": expected a nonnegative integer.");
        }
        private static Row Owner(string quest, string node = "", string route = "", string slot = "")
            => new Row { ["QuestId"] = quest, ["NodeId"] = node, ["RouteId"] = route, ["Slot"] = slot };
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
            void Parameters(JToken token, Row owner, string path = "")
            {
                if(path == "" && token is JObject root) { foreach(var p in root.Properties()) Parameters(p.Value,owner,"/"+Escape(p.Name)); return; }
                var row = Copy(owner); row["Order"] = tables["Parameters"].Count.ToString(); row["Path"] = path; row["Kind"] = token.Type.ToString();
                row["Value"] = token.Type == JTokenType.String ? (string)token : token is JContainer ? "" : token.ToString(Formatting.None); Add("Parameters", row);
                if (token is JObject obj) foreach(var p in obj.Properties()) Parameters(p.Value, owner, path + "/" + Escape(p.Name));
                else if (token is JArray array) for(int i=0;i<array.Count;i++) Parameters(array[i], owner, path + "/" + i);
            }
            void Objectives(JArray list, Row owner)
            {
                int index=0; foreach(JObject item in list) { var row=Copy(owner); row["ObjectiveId"]=(string)item["Id"]; row["Order"]=(index++).ToString(); Scalars(row,item,"Label","Kind","Subject","Key","Required","Expected","State"); Add("Objectives",row); }
            }
            void Actions(JArray list, Row owner)
            {
                int index=0; foreach(JObject item in list) {
                    var row=Copy(owner); row["ActionId"]=(string)item["Id"]; row["Order"]=(index++).ToString(); row["CapabilityId"]=(string)item["CapabilityId"]; Add("Actions",row);
                    using(var reader=new JsonTextReader(new StringReader((string)item["ArgumentsJson"])) { DateParseHandling=DateParseHandling.None, MaxDepth=64 })
                    {
                        Parameters(JObject.Load(reader,new JsonLoadSettings { DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error }),row);
                        if(reader.Read()) throw new FormatException("Action arguments contain trailing JSON.");
                    }
                }
            }
            int qi=0;
            foreach(JObject quest in JArray.Parse(book.ExportDefinitions()))
            {
                string q=(string)quest["Id"]; var owner=Owner(q); owner["Order"]=(qi++).ToString();
                Scalars(owner,quest,"Title","ParentId","FinishCombination","FailCombination","CancelChildrenOnEnd"); Add("Quests",owner);
                Conditions((JObject)quest["Accept"],Owner(q));
                Objectives((JArray)quest["Finish"],Owner(q,slot:"Finish")); Objectives((JArray)quest["Fail"],Owner(q,slot:"Fail"));
                foreach(var slot in new[]{"BeginExec","FinishExec","FailExec"}) Actions((JArray)quest[slot],Owner(q,slot:slot));
                int ni=0;
                foreach(JObject node in (JArray)quest["Stages"])
                {
                    string n=(string)node["Id"]; var nr=Owner(q,n); nr["Order"]=(ni++).ToString(); Scalars(nr,node,"TypeId","EntityRole","Title","Required","Completion"); Add("Nodes",nr);
                    Conditions((JObject)node["OpenWhen"],Owner(q,n)); Objectives((JArray)node["Objectives"],Owner(q,n,slot:"Objectives"));
                    foreach(var slot in new[]{"BeginExec","FinishExec"}) Actions((JArray)node[slot],Owner(q,n,slot:slot));
                    int li=0; foreach(string previous in (JArray)node["After"]) Add("Links",new Row { ["QuestId"]=q,["FromNode"]=previous,["Output"]="Completed",["ToNode"]=n,["Order"]=(li++).ToString() });
                    int ri=0;
                    foreach(JObject route in (JArray)node["Routes"])
                    {
                        string r=(string)route["Id"]; var rr=Owner(q,n,r); rr["Order"]=(ri++).ToString(); Scalars(rr,route,"Title","Completion"); Add("Routes",rr);
                        Conditions((JObject)route["AvailableWhen"],Owner(q,n,r)); Objectives((JArray)route["Objectives"],Owner(q,n,r,"Objectives")); Actions((JArray)route["SelectedExec"],Owner(q,n,r,"SelectedExec"));
                    }
                    int si=0; foreach(JObject change in (JArray)node["Switches"])
                    {
                        var sr=Owner(q,n); sr["SwitchId"]=(string)change["Id"]; sr["Order"]=(si++).ToString(); Scalars(sr,change,"From","To"); Add("Switches",sr);
                        var co=Owner(q,n); co["SwitchId"]=(string)change["Id"]; Conditions((JObject)change["When"],co);
                    }
                }
            }
            Directory.CreateDirectory(directory);
            foreach(var table in tables) Write(Path.Combine(directory,table.Key+".csv"),Columns[table.Key],table.Value);
            var typeRows=new List<Row> { new Row { ["TypeId"]="core.stage",["DisplayName"]="Custom Node (built-in)",["Asset"]="",["Script"]="Packages/com.demon.weave/Runtime/Core/Quests/QuestStages.cs" } };
            foreach(var guid in AssetDatabase.FindAssets("t:QuestNodeType"))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid); var type=AssetDatabase.LoadAssetAtPath<QuestNodeType>(path); if(!type) continue;
                typeRows.Add(new Row { ["TypeId"]=type.TypeId,["DisplayName"]=type.DisplayName,["Asset"]=path,["Script"]=AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(type)) });
            }
            Write(Path.Combine(directory,"NodeTypes.reference.csv"),Split("TypeId,DisplayName,Asset,Script"),typeRows);
            File.WriteAllText(Path.Combine(directory,"collection.json"),new JObject { ["Schema"]=1,["CollectionId"]=book.Id,["Title"]=book.Title }.ToString(),new UTF8Encoding(false));
        }
        public static List<QuestDefinition> Import(string directory)
        {
            var manifest=JObject.Parse(File.ReadAllText(Path.Combine(directory,"collection.json")));
            if ((int?)manifest["Schema"]!=1) throw new FormatException("collection.json: unsupported schema.");
            var tables=Columns.ToDictionary(p=>p.Key,p=>Read(Path.Combine(directory,p.Key+".csv"),p.Value));
            var consumed=new HashSet<Row>();
            Dictionary<string, QuestNodeType> nodeTypes = null;
            Row[] Take(string table, Func<Row,bool> match)
            { var result=tables[table].Where(match).OrderBy(r=>r.Number("Order")).ToArray(); foreach(var row in result) { if(!consumed.Add(row)) throw new FormatException(row.Source+": ambiguous owner."); } return result; }
            bool Same(Row r, string q,string n="",string route="",string slot="") => r.Get("QuestId")==q && r.Get("NodeId")==n && r.Get("RouteId")==route && r.Get("Slot")==slot;
            JObject Values(Row row,string idField,params string[] fields)
            {
                var result=new JObject(); if(idField!=null) result["Id"]=row.Get(idField);
                foreach(var field in fields)
                {
                    var value=row.Get(field);
                    if(field=="Required" && idField=="ObjectiveId") result[field]=row.Number(field);
                    else if(field=="Required" || field=="Expected" || field=="CancelChildrenOnEnd") {
                        if(!bool.TryParse(value,out var flag)) throw new FormatException(row.Source+"."+field+": expected true or false."); result[field]=flag;
                    } else result[field]=value;
                }
                return result;
            }
            JObject Condition(string q,string n="",string route="",string change="")
            {
                var rows=Take("Conditions",r=>Same(r,q,n,route) && r.Get("SwitchId")==change);
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
                if(roots.Count!=1) throw new FormatException("Conditions.csv: expected one root for "+q+"/"+n+"/"+route+"/"+change);
                return roots[0];
            }
            JArray Objectives(string q,string n,string r,string slot) => new JArray(Take("Objectives",x=>Same(x,q,n,r,slot)).Select(x=>Values(x,"ObjectiveId","Label","Kind","Subject","Key","Required","Expected","State")));
            JArray Actions(string q,string n,string r,string slot)
            {
                var result=new JArray();
                foreach(var row in Take("Actions",x=>Same(x,q,n,r,slot))) {
                    var item=Values(row,"ActionId","CapabilityId");
                    var parameters=Take("Parameters",x=>Same(x,q,n,r,slot) && x.Get("ActionId")==row.Get("ActionId"));
                    item["ArgumentsJson"]=ParameterObject(parameters).ToString(Formatting.None); result.Add(item);
                }
                return result;
            }
            var quests=new JArray();
            foreach(var row in Take("Quests",_=>true))
            {
                string q=row.Get("QuestId"); var quest=Values(row,"QuestId","Title","ParentId","FinishCombination","FailCombination","CancelChildrenOnEnd");
                quest["Accept"]=Condition(q); quest["Finish"]=Objectives(q,"","","Finish"); quest["Fail"]=Objectives(q,"","","Fail");
                foreach(var slot in new[]{"BeginExec","FinishExec","FailExec"}) quest[slot]=Actions(q,"","",slot);
                var nodes=new JArray(); quest["Stages"]=nodes;
                foreach(var nr in Take("Nodes",x=>x.Get("QuestId")==q)) {
                    string n=nr.Get("NodeId"); if(string.IsNullOrWhiteSpace(nr.Get("TypeId"))) throw new FormatException(nr.Source+".TypeId: required.");
                    bool templateOnly = !new[] { "Conditions", "Objectives", "Actions", "Routes", "Switches" }.Any(table => tables[table].Any(r => r.Get("QuestId")==q && r.Get("NodeId")==n));
                    if(templateOnly)
                    {
                        var types=nodeTypes ?? (nodeTypes=QuestNodeCatalog.Read());
                        if(!types.TryGetValue(nr.Get("TypeId"),out var type)) throw new FormatException(nr.Source+": explicit records or a registered Node Type are required.");
                        var generated=type.CreateNode();
                        generated.Id=n; generated.EntityRole=nr.Get("EntityRole"); if(nr.Get("Title")!="") generated.Title=nr.Get("Title");
                        if(nr.Get("Required")!="") {
                            if(!bool.TryParse(nr.Get("Required"),out var required)) throw new FormatException(nr.Source+".Required: expected true or false."); generated.Required=required;
                        }
                        if(nr.Get("Completion")!="") {
                            if(!Enum.TryParse<RuleCombination>(nr.Get("Completion"),out var completion) || !Enum.IsDefined(typeof(RuleCombination),completion)) throw new FormatException(nr.Source+".Completion: unknown combination."); generated.Completion=completion;
                        }
                        generated.After=Take("Links",r=>r.Get("QuestId")==q && r.Get("ToNode")==n).Select(r=> {
                            if(r.Get("Output")!="Completed") throw new FormatException(r.Source+".Output: expected Completed."); return r.Get("FromNode");
                        }).ToList();
                        void OverrideArguments(List<QuestExec> actions,string route,string slot)
                        {
                            foreach(var action in actions)
                            {
                                var parameters=Take("Parameters",r=>Same(r,q,n,route,slot) && r.Get("ActionId")==action.Id);
                                if(parameters.Length==0) continue;
                                JObject arguments;
                                using(var reader=new JsonTextReader(new StringReader(action.ArgumentsJson)) { DateParseHandling=DateParseHandling.None })
                                    arguments=JObject.Load(reader,new JsonLoadSettings { DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error });
                                arguments.Merge(ParameterObject(parameters),new JsonMergeSettings { MergeArrayHandling=MergeArrayHandling.Replace, MergeNullValueHandling=MergeNullValueHandling.Merge });
                                action.ArgumentsJson=arguments.ToString(Formatting.None);
                            }
                        }
                        OverrideArguments(generated.BeginExec,"","BeginExec"); OverrideArguments(generated.FinishExec,"","FinishExec");
                        foreach(var route in generated.Routes) OverrideArguments(route.SelectedExec,route.Id,"SelectedExec");
                        nodes.Add(JObject.FromObject(generated,JsonSerializer.Create(new JsonSerializerSettings { Converters={ new Newtonsoft.Json.Converters.StringEnumConverter() } })));
                        continue;
                    }
                    var node=Values(nr,"NodeId","TypeId","EntityRole","Title","Required","Completion"); node["OpenWhen"]=Condition(q,n); node["Objectives"]=Objectives(q,n,"","Objectives");
                    foreach(var slot in new[]{"BeginExec","FinishExec"}) node[slot]=Actions(q,n,"",slot);
                    node["After"]=new JArray(Take("Links",x=>x.Get("QuestId")==q && x.Get("ToNode")==n).Select(link=> {
                        if(link.Get("Output")!="Completed") throw new FormatException(link.Source+".Output: use Completed; fact gates belong in Conditions.csv."); return link.Get("FromNode");
                    }));
                    var routes=new JArray(); node["Routes"]=routes;
                    foreach(var rr in Take("Routes",x=>x.Get("QuestId")==q && x.Get("NodeId")==n)) {
                        string r=rr.Get("RouteId"); var route=Values(rr,"RouteId","Title","Completion"); route["AvailableWhen"]=Condition(q,n,r); route["Objectives"]=Objectives(q,n,r,"Objectives"); route["SelectedExec"]=Actions(q,n,r,"SelectedExec"); routes.Add(route);
                    }
                    var switches=new JArray(); node["Switches"]=switches;
                    foreach(var sr in Take("Switches",x=>x.Get("QuestId")==q && x.Get("NodeId")==n)) {
                        var change=Values(sr,"SwitchId","From","To"); change["When"]=Condition(q,n,change:sr.Get("SwitchId")); switches.Add(change);
                    }
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
        private static string Escape(string name) => name.Replace("~","~0").Replace("/","~1");
        private static string Unescape(string name) => name.Replace("~1","/").Replace("~0","~");
        private static JObject ParameterObject(Row[] rows)
        {
            var tokens=new Dictionary<string,JToken> { [""] = new JObject() };
            foreach(var row in rows.OrderBy(r=>r.Get("Path").Count(c=>c=='/'))) {
                string path=row.Get("Path"), kind=row.Get("Kind"), value=row.Get("Value");
                if(tokens.ContainsKey(path)) throw new FormatException(row.Source+": duplicate parameter path.");
                JToken token;
                if(kind=="Object") token=new JObject(); else if(kind=="Array") token=new JArray(); else if(kind=="String") token=new JValue(value);
                else if(kind=="Integer" && long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var integer)) token=new JValue(integer);
                else if(kind=="Float" && double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number) && !double.IsNaN(number) && !double.IsInfinity(number)) token=new JValue(number);
                else if(kind=="Boolean" && bool.TryParse(value,out var flag)) token=new JValue(flag);
                else if(kind=="Null" && (value=="" || value=="null")) token=JValue.CreateNull();
                else throw new FormatException(row.Source+".Value: expected "+kind+" (invariant numeric format).");
                tokens.Add(path,token);
                if(path=="") continue;
                int slash=path.LastIndexOf('/'); if(slash<0 || !tokens.TryGetValue(path.Substring(0,slash),out var parent)) throw new FormatException(row.Source+": missing parameter parent.");
                string key=Unescape(path.Substring(slash+1));
                if(parent is JObject obj) obj.Add(key,token);
                else if(parent is JArray array && int.TryParse(key,out var index) && index>=0 && index<=10000) { while(array.Count<=index) array.Add(JValue.CreateNull()); array[index]=token; }
                else throw new FormatException(row.Source+": invalid parameter path.");
            }
            if(!tokens.TryGetValue("",out var root) || !(root is JObject result)) throw new FormatException("Parameters.csv: action requires an Object root.");
            foreach(var pair in tokens) if(pair.Value is JArray array) for(int i=0;i<array.Count;i++) if(!tokens.ContainsKey(pair.Key+"/"+i)) throw new FormatException("Parameters.csv: missing array entry.");
            return result;
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
            if(records.Count==0 || !records[0].SequenceEqual(columns)) throw new FormatException(path+": unexpected columns. Export the current schema first.");
            var rows=new List<Row>(); for(int i=1;i<records.Count;i++) {
                if(records[i].Count!=columns.Length) throw new FormatException(path+" record "+(i+1)+": wrong column count.");
                var row=new Row {Source=Path.GetFileName(path)+" record "+(i+1)}; for(int j=0;j<columns.Length;j++) row.Add(columns[j],records[i][j]); rows.Add(row);
            }
            return rows;
        }
    }
}
