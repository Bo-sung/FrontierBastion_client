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
        // to (re)create the 5 prefabs under Resources/Battle FULLY CONFIGURED:
        // View component attached, child structure built (HP bar etc.), and every
        // SerializeField wired. Overwrites existing prefabs. Re-run anytime.
        // Replace placeholder sprites with real art afterward.
        [ContextMenu("Generate Battle Visual Prefabs (one-time)")]
        private void GenerateBattleVisualPrefabs()
        {
            const string dir = "Assets/Resources/Battle";
            EnsureFolder("Assets/Resources");
            EnsureFolder(dir);

            BuildEntityMarkerPrefab(dir + "/EntityMarker.prefab");
            BuildSpriteViewPrefab<FrontierBastion.Client.Stage.View.ProjectileView>(dir + "/Projectile.prefab", "Projectile", 0.18f);
            BuildSpriteViewPrefab<FrontierBastion.Client.Stage.View.BaseColumnView>(dir + "/BaseColumn.prefab", "BaseColumn", 1f);
            BuildTextViewPrefab<FrontierBastion.Client.Stage.View.FloatingTextView>(dir + "/FloatingDamageText.prefab", "FloatingDamageText", 40);
            BuildTextViewPrefab<FrontierBastion.Client.Stage.View.ResultBannerView>(dir + "/ResultBanner.prefab", "ResultBanner", 90);

            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();
            Debug.Log("[Preload] Battle visual prefabs generated and wired under " + dir +
                      ". Replace placeholder sprites with real art; restart Play to use them.");
        }

        private static void EnsureFolder(string path)
        {
            if (UnityEditor.AssetDatabase.IsValidFolder(path)) return;
            int idx = path.LastIndexOf('/');
            string parent = path.Substring(0, idx);
            string leaf = path.Substring(idx + 1);
            UnityEditor.AssetDatabase.CreateFolder(parent, leaf);
        }

        private static Sprite MakeWhiteSprite()
        {
            var tex = new Texture2D(4, 4);
            var cols = new Color[16];
            for (int i = 0; i < 16; i++) cols[i] = Color.white;
            tex.SetPixels(cols);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }

        // EntityMarker: body SpriteRenderer on root + HpBar/HpBarFill children, all wired.
        private static void BuildEntityMarkerPrefab(string path)
        {
            var root = new GameObject("EntityMarker");
            root.transform.localScale = new Vector3(0.28f, 0.28f, 1f); // match StageBattleWorldView.EntitySize
            var body = root.AddComponent<SpriteRenderer>();
            body.sprite = MakeWhiteSprite();

            var view = root.AddComponent<FrontierBastion.Client.Stage.View.EntityMarkerView>();

            // HpBar (root, positioned above the body) → HpBarFill (left-anchored fill)
            var hpBar = new GameObject("HpBar");
            hpBar.transform.SetParent(root.transform, false);
            hpBar.transform.localPosition = new Vector3(0f, 0.45f, 0f);

            var bg = new GameObject("HpBarBg");
            bg.transform.SetParent(hpBar.transform, false);
            var bgSr = bg.AddComponent<SpriteRenderer>();
            bgSr.sprite = MakeWhiteSprite();
            bgSr.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            bgSr.sortingOrder = 1;
            bg.transform.localScale = new Vector3(1.0f, 0.14f, 1f);

            var fill = new GameObject("HpBarFill");
            fill.transform.SetParent(hpBar.transform, false);
            var fillSr = fill.AddComponent<SpriteRenderer>();
            fillSr.sprite = MakeWhiteSprite();
            fillSr.color = new Color(0.2f, 0.9f, 0.3f, 1f);
            fillSr.sortingOrder = 2;
            // Left-anchor the fill so SetHp scale-x shrinks from the right.
            // Pivot trick: parent the fill under an anchor offset by half width.
            fill.transform.localScale = new Vector3(1.0f, 0.14f, 1f);

            // Wire serialized fields via SerializedObject (private fields).
            var so = new UnityEditor.SerializedObject(view);
            so.FindProperty("body").objectReferenceValue = body;
            so.FindProperty("spriteRenderer").objectReferenceValue = body;
            so.FindProperty("hpBarRoot").objectReferenceValue = hpBar.transform;
            so.FindProperty("hpBarFill").objectReferenceValue = fillSr;
            so.ApplyModifiedPropertiesWithoutUndo();

            // HP bar hidden by default (view shows it on hit/select at runtime).
            hpBar.SetActive(false);

            UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
        }

        // Generic sprite-based view (Projectile, BaseColumn): SpriteRenderer on root, wired.
        private static void BuildSpriteViewPrefab<T>(string path, string name, float scale) where T : Component
        {
            var root = new GameObject(name);
            root.transform.localScale = new Vector3(scale, scale, 1f);
            var sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = MakeWhiteSprite();
            var view = root.AddComponent<T>();

            var so = new UnityEditor.SerializedObject(view);
            var prop = so.FindProperty("spriteRenderer");
            if (prop != null) { prop.objectReferenceValue = sr; so.ApplyModifiedPropertiesWithoutUndo(); }

            UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
        }

        // Generic text-based view (FloatingText, ResultBanner): TextMesh on root, wired.
        private static void BuildTextViewPrefab<T>(string path, string name, int fontSize) where T : Component
        {
            var root = new GameObject(name);
            // MeshRenderer is auto-added by TextMesh.
            var tm = root.AddComponent<TextMesh>();
            tm.fontSize = fontSize;
            tm.characterSize = 0.1f;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.fontStyle = FontStyle.Bold;
            var view = root.AddComponent<T>();

            var so = new UnityEditor.SerializedObject(view);
            var prop = so.FindProperty("textMesh");
            if (prop != null) { prop.objectReferenceValue = tm; so.ApplyModifiedPropertiesWithoutUndo(); }

            UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
        }
#endif
    }
}
