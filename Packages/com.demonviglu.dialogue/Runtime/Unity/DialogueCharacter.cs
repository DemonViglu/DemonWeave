using UnityEngine;

namespace DemonViglu.Dialogue.Unity
{
    [CreateAssetMenu(menuName = "Dialogue/角色", fileName = "Character")]
    public sealed class DialogueCharacter : ScriptableObject
    {
        public string DisplayName = "角色";
        public Sprite Portrait;
        public Color NameColor = Color.white;
    }
}
