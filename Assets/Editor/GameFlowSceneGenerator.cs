#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;
using FrontierBastion.Client.App;
using FrontierBastion.Client.UI;

namespace FrontierBastion.Client.EditorTools
{
    /// <summary>
    /// One-time generator for the screen-flow skeleton:
    ///   • Scenes: Preload, MainMenu, StageSelect, Battle (under Assets/Scenes)
    ///   • UI prefabs: UI_MainMenu, UI_StageSelect (under Assets/Resources/UI), wired.
    ///
    /// After running, add the four scenes to File > Build Settings (Preload first).
    /// Re-running overwrites the generated assets.
    /// </summary>
    public static class GameFlowSceneGenerator
    {
        private const string ScenesDir = "Assets/Scenes";
        private const string UiResDir  = "Assets/Resources/UI";

        [MenuItem("FrontierBastion/Generate Game Flow Scenes + Menu UI")]
        public static void Generate()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder(UiResDir);
            EnsureFolder(ScenesDir);

            // UI prefabs first (scenes don't reference them directly, but keep order tidy).
            BuildMainMenuPrefab();
            BuildStageSelectPrefab();

            // Scenes.
            BuildPreloadScene();
            BuildEmptyScene("MainMenu");
            BuildEmptyScene("StageSelect");
            BuildEmptyScene("Battle");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GameFlowSceneGenerator] Generated 4 scenes + 2 menu UI prefabs. " +
                      "Now add Scenes/Preload, MainMenu, StageSelect, Battle to Build Settings (Preload first).");
        }

        // ── Scenes ───────────────────────────────────────────────────────────────

        private static void BuildPreloadScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Preload");
            go.AddComponent<Preload>();
            EditorSceneManager.SaveScene(scene, $"{ScenesDir}/Preload.unity");
        }

        private static void BuildEmptyScene(string name)
        {
            // Empty: GameFlowManager (DontDestroyOnLoad) carries UIRoot/EventSystem across
            // loads, and BattleWorldView spawns its own camera. Kept minimal on purpose.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, $"{ScenesDir}/{name}.unity");
        }

        // ── UI prefabs ─────────────────────────────────────────────────────────────

        private static void BuildMainMenuPrefab()
        {
            var root = NewPanelRoot("UI_MainMenu");
            AddBackground(root.transform, new Color(0.07f, 0.08f, 0.12f, 1f));
            var view = root.AddComponent<UI_MainMenu>();

            MakeTitle("Title", root.transform, new Vector2(0, 200), "FRONTIER BASTION", 64);

            var startBtn = MakeButton("StartButton", root.transform, new Vector2(0, 0),   "START");
            var quitBtn  = MakeButton("QuitButton",  root.transform, new Vector2(0, -70), "QUIT");

            var so = new SerializedObject(view);
            so.FindProperty("startButton").objectReferenceValue = startBtn;
            so.FindProperty("quitButton").objectReferenceValue  = quitBtn;
            so.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, $"{UiResDir}/UI_MainMenu.prefab");
        }

        private static void BuildStageSelectPrefab()
        {
            var root = NewPanelRoot("UI_StageSelect");
            AddBackground(root.transform, new Color(0.06f, 0.10f, 0.10f, 1f));
            var view = root.AddComponent<UI_StageSelect>();

            MakeTitle("Title", root.transform, new Vector2(0, 200), "SELECT STAGE", 52);

            var stageBtn = MakeButton("PrototypeStageButton", root.transform, new Vector2(0, 0),   "PROTOTYPE STAGE");
            var backBtn  = MakeButton("BackButton",           root.transform, new Vector2(0, -70), "BACK");

            var so = new SerializedObject(view);
            so.FindProperty("prototypeStageButton").objectReferenceValue = stageBtn;
            so.FindProperty("backButton").objectReferenceValue           = backBtn;
            so.ApplyModifiedPropertiesWithoutUndo();

            SavePrefab(root, $"{UiResDir}/UI_StageSelect.prefab");
        }

        private static void AddBackground(Transform parent, Color color)
        {
            var go = new GameObject("Background", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = color;
        }

        private static void MakeTitle(string name, Transform parent, Vector2 anchoredPos, string text, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(800, 100);
            rt.anchoredPosition = anchoredPos;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        // A full-rect panel meant to be instantiated under GameFlowManager.UIRoot
        // (which already has the Canvas). No nested Canvas here.
        private static GameObject NewPanelRoot(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        private static Button MakeButton(string name, Transform parent, Vector2 anchoredPos, string label)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(260, 60);
            rt.anchoredPosition = anchoredPos;

            var img = go.AddComponent<Image>();
            img.color = new Color(0.20f, 0.20f, 0.26f, 0.95f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var labelGo = new GameObject("Text", typeof(RectTransform));
            var lrt = (RectTransform)labelGo.transform;
            lrt.SetParent(go.transform, false);
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 28;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return btn;
        }

        private static void SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int idx = path.LastIndexOf('/');
            string parent = path.Substring(0, idx);
            string leaf = path.Substring(idx + 1);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
