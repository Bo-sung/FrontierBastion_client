#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
using FrontierBastion.Client.Stage;
using BattleSim.Core.State;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;

namespace FrontierBastion.Client.App
{
    /// <summary>
    /// Debug Input Bridge and stats overlay for testing Stage prototype v0.4 battle sessions.
    ///
    /// Automatically attached to the AppRoot GameObject in editor and development builds.
    /// Manages F6 bootstrapping, hotkey inputs for manual player spawning, and displays
    /// a premium status overlay using OnGUI.
    /// </summary>
    public sealed class StageAppDebugController : MonoBehaviour
    {
        private StageBattleManager _battleManager;
        private StageDataManager   _stageData;
        private string             _lastInputError;
        private float              _errorDisplayTimeLeft;

        private void Start()
        {
            _battleManager = AppRoot.Instance != null ? AppRoot.Instance.StageBattle : FindObjectOfType<StageBattleManager>();
            _stageData     = AppRoot.Instance != null ? AppRoot.Instance.StageData : FindObjectOfType<StageDataManager>();
        }

        private void Update()
        {
            if (_battleManager == null || _stageData == null) return;

            // F6: Start Interactive Sandbox Battle
            if (Input.GetKeyDown(KeyCode.F6))
            {
                var stage = StagePrototypeCatalog.CreateInteractiveSandboxStage();
                var deck  = StagePrototypeCatalog.CreateSideAPrototypeDeck();
                _battleManager.StartPrototypeBattle(stage, deck);
                SetError(null);
            }

            // A: Toggle Opponent Auto AI
            if (Input.GetKeyDown(KeyCode.A))
            {
                _battleManager.ToggleOpponentAuto();
            }

            // Space: Toggle Pause/Resume
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _battleManager.IsPaused = !_battleManager.IsPaused;
            }

            // T: Manual Step (if paused)
            if (Input.GetKeyDown(KeyCode.T))
            {
                if (_battleManager.IsPaused)
                {
                    _battleManager.ManualStep();
                }
            }

