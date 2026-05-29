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

        private int                  _selectedSlot = 0;
        private int                  _selectedLaneIndex = 0;
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

            // Space: Toggle Pause/Resume
            if (KeyPressed(Key.Space))
            {
                _battleManager.IsPaused = !_battleManager.IsPaused;
            }

            // F8: Manual Step (if paused)
            if (KeyPressed(Key.F8))
            {
                if (_battleManager.IsPaused)
                {
                    _battleManager.ManualStep();
                }
            }

            // 1..4: Select slot 0..3 (selection only, no action)
            if (KeyPressed(Key.Digit1)) _selectedSlot = 0;
            else if (KeyPressed(Key.Digit2)) _selectedSlot = 1;
            else if (KeyPressed(Key.Digit3)) _selectedSlot = 2;
            else if (KeyPressed(Key.Digit4)) _selectedSlot = 3;

            // Q / E: Cycle selected lane (previous / next)
            int laneCount = LaneCount();
            if (laneCount > 0)
            {
                if (KeyPressed(Key.Q)) _selectedLaneIndex = (_selectedLaneIndex - 1 + laneCount) % laneCount;
                else if (KeyPressed(Key.E)) _selectedLaneIndex = (_selectedLaneIndex + 1) % laneCount;
            }

            // W: Spawn drone squad (selected slot -> selected lane)
            if (KeyPressed(Key.W))
            {
                string laneId = CurrentLaneId();
                string err    = _battleManager.SubmitSpawnDroneSquad(_selectedSlot, laneId);
                ReportInput($"Spawn Drone Slot {_selectedSlot} lane {laneId}", err);
            }

            // R: Deploy pilot (selected slot -> selected lane)
            if (KeyPressed(Key.R))
            {
                string laneId = CurrentLaneId();
                string err    = _battleManager.SubmitDeployPilot(_selectedSlot, laneId);
                ReportInput($"Deploy Pilot Slot {_selectedSlot} lane {laneId}", err);
            }

            // T: Recall pilot (selected slot)
            if (KeyPressed(Key.T))
            {
                string err = _battleManager.SubmitRecallPilot(_selectedSlot);
                ReportInput($"Recall Pilot Slot {_selectedSlot}", err);
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

        private void ReportInput(string action, string err)
        {
            if (err != null)
            {
                SetError($"{action}: {err}");
                Debug.LogWarning($"[DebugBridge] {action} rejected: {err}");
            }
            else
            {
                SetError(null);
            }
        }

        private int LaneCount()
        {
            var cfg = _battleManager != null ? _battleManager.CurrentConfig : null;
            return (cfg != null && cfg.Lanes != null) ? cfg.Lanes.Length : 0;
        }

        private string CurrentLaneId()
        {
            var cfg = _battleManager != null ? _battleManager.CurrentConfig : null;
            if (cfg == null || cfg.Lanes == null || cfg.Lanes.Length == 0)
                return "lane_ground_1";
            int n = cfg.Lanes.Length;
            int idx = ((_selectedLaneIndex % n) + n) % n;
            return cfg.Lanes[idx].LaneId;
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
            
            string pauseStr = _battleManager.IsPaused ? "<color=yellow>PAUSED (Press [F8] to step)</color>" : "<color=green>RUNNING</color>";
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
                GUI.Label(new Rect(20, y, 290, 70),
                    $"<color=white>Selected Slot: <b>{_selectedSlot + 1}</b> | Lane: <b>{CurrentLaneId()}</b></color>\n" +
                    "<color=grey>[1..4]: Select Slot | [Q/E]: Prev/Next Lane\n" +
                    "[W]: Spawn Drone | [R]: Deploy Pilot | [T]: Recall Pilot\n" +
                    "[Space]: Pause/Resume | [F8]: Step | [A]: Auto Opponent</color>", style);
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
