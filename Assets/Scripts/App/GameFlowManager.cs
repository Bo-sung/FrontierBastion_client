using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using FrontierBastion.Client.UI;

namespace FrontierBastion.Client.App
{
    /// <summary>Top-level game screens. Each maps to a scene + a UI prefab.</summary>
    public enum GameScreen
    {
        None,
        MainMenu,
        StageSelect,
        Battle,
    }

    /// <summary>
    /// Persistent application root and screen-flow coordinator.  Created by
    /// <see cref="Preload"/> and kept alive for the full runtime via DontDestroyOnLoad.
    ///
    /// Owns the always-on managers (data, battle, presenter), the persistent UIRoot
    /// canvas + EventSystem, and drives screen transitions (MainMenu / StageSelect /
    /// Battle) via <see cref="SceneManager"/>.  The matching UI prefab is spawned
    /// under <see cref="UIRoot"/> after each scene load.
    /// </summary>
    public sealed class GameFlowManager : MonoBehaviour
    {
        public static GameFlowManager Instance { get; private set; }

        public StageDataManager StageData   { get; private set; }
        public BattleManager    StageBattle { get; private set; }
        public BattlePresenter  Presenter   { get; private set; }

        /// <summary>Persistent screen-space UI root that screen prefabs are spawned under.</summary>
        public Canvas UIRoot { get; private set; }

        /// <summary>Persistent main camera shared across all screens.</summary>
        public Camera MainCamera { get; private set; }

        public GameScreen CurrentScreen { get; private set; } = GameScreen.None;

        /// <summary>Stage id chosen on the StageSelect screen, consumed on Battle entry.</summary>
        public string SelectedStageId { get; private set; }

        // ── Scene names (must match the generated .unity files + Build Settings) ──
        public const string SceneMainMenu    = "MainMenu";
        public const string SceneStageSelect = "StageSelect";
        public const string SceneBattle      = "Battle";

        // ── UI prefab Resources paths ──
        private const string UiMainMenuPath    = "UI/UI_MainMenu";
        private const string UiStageSelectPath = "UI/UI_StageSelect";
        private const string UiBattleHudPath   = "UI/UIHUD";

        private GameObject _screenUiInstance;       // current screen's UI prefab instance
        private GameScreen _pendingScreen = GameScreen.None;

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
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            // The Preload scene is already loaded when Awake runs, so sceneLoaded
            // does not fire for it. Kick off the first real screen here.
            GoToMainMenu();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                Instance = null;
            }
        }

        // ── Manager bootstrap ─────────────────────────────────────────────────

        private void CreateManagers()
        {
            StageData   = gameObject.AddComponent<StageDataManager>();
            StageBattle = gameObject.AddComponent<BattleManager>();
            Presenter   = gameObject.AddComponent<BattlePresenter>();

            StageBattle.Bind(StageData);

            MainCamera = CreateMainCamera();
            UIRoot = CreateUIRoot();
            EnsureEventSystem();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            gameObject.AddComponent<DebugManager>();
#endif
        }

        private Camera CreateMainCamera()
        {
            // Reuse an existing main camera if a scene already has one.
            if (Camera.main != null) return Camera.main;

            var camGO = new GameObject("MainCamera");
            camGO.transform.SetParent(transform, false);
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.12f, 1f);
            cam.transform.position = new Vector3(0f, 0f, -10f);
            return cam;
        }

        private Canvas CreateUIRoot()
        {
            var uiGO = new GameObject("UIRoot");
            uiGO.transform.SetParent(transform, false);
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

        // ── Screen transitions ─────────────────────────────────────────────────

        public void GoToMainMenu()
        {
            TransitionTo(GameScreen.MainMenu, SceneMainMenu);
        }

        public void GoToStageSelect()
        {
            TransitionTo(GameScreen.StageSelect, SceneStageSelect);
        }

        /// <summary>Selects a stage and enters the Battle scene.</summary>
        public void GoToBattle(string stageId)
        {
            SelectedStageId = stageId;
            TransitionTo(GameScreen.Battle, SceneBattle);
        }

        /// <summary>Re-enters the Battle scene with the currently selected stage.</summary>
        public void RestartBattle()
        {
            TransitionTo(GameScreen.Battle, SceneBattle);
        }

        private void TransitionTo(GameScreen screen, string sceneName)
        {
            DespawnScreenUi();
            _pendingScreen = screen;
            SceneManager.LoadScene(sceneName);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_pendingScreen == GameScreen.None) return; // not a flow-driven load

            CurrentScreen = _pendingScreen;
            _pendingScreen = GameScreen.None;

            switch (CurrentScreen)
            {
                case GameScreen.MainMenu:    EnterMainMenu();    break;
                case GameScreen.StageSelect: EnterStageSelect(); break;
                case GameScreen.Battle:      EnterBattle();      break;
            }
        }

        // ── Per-screen entry ────────────────────────────────────────────────────

        private void EnterMainMenu()
        {
            SpawnScreenUi(UiMainMenuPath);
        }

        private void EnterStageSelect()
        {
            SpawnScreenUi(UiStageSelectPath);
        }

        private void EnterBattle()
        {
            // World-space battle view lives on this persistent object.
            var worldView = FrontierBastion.Client.Stage.BattleWorldView.GetOrCreate(gameObject);

            SpawnScreenUi(UiBattleHudPath);

            // Start the battle session for the selected stage (or prototype default).
            StageBattle.StartPrototypeBattle();
            StageBattle.IsPaused = false;
        }

        // ── Screen UI prefab spawn/despawn ───────────────────────────────────────

        private void SpawnScreenUi(string resourcePath)
        {
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogError($"[GameFlowManager] Screen UI prefab not found at Resources/{resourcePath}.prefab");
                return;
            }
            _screenUiInstance = Instantiate(prefab, UIRoot.transform, false);
        }

        private void DespawnScreenUi()
        {
            if (_screenUiInstance != null)
            {
                Destroy(_screenUiInstance);
                _screenUiInstance = null;
            }
        }
    }
}
