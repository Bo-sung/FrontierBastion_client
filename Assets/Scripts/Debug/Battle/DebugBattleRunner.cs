using System;
using System.Collections.Generic;
using System.Text;
using BattleSim.Core.Commands;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.Simulation;
using BattleSim.Core.State;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrontierBastion.Client.DebugBattle
{
    public sealed class DebugBattleRunner : MonoBehaviour
    {
        private const float TickSeconds = 0.05f;
        private const int MaxEventLines = 10;

        private readonly Queue<string> _eventLines = new Queue<string>();
        private readonly StringBuilder _guiBuilder = new StringBuilder(2048);

        private DebugBattleScenario _scenario;
        private BattleSimulator _simulator;
        private BattleState _lastState;
        private BattleResult _result;
        private bool _isPaused;
        private float _tickAccumulator;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreateInDebugBuilds()
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
            {
                return;
            }

            if (FindObjectOfType<DebugBattleRunner>() != null)
            {
                return;
            }

            GameObject runnerObject = new GameObject("Debug Battle Runner");
            runnerObject.AddComponent<DebugBattleRunner>();
            DontDestroyOnLoad(runnerObject);
        }

        private void Awake()
        {
            ResetToScenario(DebugBattleScenarioFactory.CreateSmokePlayerVictory(), paused: false);
        }

        private void Update()
        {
            HandleInput();

            if (_simulator == null || _simulator.IsTerminated || _isPaused)
            {
                return;
            }

            _tickAccumulator += Time.deltaTime;
            while (_tickAccumulator >= TickSeconds && !_simulator.IsTerminated)
            {
                RunOneTick();
                _tickAccumulator -= TickSeconds;
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, 560, 640), GUI.skin.box);
            GUILayout.Label(BuildStatusText());
            GUILayout.EndArea();
        }

        private void OnDrawGizmos()
        {
            if (_scenario == null || _lastState == null)
            {
                return;
            }

            for (int i = 0; i < _lastState.Lanes.Count; i++)
            {
                LaneState lane = _lastState.Lanes[i];
                long laneLength = GetLaneLength(lane.LaneId);
                float y = -1.5f * i;
                Vector3 start = new Vector3(-5f, y, 0f);
                Vector3 end = new Vector3(5f, y, 0f);

                Gizmos.color = Color.gray;
                Gizmos.DrawLine(start, end);
                Gizmos.DrawWireCube(start, Vector3.one * 0.2f);
                Gizmos.DrawWireCube(end, Vector3.one * 0.2f);

                foreach (BattleEntity entity in lane.Entities)
                {
                    float t = laneLength > 0 ? Mathf.Clamp01(entity.PositionMilli / (float)laneLength) : 0f;
                    Vector3 position = Vector3.Lerp(start, end, t);
                    Gizmos.color = entity.OwnerSide == OwnerSide.Player ? Color.cyan : Color.red;
                    Gizmos.DrawSphere(position, 0.14f);
                }
            }
        }

        private void HandleInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.f1Key.wasPressedThisFrame)
            {
                ResetToScenario(DebugBattleScenarioFactory.CreateSmokePlayerVictory(), paused: false);
            }

            if (keyboard.f2Key.wasPressedThisFrame)
            {
                ResetToScenario(DebugBattleScenarioFactory.CreateSmokePlayerDefeat(), paused: false);
            }

            if (keyboard.f3Key.wasPressedThisFrame)
            {
                ResetToScenario(DebugBattleScenarioFactory.CreateInteractiveSandbox(), paused: true);
            }

            if (keyboard.backspaceKey.wasPressedThisFrame)
            {
                ResetToScenario(_scenario ?? DebugBattleScenarioFactory.CreateSmokePlayerVictory(), paused: true);
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                _isPaused = !_isPaused;
                AddEvent(_isPaused ? "Paused" : "Resumed");
            }

            if (keyboard.tKey.wasPressedThisFrame)
            {
                RunOneTick();
            }

            if (_simulator == null || _simulator.IsTerminated)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                SlotState slot1 = FindLastSlotState(0);
                if (slot1 != null && slot1.DroneCooldownTick > 0)
                    AddEvent("SpawnDrone rejected: slot 0 cooldown (" + slot1.DroneCooldownTick + " ticks)");
                else if (slot1 != null && slot1.IsPilotDeployed)
                    AddEvent("SpawnDrone rejected: slot 0 pilot deployed");
                else if (_lastState != null && _scenario != null && _lastState.PlayerEnergy < FindSlotEnergyCost(0))
                    AddEvent("SpawnDrone rejected: energy " + _lastState.PlayerEnergy + " < " + FindSlotEnergyCost(0));
                else
                    TrySubmit(BattleCommand.SpawnDroneSquad(
                        _simulator.CurrentTick,
                        slotIndex: 0,
                        laneId: DebugBattleScenarioFactory.LaneGround));
            }

            if (keyboard.digit2Key.wasPressedThisFrame)
            {
                SlotState slot2 = FindLastSlotState(0);
                if (slot2 != null && slot2.IsPilotDeployed)
                    AddEvent("DeployPilot rejected: slot 0 pilot already deployed");
                else if (slot2 != null && slot2.IsPilotKnockedOut)
                    AddEvent("DeployPilot rejected: slot 0 pilot KO");
                else
                    TrySubmit(BattleCommand.DeployPilot(
                        _simulator.CurrentTick,
                        slotIndex: 0,
                        laneId: DebugBattleScenarioFactory.LaneGround));
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                SlotState slotR = FindLastSlotState(0);
                if (slotR != null && !slotR.IsPilotDeployed)
                    AddEvent("RecallPilot rejected: slot 0 pilot not deployed");
                else
                    TrySubmit(BattleCommand.RecallPilot(
                        _simulator.CurrentTick,
                        slotIndex: 0,
                        laneId: string.Empty));
            }
        }

        private void ResetToScenario(DebugBattleScenario scenario, bool paused)
        {
            _scenario = scenario;
            _simulator = new BattleSimulator(scenario.Config, scenario.InitialState);
            _lastState = _simulator.GetState();
            _result = null;
            _isPaused = paused;
            _tickAccumulator = 0f;
            _eventLines.Clear();
            AddEvent("Loaded " + scenario.DisplayName + (paused ? " (paused)" : ""));
        }

        private void RunOneTick()
        {
            if (_simulator == null || _simulator.IsTerminated)
            {
                return;
            }

            SubmitFixtureCommandsForCurrentTick();

            try
            {
                _simulator.AdvanceTick();
                _lastState = _simulator.GetState();
            }
            catch (Exception ex)
            {
                AddEvent("AdvanceTick failed: " + ex.Message);
                _isPaused = true;
                return;
            }

            if (_simulator.IsTerminated)
            {
                _result = _simulator.GetResult();
                bool matchesExpected = _scenario.MatchesExpected(_result);
                AddEvent("Result: " + FormatResult(_result) + " expected=" + matchesExpected);
                Debug.Log("[DebugBattleRunner] " + _scenario.DisplayName + " result: " + FormatResult(_result)
                    + " expected=" + matchesExpected);
            }
        }

        private void SubmitFixtureCommandsForCurrentTick()
        {
            IReadOnlyList<BattleCommand> commands = _scenario.GetFixtureCommandsForTick(_simulator.CurrentTick);
            for (int i = 0; i < commands.Count; i++)
            {
                TrySubmit(commands[i]);
            }
        }

        private void TrySubmit(BattleCommand command)
        {
            try
            {
                _simulator.SubmitCommand(command);
                AddEvent("Command: " + command.CommandType + " slot=" + command.SlotIndex
                    + " lane=" + command.LaneId + " tick=" + command.Tick);
            }
            catch (Exception ex)
            {
                AddEvent("Command rejected: " + command.CommandType + " - " + ex.Message);
            }
        }

        private string BuildStatusText()
        {
            _guiBuilder.Length = 0;
            _guiBuilder.AppendLine("Frontier Bastion - Debug Battle Runner");
            _guiBuilder.AppendLine("Scenario: " + (_scenario != null ? _scenario.DisplayName : "<none>"));
            _guiBuilder.AppendLine("Tick loop: 20 TPS / 1 tick = 0.05s / " + (_isPaused ? "Paused" : "Running"));
            _guiBuilder.AppendLine("Controls: F1 victory fixture, F2 defeat fixture, F3 interactive, Backspace reset");
            _guiBuilder.AppendLine("Controls: Alpha1 spawn drone, Alpha2 deploy pilot, R recall, Space pause, T step");
            _guiBuilder.AppendLine();

            if (_lastState != null)
            {
                _guiBuilder.AppendLine("Tick: " + _lastState.CurrentTick);
                _guiBuilder.AppendLine("Energy: " + _lastState.PlayerEnergy);
                _guiBuilder.AppendLine("Player Base HP: " + _lastState.PlayerBaseHp);
                _guiBuilder.AppendLine("Enemy Base HP: " + _lastState.EnemyBaseHp);
                _guiBuilder.AppendLine("Terminated: " + _lastState.IsTerminated + " / " + _lastState.EndReason);

                for (int i = 0; i < _lastState.Slots.Count; i++)
                {
                    SlotState slot = _lastState.Slots[i];
                    _guiBuilder.AppendLine("Slot " + slot.SlotIndex
                        + " drone_cd=" + slot.DroneCooldownTick
                        + " pilot_cd=" + slot.PilotCooldownTick
                        + " pilot_deployed=" + slot.IsPilotDeployed
                        + " pilot_ko=" + slot.IsPilotKnockedOut);
                }

                for (int i = 0; i < _lastState.Lanes.Count; i++)
                {
                    LaneState lane = _lastState.Lanes[i];
                    _guiBuilder.AppendLine("Lane " + lane.LaneId + " entities=" + lane.Entities.Count);
                    for (int j = 0; j < lane.Entities.Count; j++)
                    {
                        BattleEntity entity = lane.Entities[j];
                        _guiBuilder.AppendLine("  " + entity.EntityId
                            + " " + entity.OwnerSide
                            + " hp=" + entity.Hp
                            + " pos=" + entity.PositionMilli);
                    }
                }
            }

            if (_result != null)
            {
                _guiBuilder.AppendLine();
                _guiBuilder.AppendLine("Result: " + FormatResult(_result));
            }

            _guiBuilder.AppendLine();
            _guiBuilder.AppendLine("Events:");
            foreach (string line in _eventLines)
            {
                _guiBuilder.AppendLine("- " + line);
            }

            return _guiBuilder.ToString();
        }

        private long GetLaneLength(string laneId)
        {
            if (_scenario == null)
            {
                return 1L;
            }

            LaneDefinition[] lanes = _scenario.Config.Lanes;
            for (int i = 0; i < lanes.Length; i++)
            {
                if (lanes[i].LaneId == laneId)
                {
                    return lanes[i].LaneLengthMilli;
                }
            }

            return 1L;
        }

        private void AddEvent(string message)
        {
            if (_eventLines.Count >= MaxEventLines)
            {
                _eventLines.Dequeue();
            }

            _eventLines.Enqueue(message);
        }

        private static string FormatResult(BattleResult result)
        {
            return result.Outcome + " / " + result.EndReason
                + " clearTick=" + result.ClearTimeTick
                + " playerRatio=" + result.PlayerBaseHpRatio
                + " enemyRatio=" + result.EnemyBaseHpRatio;
        }

        /// <summary>
        /// Returns the slot state for the given slotIndex from the last known state snapshot,
        /// or null if not found. Used for pre-validation before SubmitCommand to prevent
        /// invalid commands from entering the pending queue and crashing AdvanceTick.
        /// </summary>
        private SlotState FindLastSlotState(int slotIndex)
        {
            if (_lastState == null)
                return null;
            for (int i = 0; i < _lastState.Slots.Count; i++)
                if (_lastState.Slots[i].SlotIndex == slotIndex)
                    return _lastState.Slots[i];
            return null;
        }

        /// <summary>
        /// Returns the energy cost for the given slotIndex from the scenario's initial slot definition,
        /// or Fp.Zero if not found.
        /// </summary>
        private Fp FindSlotEnergyCost(int slotIndex)
        {
            if (_scenario?.InitialState?.Slots == null)
                return Fp.Zero;
            SlotDefinition[] slots = _scenario.InitialState.Slots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].SlotIndex == slotIndex)
                    return slots[i].EnergyCost;
            return Fp.Zero;
        }
    }
}
