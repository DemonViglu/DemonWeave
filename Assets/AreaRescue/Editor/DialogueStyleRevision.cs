using DemonWeave.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DemonWeave.AreaRescue.Editor
{
    public static class DialogueStyleRevision
    {
        private static readonly Color Gold = new Color(.96f,.78f,.38f);
        public static void Apply()
        {
            foreach (var path in new[] { "Assets/AreaRescue/Content/RescueDialogueWindow.prefab", "Packages/com.demon.weave/Editor/Templates/DialogueWindow.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var view = root.GetComponent<DialoguePanel>();
                    var frame = (RectTransform)view.Line.transform.parent;
                    Place(frame,new Vector2(.08f,.04f),new Vector2(.92f,.27f));
                    var background=frame.GetComponent<Image>(); background.color=Color.clear; background.raycastTarget=false;
                    Place((RectTransform)view.Speaker.transform,new Vector2(.1f,.80f),new Vector2(.9f,.98f));
                    view.Speaker.alignment=TextAnchor.MiddleCenter; view.Speaker.color=Gold; view.Speaker.fontSize=30;
                    var rule=frame.Find("Gold rule").GetComponent<RectTransform>(); Place(rule,new Vector2(.10f,.745f),new Vector2(.90f,.75f)); rule.GetComponent<Image>().color=Gold;
                    Place((RectTransform)view.Line.transform,new Vector2(.10f,.22f),new Vector2(.90f,.70f)); view.Line.fontSize=26; view.Line.alignment=TextAnchor.UpperCenter;
                    Place((RectTransform)view.Status.transform,new Vector2(.10f,.02f),new Vector2(.90f,.18f)); view.Status.alignment=TextAnchor.MiddleCenter;
                    foreach(var label in new[] { view.Speaker,view.Line,view.Status }) { var shadow=label.GetComponent<Shadow>() ?? label.gameObject.AddComponent<Shadow>(); shadow.effectColor=new Color(0,0,0,.75f); shadow.effectDistance=new Vector2(1,-1); }
                    var viewport=root.transform.Find("Choice viewport") as RectTransform;
                    if(!viewport) viewport=NewRect("Choice viewport",root.transform);
                    Place(viewport,new Vector2(.64f,.31f),new Vector2(.94f,.69f));
                    var hitArea=viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>(); hitArea.color=Color.clear;
                    if(!viewport.GetComponent<RectMask2D>()) viewport.gameObject.AddComponent<RectMask2D>();
                    var scroll=viewport.GetComponent<ScrollRect>() ?? viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal=false; scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=28;
                    var choices=(RectTransform)view.Choices; choices.SetParent(viewport,false); choices.anchorMin=new Vector2(0,1); choices.anchorMax=Vector2.one; choices.pivot=new Vector2(.5f,1); choices.anchoredPosition=Vector2.zero; choices.sizeDelta=Vector2.zero;
                    var layout=choices.GetComponent<VerticalLayoutGroup>(); layout.spacing=12; layout.childControlWidth=true; layout.childForceExpandWidth=true; layout.childControlHeight=true; layout.childForceExpandHeight=false;
                    var fitter=choices.GetComponent<ContentSizeFitter>() ?? choices.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
                    scroll.content=choices; scroll.viewport=viewport;
                    StyleChoice(view.ChoicePrefab);
                    view.ContinueButton.transform.SetParent(root.transform,false); Place((RectTransform)view.ContinueButton.transform,new Vector2(.81f,.035f),new Vector2(.94f,.09f));
                    view.ContinueButton.GetComponent<Image>().color=new Color(.04f,.06f,.08f,.25f);
                    view.ContinueButton.GetComponentInChildren<Text>().text="CONTINUE  ›";
                    view.CancelButton.transform.SetParent(root.transform,false); Place((RectTransform)view.CancelButton.transform,new Vector2(.94f,.91f),new Vector2(.98f,.97f));
                    view.CancelButton.GetComponent<Image>().color=new Color(.04f,.06f,.08f,.30f); view.CancelButton.GetComponentInChildren<Text>().text="×";
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var scene=EditorSceneManager.OpenScene(AreaRescueSetup.ScenePath);
            foreach(var events in Object.FindObjectsByType<EventSystem>()) if(!events.GetComponent<LegacyUiPointerBridge>()) events.gameObject.AddComponent<LegacyUiPointerBridge>();
            var shield=GameObject.Find("Modal shield"); if(shield) shield.GetComponent<Image>().color=new Color(0,0,0,.08f);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }
        private static void StyleChoice(Button button)
        {
            var image=button.GetComponent<Image>(); image.color=new Color(.12f,.16f,.20f,.72f);
            image.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); image.type=Image.Type.Sliced;
            var colors=button.colors; colors.normalColor=Color.white; colors.highlightedColor=new Color(1,.87f,.55f); colors.pressedColor=new Color(.74f,.61f,.33f); colors.selectedColor=Color.white; colors.disabledColor=new Color(.55f,.55f,.55f,.65f); button.colors=colors;
            var label=button.GetComponentInChildren<Text>(); label.alignment=TextAnchor.MiddleLeft; label.fontSize=23; label.horizontalOverflow=HorizontalWrapMode.Wrap;
            Place((RectTransform)label.transform,new Vector2(.15f,.10f),new Vector2(.96f,.90f));
            var icon=button.transform.Find("Option icon") as RectTransform; if(!icon) icon=NewRect("Option icon",button.transform);
            icon.anchorMin=new Vector2(0,.5f); icon.anchorMax=new Vector2(0,.5f); icon.pivot=new Vector2(.5f,.5f); icon.anchoredPosition=new Vector2(36,0); icon.sizeDelta=new Vector2(38,38);
            if(!icon.GetComponent<CanvasRenderer>()) icon.gameObject.AddComponent<CanvasRenderer>();
            var glyph=icon.GetComponent<DialogueOptionGlyph>() ?? icon.gameObject.AddComponent<DialogueOptionGlyph>(); glyph.color=Gold; glyph.raycastTarget=false;
            button.GetComponent<LayoutElement>().preferredHeight=64;
        }
        private static RectTransform NewRect(string name,Transform parent)
        { var value=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); value.SetParent(parent,false); return value; }
        private static void Place(RectTransform rect,Vector2 min,Vector2 max)
        { rect.anchorMin=min; rect.anchorMax=max; rect.offsetMin=rect.offsetMax=Vector2.zero; }
    }
}
