#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
        private StageBattleManager   _battleManager;
        private StageDataManager     _stageData;
        private string               _lastInputError;
        private float                _errorDisplayTimeLeft;
        private StageBattleWorldView _worldView;
        private bool                 _lastFaultedReported;

        private string               _selectedGroundLane = "lane_ground_1";
        private readonly Queue<string> _recentEventLines = new Queue<string>();
        private int                  _lastProcessedEventTick = -1;

        private void Start()
        {
            _battleManager = AppRoot.Instance != null ? AppRoot.Instance.StageBattle : FindFirstObjectByType<StageBattleManager>();
            _stageData     = AppRoot.Instance != null ? AppRoot.Instance.StageData : FindFirstObjectByType<StageDataManager>();
            _worldView     = StageBattleWorldView.GetOrCreate(gameObject);
        }

        private static bool KeyPressed(Key key)
        {
            Keyboard kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }

        private void Update()
        {
            if (_battleManager == null || _stageData == null) return;

            // F6: Start Interactive Sandbox Battle
            if (KeyPressed(Key.F6))
            {
                var stage = StagePrototypeCatalog.CreateInteractiveSandboxStage();
                var deck  = StagePrototypeCatalog.CreateSideAPrototypeDeck();
                _battleManager.StartPrototypeBattle(stage, deck);
                SetError(null);
            }

            // A: Toggle Opponent Auto AI
            if (KeyPressed(Key.A))
            {
                _battleManager.ToggleOpponentAuto();
            }

            // L: Toggle Selected Ground Lane
            if (KeyPressed(Key.L))
            {
                _selectedGroundLane = StagePrototypeCatalog.GetGroundLaneToggle(_selectedGroundLane);
            }

            // Space: Toggle Pause/Resume
            if (KeyPressed(Key.Space))
            {
                _battleManager.IsPaused = !_battleManager.IsPaused;
            }

            // T: Manual Step (if paused)
            if (KeyPressed(Key.T))
            {
                if (_battleManager.IsPaused)
                {
                    _battleManager.ManualStep();
                }
            }

            // 1..4: Spawn Drone Squad for slot 0..3
            int spawnSlot = -1;
            if (KeyPressed(Key.Digit1)) spawnSlot = 0;
            else if (KeyPressed(Key.Digit2)) spawnSlot = 1;
            else if (KeyPressed(Key.Digit3)) spawnSlot = 2;
            else if (KeyPressed(Key.Digit4)) spawnSlot = 3;

            if (spawnSlot != -1)
            {
                string laneId = (spawnSlot == 3) ? StagePrototypeCatalog.GetDefaultLaneId(spawnSlot) : _selectedGroundLane;
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

            // Q..R: Deploy Pilot for slot 0..3
            int deploySlot = -1;
            if (KeyPressed(Key.Q)) deploySlot = 0;
            else if (KeyPressed(Key.W)) deploySlot = 1;
            else if (KeyPressed(Key.E)) deploySlot = 2;
            else if (KeyPressed(Key.R)) deploySlot = 3;

            if (deploySlot != -1)
            {
                string laneId = (deploySlot == 3) ? StagePrototypeCatalog.GetDefaultLaneId(deploySlot) : _selectedGroundLane;
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
            if (KeyPressed(Key.Z)) recallSlot = 0;
            else if (KeyPressed(Key.X)) recallSlot = 1;
            else if (KeyPressed(Key.C)) recallSlot = 2;
            else if (KeyPressed(Key.V)) recallSlot = 3;

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

        private void LateUpdate()
        {
            // Fault-once dump: first frame the session enters faulted state,
            // dump LastFailureDump to the console for diagnosis.
            StageBattleSession s = _battleManager != null ? _battleManager.LastSession : null;
            if (s != null && s.IsFaulted && !_lastFaultedReported)
            {
                _lastFaultedReported = true;
                Debug.LogError("[DebugBridge] StageBattleSession FAULTED. Dump follows:\n" + s.LastFailureDump);
            }
            else if (s != null && !s.IsFaulted && _lastFaultedReported)
            {
                _lastFaultedReported = false; // session restarted
            }

            if (s != null && s.LastState != null)
            {
                if (s.LastState.CurrentTick < _lastProcessedEventTick || s.LastState.CurrentTick <= 0)
                {
                    _recentEventLines.Clear();
                }

                if (s.LastState.CurrentTick != _lastProcessedEventTick)
                {
                    _lastProcessedEventTick = s.LastState.CurrentTick;
                    if (s.LastState.RecentEvents != null)
                    {
                        foreach (var evt in s.LastState.RecentEvents)
                        {
                            string line = $"T{evt.Tick} #{evt.Sequence} {evt.EventType} src={evt.SourceEntityId} tgt={evt.TargetEntityId} lane={evt.LaneId} dmg={evt.DamageAmount}";
                            _recentEventLines.Enqueue(line);
                            while (_recentEventLines.Count > 12)
                            {
                                _recentEventLines.Dequeue();
                            }
                        }
                    }
                }
            }

            if (_worldView == null) return;

            if (_battleManager?.CurrentConfig != null && _battleManager.LastSession != null)
            {
                _worldView.Render(
                    _battleManager.CurrentConfig,
                    _battleManager.LastSession.LastState,
                    _battleManager.CurrentConfig.SideA.BaseInitialHp,
                    _battleManager.CurrentConfig.SideB.BaseInitialHp,
                    _battleManager.CurrentConfig.TimeOutTieWinnerSide);
            }
            else
            {
                _worldView.Render(null, null, Fp.Zero, Fp.Zero, BattleSide.SideB);
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
            GUI.Box(new Rect(10, 10, 310, 480), "FB BATTLE CORE v0.5+v0.7 DEBUG BRIDGE");

            var style = new GUIStyle(GUI.skin.label);
            style.fontSize = 11;
            style.richText = true;

            float y = 35f;
            float lineOffset = 18f;

            if (session == null)
            {
                GUI.Label(new Rect(20, y, 290, 40), "<color=orange>Press [F6] to start custom\nInteractive Sandbox Stage (v0.5+v0.7)</color>", style);
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
                string winColor = res.WinnerSide == BattleSide.SideA ? "green" : "red";
                GUI.Label(new Rect(20, y, 290, 20), $"<b>*** BATTLE OVER ***</b>", style); y += lineOffset;
                GUI.Label(new Rect(20, y, 290, 20), $"Winner: <color={winColor}><b>{res.WinnerSide}</b></color> (Reason: {res.EndReason})", style); y += lineOffset;
            }
            else
            {
                GUI.Label(new Rect(20, y, 290, 50), $"<color=grey>Controls:\n[1..4]: Spawn Drones | [Q..R]: Pilot | [Z..V]: Recall\n[Space]: Pause/Resume | [A]: Auto Opponent\n[L]: Ground Lane Toggle (current: {_selectedGroundLane})</color>", style);
            }

            // Recent Events overlay box
            GUI.Box(new Rect(10, 500, 310, 200), "Recent Events");
            float eventY = 525f;
            foreach (var evtLine in _recentEventLines)
            {
                GUI.Label(new Rect(20, eventY, 290, 20), evtLine, style);
                eventY += 14f;
            }
        }
    }
}
#endif
