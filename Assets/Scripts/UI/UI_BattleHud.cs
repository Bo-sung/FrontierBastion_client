using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using FrontierBastion.Client.App;

namespace FrontierBastion.Client.UI
{
    /// <summary>
    /// View in the MVP structure. SerializeField references are assigned in the Unity Editor.
    /// Does not contain game logic, only formats ViewModel values into UI elements
    /// and dispatches user interactions to the Presenter.
    /// </summary>
    public sealed class UI_BattleHud : MonoBehaviour
    {
        [Header("Presenter Reference")]
        [SerializeField] private BattlePresenter presenter;

        [Header("Simulation Status")]
        [SerializeField] private TMP_Text tickText;
        [SerializeField] private TMP_Text statusText;

        [Header("Energy Indicators")]
        [SerializeField] private Slider sideAEnergyBar;
        [SerializeField] private TMP_Text sideAEnergyText;

        [Header("Base HP Indicators")]
        [SerializeField] private Slider sideABaseHpBar;
        [SerializeField] private Slider sideBBaseHpBar;

        [Header("AI Indicator")]
        [SerializeField] private TMP_Text opponentAutoText;

        [Header("Selection Information")]
        [SerializeField] private TMP_Text selectedInfoText;

        [Header("Slots (Deck Cards)")]
        [SerializeField] private Button[] slotSelectButtons;  // 4 elements
        [SerializeField] private TMP_Text[] slotCooldownTexts;    // 4 elements

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
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private Button resultRestartButton;
        [SerializeField] private Button resultStageSelectButton;
        [SerializeField] private Button resultMainMenuButton;

        private bool _buttonsBound;

        private void Start()
        {
            if (presenter == null)
            {
                presenter = FindAnyObjectByType<BattlePresenter>();
            }

            BindButtons();
        }

        /// <summary>
        /// Called by the presenter when it resolves this view after the HUD spawns.
        /// Ensures button listeners are wired even if the presenter wasn't found in Start().
        /// </summary>
        public void BindPresenter(BattlePresenter p)
        {
            presenter = p;
            BindButtons();
        }

        private void BindButtons()
        {
            if (presenter == null || _buttonsBound) return;
            _buttonsBound = true;

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

            // Result panel flow buttons → GameFlowManager (not the presenter).
            if (resultRestartButton != null)
                resultRestartButton.onClick.AddListener(OnResultRestart);
            if (resultStageSelectButton != null)
                resultStageSelectButton.onClick.AddListener(OnResultStageSelect);
            if (resultMainMenuButton != null)
                resultMainMenuButton.onClick.AddListener(OnResultMainMenu);
        }

        private static void OnResultRestart()
        {
            if (GameFlowManager.Instance != null) GameFlowManager.Instance.RestartBattle();
        }

        private static void OnResultStageSelect()
        {
            if (GameFlowManager.Instance != null) GameFlowManager.Instance.GoToStageSelect();
        }

