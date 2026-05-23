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

        // Called by AppRoot.CreateManagers() after all AddComponents.
        public void Bind(StageBattleManager battleManager)
        {
            _battleManager = battleManager;
        }

        public void StartPrototypeBattle()
        {
            _battleManager?.StartPrototypeBattle();
        }
    }
}
