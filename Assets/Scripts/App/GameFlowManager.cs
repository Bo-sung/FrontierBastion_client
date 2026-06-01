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
    /// destroyed so only one GameFlowManager ever exists.
    /// </summary>
    public sealed class GameFlowManager : MonoBehaviour
    {
        public static GameFlowManager Instance { get; private set; }

        public StageDataManager StageData   { get; private set; }
        public BattleManager    StageBattle { get; private set; }
        public BattlePresenter  Presenter   { get; private set; }

        /// <summary>Persistent screen-space UI root that HUD prefabs are spawned under.</summary>
        public Canvas UIRoot { get; private set; }

        // Current stage HUD instance (spawned under UIRoot on EnterStage).
        private GameObject _hudInstance;

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
            StageBattle = gameObject.AddComponent<BattleManager>();
            Presenter   = gameObject.AddComponent<BattlePresenter>();

            StageBattle.Bind(StageData);

            // Persistent UI root + event system for screen-space HUD prefabs.
            UIRoot = CreateUIRoot();
            EnsureEventSystem();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            gameObject.AddComponent<DebugManager>();
#endif
        }

        private Canvas CreateUIRoot()
        {
            var uiGO = new GameObject("UIRoot");
            uiGO.transform.SetParent(transform, false); // child of GameFlowManager → persists via DontDestroyOnLoad
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
        /// Enters a stage: ensures the world-space battle view exists and spawns the
        /// HUD prefab under <see cref="UIRoot"/>.  Idempotent — the HUD is spawned only
        /// once.  Called by <see cref="Preload"/> at boot; later this can be driven by a
        /// stage-select flow.
        /// </summary>
        public void EnterStage()
        {
            // First-class self-driven world-space battle view (reads state via GameFlowManager).
            FrontierBastion.Client.Stage.BattleWorldView.GetOrCreate(gameObject);

            if (_hudInstance != null) return;

            GameObject hudPrefab = Resources.Load<GameObject>(HudResourcePath);
            if (hudPrefab == null)
            {
                Debug.LogError($"[GameFlowManager] HUD prefab not found at Resources/{HudResourcePath}.prefab");
                return;
            }
            _hudInstance = Instantiate(hudPrefab, UIRoot.transform, false);
        }

        /// <summary>Despawns the current stage HUD instance, if any.</summary>
        public void ExitStage()
        {
            if (_hudInstance != null)
            {
                Destroy(_hudInstance);
                _hudInstance = null;
            }
        }
    }
}
