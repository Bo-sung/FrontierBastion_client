using UnityEngine;

namespace FrontierBastion.Client.App
{
    /// <summary>
    /// Minimal Stage prototype flow coordinator.
    /// Does not auto-start a battle on Awake or Start so the existing
    /// DebugBattle F1–F4 behaviour is unaffected.
    /// Trigger <see cref="StartPrototypeBattle"/> explicitly from a UI button,
    /// keyboard handler, or test script.
    /// </summary>
    public sealed class StageFlowManager : MonoBehaviour
    {
        private StageBattleManager _battleManager;
        private GameObject _hudInstance;

        // Called by AppRoot.CreateManagers() after all AddComponents.
        public void Bind(StageBattleManager battleManager)
        {
            _battleManager = battleManager;
        }

        public void StartPrototypeBattle()
        {
            _battleManager?.StartPrototypeBattle();
        }

        /// <summary>
        /// Enters a stage: spawns the HUD prefab under the given UI root.
        /// Idempotent — does nothing if a HUD instance already exists.
        /// </summary>
        public void EnterStage(Canvas uiRoot, GameObject hudPrefab)
        {
            if (uiRoot == null || hudPrefab == null) return;
            if (_hudInstance != null) return;
            _hudInstance = Object.Instantiate(hudPrefab, uiRoot.transform, false);
        }

        /// <summary>Despawns the current HUD instance, if any.</summary>
        public void ExitStage()
        {
            if (_hudInstance != null)
            {
                Object.Destroy(_hudInstance);
                _hudInstance = null;
            }
        }
    }
}
