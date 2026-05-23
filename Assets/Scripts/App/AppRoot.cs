using UnityEngine;

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

            StageBattle.Bind(StageData);
            StageFlow.Bind(StageBattle);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            gameObject.AddComponent<StageAppDebugController>();
#endif
        }
    }
}
