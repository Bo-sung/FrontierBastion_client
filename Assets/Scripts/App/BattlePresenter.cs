using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FrontierBastion.Client.App;
using FrontierBastion.Client.Stage;
using BattleSim.Core.Config;
using BattleSim.Core.Results;
using BattleSim.Core.State;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Commands;

namespace FrontierBastion.Client.UI
{
    public enum StatusKind
    {
        None,
        Info,
        Warning,
        Error
    }

    public struct SlotVm
    {
        public int SlotIndex;
        public float EnergyCost;
        public int DroneCooldown;
        public int PilotCooldown;
        public bool IsPilotDeployed;
        public bool IsPilotKnockedOut;
    }

    public struct HudViewModel
    {
        public bool HasSession;
        public bool IsPaused;
        public bool IsTerminated;
        public bool IsFaulted;
        public int CurrentTick;
        
        public float SideAEnergy;
        public float SideAEnergyMax;
        public float SideABaseHp;
        public float SideABaseHpMax;
        
        public float SideBEnergy;
        public float SideBBaseHp;
        public float SideBBaseHpMax;
        
        public bool OpponentAuto;
        public int SelectedSlot;
        public string SelectedLaneId;
        public SlotVm[] Slots;
        public string StatusText;
        public StatusKind StatusKind;
        public string ResultText;

        // Support Upgrade Fields (SideA)
        public int ResourceLevel;
        public int PilotLevel;
        public BattleSupportTrack ActiveTrack;
        public int ActiveTargetLevel;
        public float SupportRemainingSeconds;
        public bool SupportActive;
        public bool EnergyRegenPaused;
        public bool PilotDeployBlocked;
    }

    /// <summary>
    /// Presenter in the MVP structure. Owns input state, keyboard shortcuts,
    /// intent API routing to BattleManager, and binds the View to the Model.
    /// </summary>
    public sealed class BattlePresenter : MonoBehaviour
    {
        [SerializeField] private BattleManager _battleManager;
        [SerializeField] private UI_BattleHud _view;

        // Input States (single source of truth)
        private int _selectedSlot = 0;
        private int _selectedLaneIndex = 0;

        public int SelectedSlot => _selectedSlot;
        public string SelectedLaneId => CurrentLaneId();

        // Status messages and limit/error tracking
        private string _statusText = null;
        private float _statusTimer = 0f;
        private StatusKind _statusKind = StatusKind.None;

        private void Start()
        {
            if (_battleManager == null)
            {
                _battleManager = GameFlowManager.Instance != null ? GameFlowManager.Instance.StageBattle : FindAnyObjectByType<BattleManager>();
            }
            if (_view == null)
            {
                _view = FindAnyObjectByType<UI_BattleHud>();
            }
        }

        private static bool KeyPressed(Key key)
        {
            Keyboard kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }

        private void Update()
        {
            // Decay status display timer
            if (_statusTimer > 0f)
            {
                _statusTimer -= Time.deltaTime;
                if (_statusTimer <= 0f)
                {
                    _statusText = null;
                    _statusKind = StatusKind.None;
                }
            }

            if (_battleManager == null) return;

            // Key inputs
            if (KeyPressed(Key.F6)) StartBattle();
            if (KeyPressed(Key.Space)) TogglePause();
            if (KeyPressed(Key.F8)) ManualStep();
            if (KeyPressed(Key.A)) ToggleOpponentAuto();

            if (KeyPressed(Key.F1)) CommandStartResourceUpgrade();
            if (KeyPressed(Key.F2)) CommandStartPilotUpgrade();

            if (KeyPressed(Key.Digit1)) SelectSlot(0);
            if (KeyPressed(Key.Digit2)) SelectSlot(1);
            if (KeyPressed(Key.Digit3)) SelectSlot(2);
            if (KeyPressed(Key.Digit4)) SelectSlot(3);

            if (KeyPressed(Key.Q)) CycleLane(-1);
            if (KeyPressed(Key.E)) CycleLane(+1);

            if (KeyPressed(Key.W)) CommandSpawnDrone();
            if (KeyPressed(Key.R)) CommandDeployPilot();
            if (KeyPressed(Key.T)) CommandRecallPilot();
        }

