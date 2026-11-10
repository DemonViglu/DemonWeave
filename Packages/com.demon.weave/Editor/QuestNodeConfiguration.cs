using System;
using System.Linq;
using DemonWeave.Quests;
using DemonWeave.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
namespace DemonWeave.Editor
{
    public sealed partial class QuestEditorWindow
    {
        private void BuildNodeLogic(VisualElement parent, QuestStage node)
        {
            var descriptor = QuestLogicCatalog.Find(node.LogicId);
            var row = ItemRow(parent); row.Add(new Label("Logic") { style = { minWidth = 120 } });
            var select = new Button(() => PickCatalog("Node Logic", QuestLogicCatalog.All.Select(d => new QuestCatalogPicker.Entry {
                Id = d.Id, Label = d.Name, Choose = () => {
                    if (node.LogicId == d.Id) return;
                    if (!string.IsNullOrEmpty(node.LogicId) && !EditorUtility.DisplayDialog("Change Logic", "Replace this node's parameters and output contract? Existing connections will be validated; Undo restores the change.", "Replace", "Cancel")) return;
                    Edit("Change Node Logic", () => { node.LogicId = d.Id; node.Parameters = d.Defaults(); node.Results = d.Results.ToList(); });
                }
            }))) { text = descriptor?.Name ?? (string.IsNullOrEmpty(node.LogicId) ? "Select Logic…" : "Missing: " + node.LogicId) };
            select.style.flexGrow = 1; row.Add(select);
            if (descriptor == null) return;
            IconAction(row, "{}", "Open logic code", () => OpenImplementation(descriptor.Type));
            if (!descriptor.Results.SequenceEqual(node.Results)) {
                parent.Add(new HelpBox("The code's result contract changed. Review connected ports before updating.", HelpBoxMessageType.Error));
                parent.Add(new Button(() => Edit("Update Result Contract", () => node.Results = descriptor.Results.ToList())) { text = "Update Results" });
            }
            var advanced = new Foldout { text = "Advanced", value = false };
            var defaults = descriptor.Defaults();
            foreach (var field in descriptor.Fields) {
                var display = (ShowInQuestAttribute)Attribute.GetCustomAttribute(field, typeof(ShowInQuestAttribute));
                var section = display != null && !display.Advanced ? parent : advanced;
                var name = field.Name; var label = ObjectNames.NicifyVariableName(name);
                if (QuestLogicDescriptor.ObjectType(field.FieldType) != null && !QuestLogicDescriptor.IsRequired(field)) label += " (Optional)";
                var token = node.Parameters?[name];
                void Set(JToken value) => Edit("Change " + label, () => { node.Parameters ??= new JObject(); node.Parameters[name] = value; });
                if (token == null) {
                    var missing = ItemRow(section); missing.Add(new Label(label + " — missing"));
                    missing.Add(new Button(() => Set(defaults[name].DeepClone())) { text = "Use Code Default" }); continue;
                }
                var type = field.FieldType;
                try {
                    var objectType = QuestLogicDescriptor.ObjectType(type);
                    if (objectType != null) { BuildObjectParameter(section, label, (string)token["Id"] ?? "", objectType, id => Set(new JObject { ["Id"] = id })); }
                    else if (type.IsEnum) Choice(section, label, Enum.GetNames(type).ToList(), (string)token, value => Set(new JValue(value)));
                    else if (type == typeof(string)) Text(section, label, (string)token, value => Set(new JValue(value)));
                    else if (type == typeof(bool)) { var input = new Toggle(label) { value = (bool)token }; input.RegisterValueChangedCallback(e => Set(new JValue(e.newValue))); section.Add(input); }
                    else if (type == typeof(int)) { var input = new IntegerField(label) { value = (int)token, isDelayed = true }; input.RegisterValueChangedCallback(e => Set(new JValue(e.newValue))); section.Add(input); }
                    else if (type == typeof(float)) { var input = new FloatField(label) { value = (float)token, isDelayed = true }; input.RegisterValueChangedCallback(e => Set(new JValue(e.newValue))); section.Add(input); }
                    else if (type == typeof(double)) { var input = new DoubleField(label) { value = (double)token, isDelayed = true }; input.RegisterValueChangedCallback(e => Set(new JValue(e.newValue))); section.Add(input); }
                } catch (Exception error) { section.Add(new HelpBox(label + ": " + error.Message, HelpBoxMessageType.Error)); }
            }
            if (advanced.childCount > 0) parent.Add(advanced);
            foreach (var property in node.Parameters?.Properties().ToArray() ?? Array.Empty<JProperty>())
                if (!descriptor.Fields.Any(f => f.Name == property.Name)) {
                    var obsolete = ItemRow(parent); obsolete.Add(new Label("Unknown parameter: " + property.Name));
                    IconAction(obsolete, "×", "Remove obsolete parameter", () => Edit("Remove Parameter", () => node.Parameters.Remove(property.Name)));
                }
            try { QuestLogicCatalog.Validate(node, Host == null ? null : new QuestBindings(Host.BindingEntries, hostObject.gameObject)); }
            catch (Exception error) { parent.Add(new HelpBox(error.Message, HelpBoxMessageType.Error)); }
        }
        private void BuildObjectParameter(VisualElement parent, string label, string id, Type type, Action<string> changed)
        {
            var binding = Host?.BindingEntries.FirstOrDefault(b => b.Role == id);
            var row = ItemRow(parent);
            var field = new ObjectField(label) { objectType = typeof(Component).IsAssignableFrom(type) ? typeof(GameObject) : type, allowSceneObjects = true, value = binding?.Target is Component component ? component.gameObject : binding?.Target, tooltip = type.Name + " / " + id };
            field.style.flexGrow = 1; field.style.flexShrink = 1; field.style.flexBasis = 0; field.style.minWidth = 0;
            field.labelElement.style.minWidth = 110; field.labelElement.style.width = 110;
            field.labelElement.style.flexGrow = 0;
            var input = field.Q<VisualElement>(className: "unity-base-field__input");
            if (input != null) { input.style.minWidth = 0; input.style.overflow = Overflow.Hidden; }
            row.Add(field); field.SetEnabled(Host != null);
            void Bind(UnityEngine.Object value) {
                if (Host == null) return;
                if (value) {
                    try { new QuestBindings(new[] { new ObjectBinding { Role = "selection", Object = value } }).Require("selection", type); }
                    catch (Exception error) { notice = error.Message; field.SetValueWithoutNotify(binding?.Target is Component previous ? previous.gameObject : binding?.Target); UpdateStatus(); return; }
                }
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
                try {
                    if (!value) { changed(""); return; }
                    var existing = Host.BindingEntries.FirstOrDefault(b => b.Target == value);
                    if (existing == null) {
                        Undo.RecordObject(hostObject, "Bind Gameplay Object");
                        existing = new ObjectBinding { Role = "object:" + Guid.NewGuid().ToString("N"), Object = value };
                        Host.BindingEntries.Add(existing); MarkHost();
                    }
                    changed(existing.Role);
                } finally { Undo.CollapseUndoOperations(group); }
            }
            field.RegisterValueChangedCallback(e => Bind(e.newValue));
            var browse = IconAction(row, "≡", "Browse scene hierarchy", () => {
                var current = binding?.Target as GameObject ?? (binding?.Target as Component)?.gameObject;
                UnityEditor.PopupWindow.Show(new Rect(GUIUtility.GUIToScreenPoint(new Vector2(row.worldBound.x, row.worldBound.yMax)), Vector2.one),
                    new QuestScenePicker(hostObject.gameObject.scene, type, current, go => {
                        if (!go || type == typeof(GameObject)) { Bind(go); return; }
                        var components = go.GetComponents(type);
                        if (components.Length == 1) Bind(go);
                        else if (components.Length > 1) {
                            var menu = new GenericMenu();
                            for (int i = 0; i < components.Length; i++) { var component = components[i]; menu.AddItem(new GUIContent(type.Name + " #" + (i+1)), false, () => Bind(component)); }
                            menu.ShowAsContext();
                        }
                    }));
            });
            browse.SetEnabled(Host != null && (type == typeof(GameObject) || typeof(Component).IsAssignableFrom(type)));
            IconAction(row, "◎", "Select and locate object", () => { Selection.activeObject = binding.Target; EditorGUIUtility.PingObject(binding.Target); }).SetEnabled(binding?.Target);
            if (Host == null) parent.Add(new HelpBox("Select a scene runner to resolve this object reference.", HelpBoxMessageType.Info));
            else if (!binding?.Target && !string.IsNullOrEmpty(id)) parent.Add(new HelpBox(label + ": reference missing. Choose a replacement object.", HelpBoxMessageType.Error));
        }
    }
}