            // 1..4: Spawn Drone Squad for slot 0..3 in its default lane
            int spawnSlot = -1;
            if (Input.GetKeyDown(KeyCode.Alpha1)) spawnSlot = 0;
            else if (Input.GetKeyDown(KeyCode.Alpha2)) spawnSlot = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha3)) spawnSlot = 2;
            else if (Input.GetKeyDown(KeyCode.Alpha4)) spawnSlot = 3;

            if (spawnSlot != -1)
            {
                string laneId = StagePrototypeCatalog.GetDefaultLaneId(spawnSlot);
                string err    = _battleManager.SubmitSpawnDroneSquad(spawnSlot, laneId);
                if (err != null)
                {
                    SetError($"Spawn Drone Slot {spawnSlot} Error: {err}");
                    Debug.LogWarning($"[DebugBridge] Spawn Drone Slot {spawnSlot} Rejection: {err}");
                }
                else
                {
                    SetError(null);
                }
            }

            // Q..R: Deploy Pilot for slot 0..3 in its default lane
            int deploySlot = -1;
            if (Input.GetKeyDown(KeyCode.Q)) deploySlot = 0;
            else if (Input.GetKeyDown(KeyCode.W)) deploySlot = 1;
            else if (Input.GetKeyDown(KeyCode.E)) deploySlot = 2;
            else if (Input.GetKeyDown(KeyCode.R)) deploySlot = 3;

            if (deploySlot != -1)
            {
                string laneId = StagePrototypeCatalog.GetDefaultLaneId(deploySlot);
                string err    = _battleManager.SubmitDeployPilot(deploySlot, laneId);
                if (err != null)
                {
                    SetError($"Deploy Pilot Slot {deploySlot} Error: {err}");
                    Debug.LogWarning($"[DebugBridge] Deploy Pilot Slot {deploySlot} Rejection: {err}");
                }
                else
                {
                    SetError(null);
                }
            }

            // Z..V: Recall Pilot for slot 0..3
            int recallSlot = -1;
            if (Input.GetKeyDown(KeyCode.Z)) recallSlot = 0;
            else if (Input.GetKeyDown(KeyCode.X)) recallSlot = 1;
            else if (Input.GetKeyDown(KeyCode.C)) recallSlot = 2;
            else if (Input.GetKeyDown(KeyCode.V)) recallSlot = 3;

            if (recallSlot != -1)
            {
                string err = _battleManager.SubmitRecallPilot(recallSlot);
                if (err != null)
                {
                    SetError($"Recall Pilot Slot {recallSlot} Error: {err}");
                    Debug.LogWarning($"[DebugBridge] Recall Pilot Slot {recallSlot} Rejection: {err}");
                }
                else
                {
                    SetError(null);
                }
            }

            // Decay error timer
            if (_errorDisplayTimeLeft > 0f)
            {
                _errorDisplayTimeLeft -= Time.deltaTime;
                if (_errorDisplayTimeLeft <= 0f)
                {
                    _lastInputError = null;
                }
            }
        }

        private void SetError(string msg)
        {
            _lastInputError = msg;
            _errorDisplayTimeLeft = msg != null ? 4f : 0f;
        }

        private void OnGUI()
        {
            if (_battleManager == null) return;

            StageBattleSession session = _battleManager.LastSession;

            // Make the box look neat and tidy.
            GUI.Box(new Rect(10, 10, 310, 480), "FB BATTLE CORE v0.4 DEBUG BRIDGE");

            var style = new GUIStyle(GUI.skin.label);
            style.fontSize = 11;
            style.richText = true;

            float y = 35f;
            float lineOffset = 18f;

            if (session == null)
            {
                GUI.Label(new Rect(20, y, 290, 40), "<color=orange>Press [F6] to start custom\nInteractive Sandbox Stage (v0.4)</color>", style);
                return;
            }

            GUI.Label(new Rect(20, y, 290, 20), $"<b>Stage ID:</b> {session.StageId}", style); y += lineOffset;
            
            string pauseStr = _battleManager.IsPaused ? "<color=yellow>PAUSED (Press [T] to step)</color>" : "<color=green>RUNNING</color>";
            GUI.Label(new Rect(20, y, 290, 20), $"<b>Simulation:</b> {pauseStr}", style); y += lineOffset;

            int tick = session.CurrentTick;
            GUI.Label(new Rect(20, y, 290, 20), $"<b>Current Tick:</b> {tick}", style); y += lineOffset;

            // Side A/B States
            BattleState state = session.LastState;
            if (state != null && state.Sides != null)
            {
                BattleSideState sideA = null;
                BattleSideState sideB = null;
                for (int i = 0; i < state.Sides.Count; i++)
                {
                    if (state.Sides[i].Side == BattleSide.SideA) sideA = state.Sides[i];
                    else if (state.Sides[i].Side == BattleSide.SideB) sideB = state.Sides[i];
                }

                if (sideA != null)
                {
                    GUI.Label(new Rect(20, y, 290, 20), $"<b>[Player - SideA]</b>", style); y += lineOffset;
                    GUI.Label(new Rect(30, y, 280, 20), $"Base HP: {sideA.BaseHp} | Energy: {sideA.Energy:F2}", style); y += lineOffset;
                }

                if (sideB != null)
                {
                    GUI.Label(new Rect(20, y, 290, 20), $"<b>[AI - SideB]</b>", style); y += lineOffset;
                    string autoStr = session.OpponentController.IsEnabled ? "<color=green>Auto ON</color>" : "<color=red>Auto OFF</color>";
                    GUI.Label(new Rect(30, y, 280, 20), $"Base HP: {sideB.BaseHp} | Energy: {sideB.Energy:F2} ({autoStr})", style); y += lineOffset;
                }

                // Lane entity counts
                GUI.Label(new Rect(20, y, 290, 20), $"<b>[Lane Entity Counts]</b>", style); y += lineOffset;
                if (state.Lanes != null)
                {
                    for (int i = 0; i < state.Lanes.Count; i++)
                    {
                        var lane = state.Lanes[i];
                        int aCount = 0;
                        int bCount = 0;
                        for (int j = 0; j < lane.Entities.Count; j++)
                        {
                            if (lane.Entities[j].Side == BattleSide.SideA) aCount++;
                            else bCount++;
                        }
                        GUI.Label(new Rect(30, y, 280, 20), $"{lane.LaneId}: SideA={aCount} | SideB={bCount}", style); y += lineOffset;
                    }
                }
            }

            // Input status / Error
            GUI.Label(new Rect(20, y, 290, 20), $"<b>[Last Input Status]</b>", style); y += lineOffset;
            if (!string.IsNullOrEmpty(_lastInputError))
            {
                GUI.Label(new Rect(30, y, 280, 40), $"<color=red>{_lastInputError}</color>", style);
            }
            else
            {
                GUI.Label(new Rect(30, y, 280, 20), "<color=grey>No errors (Inputs OK)</color>", style);
            }
            y += 30f;

            // Session State (Faulted or Terminated)
            if (session.IsFaulted)
            {
                GUI.Label(new Rect(20, y, 290, 20), "<color=red><b>*** SESSION FAULTED ***</b></color>", style); y += lineOffset;
                GUI.Label(new Rect(20, y, 290, 30), "Check console logs for details.", style); y += lineOffset;
            }
            else if (session.IsTerminated)
            {
                BattleResult res = session.Result;
                string winColor = res.Winner == BattleSide.SideA ? "green" : "red";
                GUI.Label(new Rect(20, y, 290, 20), $"<b>*** BATTLE OVER ***</b>", style); y += lineOffset;
                GUI.Label(new Rect(20, y, 290, 20), $"Winner: <color={winColor}><b>{res.Winner}</b></color> (Reason: {res.EndReason})", style); y += lineOffset;
            }
            else
            {
                GUI.Label(new Rect(20, y, 290, 40), "<color=grey>Controls:\n[1..4]: Spawn Drones | [Q..R]: Pilot | [Z..V]: Recall\n[Space]: Pause/Resume | [A]: Auto Opponent</color>", style);
            }
        }
    }
}
#endif