        private static void OnResultMainMenu()
        {
            if (GameFlowManager.Instance != null) GameFlowManager.Instance.GoToMainMenu();
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

            // Energy Bar and Text (max from config, no hardcoded cap)
            if (sideAEnergyBar != null)
            {
                sideAEnergyBar.value = (vm.HasSession && vm.SideAEnergyMax > 0f)
                    ? Mathf.Clamp01(vm.SideAEnergy / vm.SideAEnergyMax) : 0f;
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
                var txt = pauseButton.GetComponentInChildren<TMP_Text>();
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

#if UNITY_EDITOR
        // ── Editor-only debug HUD builder ──────────────────────────────────────
        // Right-click this component in the Inspector → "Build Debug HUD" to
        // generate a throwaway uGUI layout and auto-assign all SerializeFields.
        // This is a quick prototype tool, NOT a final art-directed layout.

        [ContextMenu("Build Debug HUD")]
        private void BuildDebugHud()
        {
            EnsureCanvasAndEventSystem();
            RectTransform root = (RectTransform)transform;

            // Top-left status texts
            tickText         = MakeText("TickText",         root, new Vector2(0, 1), new Vector2(20, -20),  new Vector2(300, 28), "Tick: 0",      TextAlignmentOptions.TopLeft);
            statusText       = MakeText("StatusText",       root, new Vector2(0, 1), new Vector2(20, -50),  new Vector2(420, 28), "",             TextAlignmentOptions.TopLeft);
            opponentAutoText = MakeText("OpponentAutoText", root, new Vector2(0, 1), new Vector2(20, -80),  new Vector2(300, 28), "AUTO OFF",     TextAlignmentOptions.TopLeft);
            selectedInfoText = MakeText("SelectedInfoText", root, new Vector2(0, 1), new Vector2(20, -110), new Vector2(420, 28), "No selection", TextAlignmentOptions.TopLeft);
            sideAEnergyText  = MakeText("SideAEnergyText",  root, new Vector2(0, 1), new Vector2(20, -140), new Vector2(200, 28), "0.0",          TextAlignmentOptions.TopLeft);

            // Bars (top-left stacked)
            sideAEnergyBar = MakeSlider("SideAEnergyBar", root, new Vector2(0, 1), new Vector2(20, -170), new Vector2(220, 18), new Color(0.2f, 0.8f, 0.9f, 1f));
            sideABaseHpBar = MakeSlider("SideABaseHpBar", root, new Vector2(0, 1), new Vector2(20, -196), new Vector2(220, 18), new Color(0.3f, 0.6f, 1.0f, 1f));
            sideBBaseHpBar = MakeSlider("SideBBaseHpBar", root, new Vector2(0, 1), new Vector2(20, -222), new Vector2(220, 18), new Color(1.0f, 0.4f, 0.4f, 1f));

            // Lane prev/next (above deck, bottom-left)
            lanePrevButton = MakeButton("LanePrevButton", root, new Vector2(0, 0), new Vector2(20,  200), new Vector2(60, 40), "<");
            laneNextButton = MakeButton("LaneNextButton", root, new Vector2(0, 0), new Vector2(90,  200), new Vector2(60, 40), ">");

            // Deck slot buttons (bottom row) + cooldown labels
            slotSelectButtons = new Button[4];
            slotCooldownTexts = new TMP_Text[4];
            for (int i = 0; i < 4; i++)
            {
                Button b = MakeButton("Slot_" + i, root, new Vector2(0, 0), new Vector2(20 + i * 150, 130), new Vector2(140, 60), "Slot " + (i + 1));
                slotSelectButtons[i] = b;
                slotCooldownTexts[i] = MakeText("CooldownText_" + i, (RectTransform)b.transform, new Vector2(0.5f, 0f), new Vector2(0, 6), new Vector2(140, 20), "", TextAlignmentOptions.Center);
            }

            // Action buttons (bottom-right)
            spawnButton  = MakeButton("SpawnButton",  root, new Vector2(1, 0), new Vector2(-20, 200), new Vector2(180, 40), "SPAWN DRONE");
            deployButton = MakeButton("DeployButton", root, new Vector2(1, 0), new Vector2(-20, 155), new Vector2(180, 40), "DEPLOY PILOT");
            recallButton = MakeButton("RecallButton", root, new Vector2(1, 0), new Vector2(-20, 110), new Vector2(180, 40), "RECALL PILOT");

            // System buttons (top-right)
            startButton = MakeButton("StartButton", root, new Vector2(1, 1), new Vector2(-20, -20),  new Vector2(160, 36), "START BATTLE");
            pauseButton = MakeButton("PauseButton", root, new Vector2(1, 1), new Vector2(-20, -60),  new Vector2(160, 36), "PAUSE");
            stepButton  = MakeButton("StepButton",  root, new Vector2(1, 1), new Vector2(-20, -100), new Vector2(160, 36), "STEP");
            autoButton  = MakeButton("AutoButton",  root, new Vector2(1, 1), new Vector2(-20, -140), new Vector2(160, 36), "AUTO AI");

            // Result panel (full-screen dim, hidden by default)
            resultPanel = MakePanel("ResultPanel", root);
            resultText  = MakeText("ResultText", (RectTransform)resultPanel.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500, 90), "RESULT", TextAlignmentOptions.Center);
            resultText.fontSize = 48;
            resultPanel.SetActive(false);

            if (presenter == null) presenter = FindAnyObjectByType<BattlePresenter>();

            Debug.Log("[UI_BattleHud] Debug HUD built and fields assigned. " +
                      "If 'Presenter' is still empty, assign it (or it auto-resolves at runtime). Enter Play and press START BATTLE.");
        }

        private void EnsureCanvasAndEventSystem()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.GetComponent<Canvas>();
                if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                if (GetComponent<CanvasScaler>() == null) gameObject.AddComponent<CanvasScaler>();
                if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            }
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
        }

        private static RectTransform NewUI(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void SetAnchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        private static TMP_Text MakeText(string name, RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, string text, TextAlignmentOptions align)
        {
            RectTransform rt = NewUI(name, parent);
            SetAnchor(rt, anchor, pos, size);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = 18;
            t.alignment = align;
            t.color = Color.white;
            return t;
        }

        private static Button MakeButton(string name, RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, string label)
        {
            RectTransform rt = NewUI(name, parent);
            SetAnchor(rt, anchor, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.20f, 0.20f, 0.26f, 0.95f);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            RectTransform labelRt = NewUI("Text", rt);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;
            labelRt.anchoredPosition = Vector2.zero;
            var t = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = label;
            t.fontSize = 16;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            return btn;
        }

        private static Slider MakeSlider(string name, RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color fillColor)
        {
            RectTransform rt = NewUI(name, parent);
            SetAnchor(rt, anchor, pos, size);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

            var slider = rt.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;

            RectTransform fillArea = NewUI("Fill Area", rt);
            fillArea.anchorMin = Vector2.zero;
            fillArea.anchorMax = Vector2.one;
            fillArea.sizeDelta = Vector2.zero;
            fillArea.anchoredPosition = Vector2.zero;

            RectTransform fill = NewUI("Fill", fillArea);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.sizeDelta = Vector2.zero;
            fill.anchoredPosition = Vector2.zero;
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.color = fillColor;

            slider.fillRect = fill;
            slider.targetGraphic = fillImg;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            return slider;
        }

        private static GameObject MakePanel(string name, RectTransform parent)
        {
            RectTransform rt = NewUI(name, parent);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.6f);
            return rt.gameObject;
        }
#endif
    }
}
