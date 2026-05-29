using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FrontierBastion.Client.UI
{
    /// <summary>
    /// View in the MVP structure. SerializeField references are assigned in the Unity Editor.
    /// Does not contain game logic, only formats ViewModel values into UI elements
    /// and dispatches user interactions to the Presenter.
    /// </summary>
    public sealed class StageBattleHudView : MonoBehaviour
    {
        [Header("Presenter Reference")]
        [SerializeField] private StageBattlePresenter presenter;

        [Header("Simulation Status")]
        [SerializeField] private Text tickText;
        [SerializeField] private Text statusText;

        [Header("Energy Indicators")]
        [SerializeField] private Slider sideAEnergyBar;
        [SerializeField] private Text sideAEnergyText;

        [Header("Base HP Indicators")]
        [SerializeField] private Slider sideABaseHpBar;
        [SerializeField] private Slider sideBBaseHpBar;

        [Header("AI Indicator")]
        [SerializeField] private Text opponentAutoText;

        [Header("Selection Information")]
        [SerializeField] private Text selectedInfoText;

        [Header("Slots (Deck Cards)")]
        [SerializeField] private Button[] slotSelectButtons;  // 4 elements
        [SerializeField] private Text[] slotCooldownTexts;    // 4 elements

        [Header("Action Buttons")]
        [SerializeField] private Button spawnButton;
        [SerializeField] private Button deployButton;
        [SerializeField] private Button recallButton;

        [Header("Navigation Buttons")]
        [SerializeField] private Button lanePrevButton;
        [SerializeField] private Button laneNextButton;

        [Header("System Buttons")]
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button stepButton;
        [SerializeField] private Button autoButton;
        [SerializeField] private Button startButton;

        [Header("Termination Screen")]
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Text resultText;

        private void Start()
        {
            if (presenter == null)
            {
                presenter = FindFirstObjectByType<StageBattlePresenter>();
            }

            BindButtons();
        }

        private void BindButtons()
        {
            if (presenter == null) return;

            if (startButton != null) startButton.onClick.AddListener(presenter.StartBattle);
            if (spawnButton != null) spawnButton.onClick.AddListener(presenter.CommandSpawnDrone);
            if (deployButton != null) deployButton.onClick.AddListener(presenter.CommandDeployPilot);
            if (recallButton != null) recallButton.onClick.AddListener(presenter.CommandRecallPilot);
            
            if (lanePrevButton != null) lanePrevButton.onClick.AddListener(() => presenter.CycleLane(-1));
            if (laneNextButton != null) laneNextButton.onClick.AddListener(() => presenter.CycleLane(1));

            if (pauseButton != null) pauseButton.onClick.AddListener(presenter.TogglePause);
            if (stepButton != null) stepButton.onClick.AddListener(presenter.ManualStep);
            if (autoButton != null) autoButton.onClick.AddListener(presenter.ToggleOpponentAuto);

            if (slotSelectButtons != null)
            {
                for (int i = 0; i < slotSelectButtons.Length; i++)
                {
                    if (slotSelectButtons[i] == null) continue;
                    int index = i;
                    slotSelectButtons[i].onClick.AddListener(() => presenter.SelectSlot(index));
                }
            }
        }

        /// <summary>
        /// Renders the current viewModel snapshot into the UI elements.
        /// Handles null references gracefully so the HUD can be partially assembled.
        /// </summary>
        public void Render(HudViewModel vm)
        {
            // Tick Text
            if (tickText != null)
            {
                tickText.text = vm.HasSession ? $"Tick: {vm.CurrentTick}" : "No Active Battle";
            }

            // Status message
            if (statusText != null)
            {
                if (string.IsNullOrEmpty(vm.StatusText))
                {
                    statusText.text = string.Empty;
                }
                else
                {
                    statusText.text = vm.StatusText;
                    switch (vm.StatusKind)
                    {
                        case StatusKind.Warning:
                            statusText.color = Color.yellow;
                            break;
                        case StatusKind.Error:
                            statusText.color = Color.red;
                            break;
                        default:
                            statusText.color = Color.white;
                            break;
                    }
                }
            }

            // Energy Bar and Text (assume max energy is 120f)
            if (sideAEnergyBar != null)
            {
                sideAEnergyBar.value = vm.HasSession ? Mathf.Clamp01(vm.SideAEnergy / 120f) : 0f;
            }
            if (sideAEnergyText != null)
            {
                sideAEnergyText.text = vm.HasSession ? $"{vm.SideAEnergy:F1}" : "0.0";
            }

            // Base HP Bars
            if (sideABaseHpBar != null)
            {
                sideABaseHpBar.value = (vm.HasSession && vm.SideABaseHpMax > 0f) 
                    ? Mathf.Clamp01(vm.SideABaseHp / vm.SideABaseHpMax) : 0f;
            }
            if (sideBBaseHpBar != null)
            {
                sideBBaseHpBar.value = (vm.HasSession && vm.SideBBaseHpMax > 0f) 
                    ? Mathf.Clamp01(vm.SideBBaseHp / vm.SideBBaseHpMax) : 0f;
            }

            // Opponent Auto Indicator
            if (opponentAutoText != null)
            {
                opponentAutoText.text = vm.HasSession 
                    ? (vm.OpponentAuto ? "AUTO ON" : "AUTO OFF") : "AUTO OFF";
            }

            // Selected Information Text
            if (selectedInfoText != null)
            {
                selectedInfoText.text = vm.HasSession 
                    ? $"Slot {vm.SelectedSlot + 1} / {vm.SelectedLaneId}" : "No selection";
            }

            // Slot select button highlights and cooldown labels
            if (slotSelectButtons != null)
            {
                for (int i = 0; i < slotSelectButtons.Length; i++)
                {
                    if (slotSelectButtons[i] == null) continue;

                    var cb = slotSelectButtons[i].colors;
                    if (vm.HasSession && vm.SelectedSlot == i)
                    {
                        cb.normalColor = new Color(0.1f, 0.8f, 0.9f, 1f); // Cyan highlight
                        cb.selectedColor = new Color(0.1f, 0.8f, 0.9f, 1f);
                    }
                    else
                    {
                        cb.normalColor = Color.white;
                        cb.selectedColor = Color.white;
                    }
                    slotSelectButtons[i].colors = cb;

                    // Slot state / cooldown text
                    if (slotCooldownTexts != null && i < slotCooldownTexts.Length && slotCooldownTexts[i] != null)
                    {
                        if (!vm.HasSession)
                        {
                            slotCooldownTexts[i].text = string.Empty;
                        }
                        else
                        {
                            var s = vm.Slots[i];
                            if (s.IsPilotKnockedOut)
                            {
                                slotCooldownTexts[i].text = s.PilotCooldown > 0 ? $"KO ({s.PilotCooldown})" : "KO";
                            }
                            else if (s.IsPilotDeployed)
                            {
                                slotCooldownTexts[i].text = "PILOT";
                            }
                            else if (s.DroneCooldown > 0)
                            {
                                slotCooldownTexts[i].text = $"CD {s.DroneCooldown}";
                            }
                            else
                            {
                                slotCooldownTexts[i].text = $"{s.EnergyCost:F0}";
                            }
                        }
                    }
                }
            }

            // Pause label update on button if pauseButton contains Text
            if (pauseButton != null)
            {
                var txt = pauseButton.GetComponentInChildren<Text>();
                if (txt != null)
                {
                    txt.text = vm.IsPaused ? "RESUME" : "PAUSE";
                }
            }

            // Result panel
            if (resultPanel != null)
            {
                resultPanel.SetActive(vm.HasSession && vm.IsTerminated);
            }
            if (resultText != null && vm.HasSession && vm.IsTerminated)
            {
                resultText.text = vm.ResultText;
                resultText.color = vm.ResultText.Contains("VICTORY") ? Color.green : Color.red;
            }
        }
    }
}