        private void LateUpdate()
        {
            // The HUD is spawned only after entering the Battle scene, which can be
            // long after this persistent presenter's Start(). Re-resolve until found.
            if (_view == null)
            {
                _view = FindAnyObjectByType<UI_BattleHud>();
                if (_view != null) _view.BindPresenter(this);
            }

            if (_view != null)
            {
                _view.Render(BuildViewModel());
            }
        }

        // ── Intent API ────────────────────────────────────────────────────────

        public void StartBattle()
        {
            var stage = StagePrototypeCatalog.CreateInteractiveSandboxStage();
            var deck  = StagePrototypeCatalog.CreateSideAPrototypeDeck();
            _battleManager.StartPrototypeBattle(stage, deck);
            SetStatus(null);
        }

        public void SelectSlot(int slotIndex)
        {
            _selectedSlot = Mathf.Clamp(slotIndex, 0, 3);
        }

        public void CycleLane(int direction)
        {
            int count = LaneCount();
            if (count > 0)
            {
                _selectedLaneIndex = (_selectedLaneIndex + direction + count) % count;
            }
        }

        public void CommandSpawnDrone()
        {
            string err = _battleManager.SubmitSpawnDroneSquad(_selectedSlot, CurrentLaneId());
            SetStatus(err);
        }

        public void CommandDeployPilot()
        {
            string err = _battleManager.SubmitDeployPilot(_selectedSlot, CurrentLaneId());
            SetStatus(err);
        }

        public void CommandRecallPilot()
        {
            string err = _battleManager.SubmitRecallPilot(_selectedSlot);
            SetStatus(err);
        }

        public void CommandStartResourceUpgrade()
        {
            string err = _battleManager.SubmitStartSupportUpgrade(BattleSupportTrack.Resource);
            SetStatus(err);
        }

        public void CommandStartPilotUpgrade()
        {
            string err = _battleManager.SubmitStartSupportUpgrade(BattleSupportTrack.Pilot);
            SetStatus(err);
        }

        public void TogglePause()
        {
            _battleManager.IsPaused = !_battleManager.IsPaused;
        }

        public void ManualStep()
        {
            if (_battleManager.IsPaused)
            {
                _battleManager.ManualStep();
            }
        }

        public void ToggleOpponentAuto()
        {
            _battleManager.ToggleOpponentAuto();
        }

        // ── Lane Helpers ──────────────────────────────────────────────────────

        private BattleConfigSnapshot CurrentConfig => _battleManager?.CurrentConfig;

        private int LaneCount() => CurrentConfig?.Lanes?.Length ?? 0;

        private string CurrentLaneId()
        {
            int count = LaneCount();
            if (count <= 0) return "lane_ground_1";
            int clamped = Mathf.Clamp(_selectedLaneIndex, 0, count - 1);
            return CurrentConfig.Lanes[clamped].LaneId;
        }

        private void SetStatus(string errorMsg)
        {
            if (string.IsNullOrEmpty(errorMsg))
            {
                _statusText = null;
                _statusKind = StatusKind.None;
                _statusTimer = 0f;
            }
            else
            {
                _statusText = errorMsg;
                _statusTimer = 4f; // 4 seconds decay
                
                string lower = errorMsg.ToLowerInvariant();
                if (lower.StartsWith("same-tick") || lower.Contains("cooldown") || lower.Contains("already"))
                {
                    _statusKind = StatusKind.Warning;
                }
                else
                {
                    _statusKind = StatusKind.Error;
                }
            }
        }

        // ── ViewModel Building ────────────────────────────────────────────────

