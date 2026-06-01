#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using FrontierBastion.Client.UI;

namespace FrontierBastion.Client.EditorTools
{
    /// <summary>
    /// One-time idempotent editor tool that adds the Support Upgrade buttons (Resource & Pilot)
    /// and the Support Status text label to the UIHUD prefab, then wires them to the serialized fields of UI_BattleHud.
    /// Safe to re-run (idempotent, skips if already present).
    /// </summary>
    public static class AddSupportUpgradeUIToUIHUD
    {
        private const string HudPrefabPath = "Assets/Resources/UI/UIHUD.prefab";

        [MenuItem("FrontierBastion/Add Support Upgrade UI to UIHUD")]
        public static void AddSupportUpgradeUI()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(HudPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[AddSupportUpgradeUIToUIHUD] Could not load prefab at {HudPrefabPath}");
                return;
            }

            try
            {
                var hud = root.GetComponent<UI_BattleHud>();
                if (hud == null)
                {
                    Debug.LogError("[AddSupportUpgradeUIToUIHUD] UIHUD prefab root has no UI_BattleHud component.");
                    return;
                }

                var so = new SerializedObject(hud);
                Transform rootTransform = root.transform;

                // 1. Create Resource Upgrade Button ("RES UP") stacked above SpawnButton
                Button resBtn = MakeButton(rootTransform, "ResourceUpgradeButton", new Vector2(1f, 0f), new Vector2(-20, 245), new Vector2(180, 40), "RES UP");
                
                // 2. Create Pilot Upgrade Button ("PILOT UP") stacked above Resource Upgrade Button
                Button pilBtn = MakeButton(rootTransform, "PilotUpgradeButton", new Vector2(1f, 0f), new Vector2(-20, 290), new Vector2(180, 40), "PILOT UP");
                
                // 3. Create Support Status Text label stacked under Energy/HP bars
                TMP_Text statusText = MakeText(rootTransform, "SupportStatusText", new Vector2(0f, 1f), new Vector2(20, -250), new Vector2(500, 28), "RES Lv1  PILOT Lv1", TextAlignmentOptions.TopLeft);

                // Assign to Serialized fields
                var resProp = so.FindProperty("resourceUpgradeButton");
                var pilProp = so.FindProperty("pilotUpgradeButton");
                var statProp = so.FindProperty("supportStatusText");

                if (resProp != null) resProp.objectReferenceValue = resBtn;
                if (pilProp != null) pilProp.objectReferenceValue = pilBtn;
                if (statProp != null) statProp.objectReferenceValue = statusText;

                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
                Debug.Log("[AddSupportUpgradeUIToUIHUD] Successfully added and wired Support Upgrade UI elements on UIHUD prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Button MakeButton(Transform parent, string name, Vector2 anchor, Vector2 anchoredPos, Vector2 size, string label)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing.GetComponent<Button>();

            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot     = anchor;
            rt.sizeDelta = size;
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
            tmp.fontSize = 16;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return btn;
        }

        private static TMP_Text MakeText(Transform parent, string name, Vector2 anchor, Vector2 anchoredPos, Vector2 size, string text, TextAlignmentOptions align)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing.GetComponent<TMP_Text>();

            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot     = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = 18;
            t.alignment = align;
            t.color = Color.white;
            return t;
        }
    }
}
#endif
