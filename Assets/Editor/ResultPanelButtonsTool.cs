#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using FrontierBastion.Client.UI;

namespace FrontierBastion.Client.EditorTools
{
    /// <summary>
    /// One-time tool: adds Restart / Stage Select / Main Menu buttons to the
    /// UIHUD prefab's ResultPanel and wires them to the UI_BattleHud serialized
    /// fields. Safe to re-run (skips buttons that already exist).
    /// </summary>
    public static class ResultPanelButtonsTool
    {
        private const string HudPrefabPath = "Assets/Resources/UI/UIHUD.prefab";

        [MenuItem("FrontierBastion/Add Result Panel Buttons to UIHUD")]
        public static void AddButtons()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(HudPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[ResultPanelButtonsTool] Could not load prefab at {HudPrefabPath}");
                return;
            }

            try
            {
                var hud = root.GetComponent<UI_BattleHud>();
                if (hud == null)
                {
                    Debug.LogError("[ResultPanelButtonsTool] UIHUD root has no UI_BattleHud component.");
                    return;
                }

                var so = new SerializedObject(hud);
                var panelProp = so.FindProperty("resultPanel");
                GameObject panel = panelProp != null ? panelProp.objectReferenceValue as GameObject : null;
                if (panel == null)
                {
                    Debug.LogError("[ResultPanelButtonsTool] UI_BattleHud.resultPanel is not assigned.");
                    return;
                }

                Button restart = MakeButton(panel.transform, "ResultRestartButton",   new Vector2(0, -60),  "RESTART");
                Button select  = MakeButton(panel.transform, "ResultStageSelectButton", new Vector2(0, -120), "STAGE SELECT");
                Button menu    = MakeButton(panel.transform, "ResultMainMenuButton",  new Vector2(0, -180), "MAIN MENU");

                so.FindProperty("resultRestartButton").objectReferenceValue     = restart;
                so.FindProperty("resultStageSelectButton").objectReferenceValue = select;
                so.FindProperty("resultMainMenuButton").objectReferenceValue    = menu;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
                Debug.Log("[ResultPanelButtonsTool] Added/wired result panel buttons on UIHUD.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Button MakeButton(Transform parent, string name, Vector2 anchoredPos, string label)
        {
            // Reuse if already present (idempotent re-run).
            var existing = parent.Find(name);
            if (existing != null) return existing.GetComponent<Button>();

            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(240, 50);
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
            tmp.fontSize = 22;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return btn;
        }
    }
}
#endif