        public HudViewModel BuildViewModel()
        {
            HudViewModel vm = new HudViewModel();
            vm.Slots = new SlotVm[4];
            for (int i = 0; i < 4; i++)
            {
                vm.Slots[i] = new SlotVm { SlotIndex = i };
            }

            var session = _battleManager?.LastSession;
            if (session == null)
            {
                vm.HasSession = false;
                vm.StatusText = _statusText;
                vm.StatusKind = _statusKind;
                return vm;
            }

            vm.HasSession = true;
            vm.IsPaused = _battleManager.IsPaused;
            vm.IsTerminated = session.IsTerminated;
            vm.IsFaulted = session.IsFaulted;
            vm.CurrentTick = session.CurrentTick;
            vm.OpponentAuto = session.OpponentController?.IsEnabled ?? false;
            vm.SelectedSlot = _selectedSlot;
            vm.SelectedLaneId = CurrentLaneId();
            vm.StatusText = _statusText;
            vm.StatusKind = _statusKind;

            var config = session.Config;
            var state = session.LastState;
            
            if (config != null)
            {
                vm.SideABaseHpMax = (float)config.SideA.BaseInitialHp.Raw / 10000f;
                vm.SideBBaseHpMax = (float)config.SideB.BaseInitialHp.Raw / 10000f;
                vm.SideAEnergyMax = (float)config.SideA.MaxEnergy.Raw / 10000f;
            }

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
                    vm.SideAEnergy = (float)sideA.Energy.Raw / 10000f;
                    vm.SideABaseHp = (float)sideA.BaseHp.Raw / 10000f;

                    if (sideA.SupportState != null)
                    {
                        vm.ResourceLevel = sideA.SupportState.ResourceLevel;
                        vm.PilotLevel = sideA.SupportState.PilotLevel;
                        vm.ActiveTrack = sideA.SupportState.ActiveTrack;
                        vm.ActiveTargetLevel = sideA.SupportState.ActiveTargetLevel;
                        vm.SupportRemainingSeconds = (float)sideA.SupportState.RemainingTick / 20f;
                        vm.SupportActive = sideA.SupportState.IsActive;
                        vm.EnergyRegenPaused = sideA.SupportState.IsEnergyRegenPaused;
                        vm.PilotDeployBlocked = sideA.SupportState.IsPilotDeployBlocked;
                    }

                    var sideASlotsDef = session.SideASlots;
                    for (int i = 0; i < 4; i++)
                    {
                        SlotState ss = null;
                        for (int j = 0; j < sideA.Slots.Count; j++)
                        {
                            if (sideA.Slots[j].SlotIndex == i)
                            {
                                ss = sideA.Slots[j];
                                break;
                            }
                        }

                        SlotDefinition sd = null;
                        if (sideASlotsDef != null)
                        {
                            for (int j = 0; j < sideASlotsDef.Count; j++)
                            {
                                if (sideASlotsDef[j].SlotIndex == i)
                                {
                                    sd = sideASlotsDef[j];
                                    break;
                                }
                            }
                        }

                        if (sd != null)
                        {
                            vm.Slots[i].EnergyCost = (float)sd.EnergyCost.Raw / 10000f;
                        }

                        if (ss != null)
                        {
                            vm.Slots[i].DroneCooldown = ss.DroneCooldownTick;
                            vm.Slots[i].PilotCooldown = ss.PilotCooldownTick;
                            vm.Slots[i].IsPilotDeployed = ss.IsPilotDeployed;
                            vm.Slots[i].IsPilotKnockedOut = ss.IsPilotKnockedOut;
                        }
                    }
                }

                if (sideB != null)
                {
                    vm.SideBEnergy = (float)sideB.Energy.Raw / 10000f;
                    vm.SideBBaseHp = (float)sideB.BaseHp.Raw / 10000f;
                }
            }

            if (session.IsTerminated && session.Result != null)
            {
                BattleResult res = session.Result;
                bool isPlayerWinner = res.WinnerSide == BattleSide.SideA;
                string prefix = res.EndReason == BattleEndReason.TimeOut ? "TIMEOUT " : "";
                vm.ResultText = isPlayerWinner ? prefix + "VICTORY" : prefix + "DEFEAT";
            }

            return vm;
        }
    }
}
