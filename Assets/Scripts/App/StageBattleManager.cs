using FrontierBastion.Client.Stage;
using UnityEngine;

namespace FrontierBastion.Client.App
{
    /// <summary>
    /// Owns one <see cref="StageBattleSession"/> at a time.
    /// Drives the active session via <c>Time.deltaTime</c> in Update at the manager
    /// layer — <see cref="StageBattleSession"/> itself never calls <c>Time.deltaTime</c>.
    /// Exposes player command and flow APIs for UI or input layers above.
    /// </summary>
    public sealed class StageBattleManager : MonoBehaviour
    {
        private StageDataManager _stageData;

        /// <summary>The most recently started session, or null if none.</summary>
        public StageBattleSession LastSession { get; private set; }

        /// <summary>Set true to suspend automatic ticking without destroying the session.</summary>
        public bool IsPaused { get; set; }

        // Called by AppRoot.CreateManagers() after AddComponent so Awake ordering
        // does not matter.
        public void Bind(StageDataManager stageData)
        {
            _stageData = stageData;
        }

        // ── Session lifecycle ─────────────────────────────────────────────────

        public void StartPrototypeBattle()
        {
            StageDefinition stage     = _stageData.GetPrototypeStage();
            TroopCardData[] sideADeck = _stageData.GetPrototypeSideADeck();
            LastSession = new StageBattleSession(stage, sideADeck);
        }

        public void StartPrototypeBattle(StageDefinition stage, TroopCardData[] sideADeck)
        {
            LastSession = new StageBattleSession(stage, sideADeck);
        }

        // ── Tick driving ──────────────────────────────────────────────────────

        private void Update()
        {
            if (LastSession == null) return;
            if (IsPaused || LastSession.IsFaulted || LastSession.IsTerminated) return;
            LastSession.Tick(Time.deltaTime);
        }

        // ── Player command wrappers ───────────────────────────────────────────

        public void ManualStep() => LastSession?.ManualStep();

        /// <summary>Returns null on success, or a rejection reason string.</summary>
        public string SubmitSpawnDroneSquad(int slotIndex, string laneId) =>
            LastSession != null
                ? LastSession.SubmitSpawnDroneSquad(slotIndex, laneId)
                : "No active session";

        /// <summary>Returns null on success, or a rejection reason string.</summary>
        public string SubmitDeployPilot(int slotIndex, string laneId) =>
            LastSession != null
                ? LastSession.SubmitDeployPilot(slotIndex, laneId)
                : "No active session";

        /// <summary>Returns null on success, or a rejection reason string.</summary>
        public string SubmitRecallPilot(int slotIndex) =>
            LastSession != null
                ? LastSession.SubmitRecallPilot(slotIndex)
                : "No active session";

        public void ToggleOpponentAuto() => LastSession?.OpponentController.Toggle();
    }
}
