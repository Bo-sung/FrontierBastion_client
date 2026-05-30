using UnityEngine;

namespace FrontierBastion.Client.App
{
    /// <summary>
    /// Explicit app entry point.  Attach this to a GameObject in the Preload scene.
    ///
    /// Awake bootstraps <see cref="AppRoot"/> if it does not already exist.
    /// This class does not use RuntimeInitializeOnLoadMethod and is never
    /// auto-created — it must be placed in a scene manually.
    /// No battle logic lives here.
    /// </summary>
    public sealed class Preload : MonoBehaviour
    {
        private void Awake()
        {
            if (AppRoot.Instance != null) return;

            var rootGO = new GameObject("AppRoot");
            var appRoot = rootGO.AddComponent<AppRoot>(); // AppRoot.Awake → CreateManagers (UIRoot, managers)
            appRoot.EnterStage();                         // load HUD from Resources, spawn under UIRoot
        }

#if UNITY_EDITOR
        // ── One-time editor tool ────────────────────────────────────────────────
        // Right-click this component in the Inspector → "Generate Battle Visual Prefabs"
        // to create the 5 placeholder prefabs StageBattleWorldView loads from
        // Resources/Battle. They contain only the required component (SpriteRenderer /
        // TextMesh); add sprites/effects afterward. Safe to re-run (overwrites).
        [ContextMenu("Generate Battle Visual Prefabs (one-time)")]
        private void GenerateBattleVisualPrefabs()
        {
            const string dir = "Assets/Resources/Battle";
            EnsureFolder("Assets/Resources");
            EnsureFolder(dir);

            CreateSpritePrefab(dir + "/EntityMarker.prefab", "EntityMarker");
            CreateSpritePrefab(dir + "/Projectile.prefab", "Projectile");
            CreateSpritePrefab(dir + "/BaseColumn.prefab", "BaseColumn");
            CreateTextPrefab(dir + "/FloatingDamageText.prefab", "FloatingDamageText", 40);
            CreateTextPrefab(dir + "/ResultBanner.prefab", "ResultBanner", 90);

            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();
            Debug.Log("[Preload] Battle visual prefabs generated under " + dir +
                      ". Assign sprites/effects as needed; restart Play to use them.");
        }

        private static void EnsureFolder(string path)
        {
            if (UnityEditor.AssetDatabase.IsValidFolder(path)) return;
            int idx = path.LastIndexOf('/');
            string parent = path.Substring(0, idx);
            string leaf = path.Substring(idx + 1);
            UnityEditor.AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void CreateSpritePrefab(string path, string name)
        {
            var go = new GameObject(name);
            go.AddComponent<SpriteRenderer>();
            UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, path);
            DestroyImmediate(go);
        }

        private static void CreateTextPrefab(string path, string name, int fontSize)
        {
            var go = new GameObject(name);
            var tm = go.AddComponent<TextMesh>();
            tm.fontSize = fontSize;
            tm.characterSize = 0.1f;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.fontStyle = FontStyle.Bold;
            UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, path);
            DestroyImmediate(go);
        }
#endif
    }
}
