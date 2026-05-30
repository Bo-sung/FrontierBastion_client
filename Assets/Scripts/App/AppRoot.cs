using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using FrontierBastion.Client.UI;

namespace FrontierBastion.Client.App
{
    /// <summary>
    /// Persistent application root.  Created by <see cref="Preload"/> and kept alive
    /// for the full runtime via DontDestroyOnLoad.
    ///
    /// Owns all manager component references.  Duplicate instances are immediately
    /// destroyed so only one AppRoot ever exists.
    /// </summary>
    public sealed class AppRoot : MonoBehaviour
    {
        public static AppRoot Instance { get; private set; }

        public StageDataManager   StageData   { get; private set; }
        public StageBattleManager StageBattle { get; private set; }
        public StageFlowManager   StageFlow   { get; private set; }
        public StageBattlePresenter Presenter { get; private set; }

        /// <summary>Persistent screen-space UI root that HUD prefabs are spawned under.</summary>
        public Canvas UIRoot { get; private set; }

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            CreateManagers();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ── Manager bootstrap ─────────────────────────────────────────────────

        private void CreateManagers()
        {
            // AddComponent triggers each manager's Awake immediately.
            // Bind() runs after all Awakes to wire cross-references.
            StageData   = gameObject.AddComponent<StageDataManager>();
            StageBattle = gameObject.AddComponent<StageBattleManager>();
            StageFlow   = gameObject.AddComponent<StageFlowManager>();
            Presenter   = gameObject.AddComponent<StageBattlePresenter>();

            StageBattle.Bind(StageData);
            StageFlow.Bind(StageBattle);

            // Persistent UI root + event system for screen-space HUD prefabs.
            UIRoot = CreateUIRoot();
            EnsureEventSystem();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            gameObject.AddComponent<StageAppDebugController>();
#endif
        }

        private Canvas CreateUIRoot()
        {
            var uiGO = new GameObject("UIRoot");
            uiGO.transform.SetParent(transform, false); // child of AppRoot → persists via DontDestroyOnLoad
            var canvas = uiGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiGO.AddComponent<CanvasScaler>();
            uiGO.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            DontDestroyOnLoad(es);
        }

        // ── Stage entry ───────────────────────────────────────────────────────

        /// <summary>Resources path (no extension) of the HUD prefab.</summary>
        private const string HudResourcePath = "UI/UIHUD";

        /// <summary>
        /// Enters a stage: loads the HUD prefab from Resources and spawns it under
        /// <see cref="UIRoot"/> via <see cref="StageFlowManager"/>.  Called by
        /// <see cref="Preload"/> at boot; later this can be driven by a stage-select flow.
        /// </summary>
        public void EnterStage()
        {
            // First-class self-driven world-space battle view (reads state via AppRoot).
            FrontierBastion.Client.Stage.StageBattleWorldView.GetOrCreate(gameObject);

            GameObject hudPrefab = Resources.Load<GameObject>(HudResourcePath);
            if (hudPrefab == null)
            {
                Debug.LogError($"[AppRoot] HUD prefab not found at Resources/{HudResourcePath}.prefab");
                return;
            }
            StageFlow.EnterStage(UIRoot, hudPrefab);
        }
    }
}
