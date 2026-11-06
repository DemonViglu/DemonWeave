using UnityEngine;

namespace DemonWeave.World
{
    [CreateAssetMenu(menuName = "DemonWeave/遭遇配置")]
    public sealed class EncounterDefinition : ScriptableObject
    {
        public CombatActor EnemyPrefab;
        [Min(1)] public int EnemyCount = 3;
        public GameObject CompanionPrefab;
        public string CompanionRole = "companion";
        public string Validate(int availableSpawnPoints)
        {
            if (!EnemyPrefab || !CompanionPrefab) return "指定敌人和同伴 Prefab。";
            if (EnemyCount < 1 || EnemyCount > availableSpawnPoints) return "敌人数必须为正，且出生点数量足够。";
            if (string.IsNullOrWhiteSpace(CompanionRole)) return "同伴需要动态对象槽位。";
            return "";
        }
    }
}
