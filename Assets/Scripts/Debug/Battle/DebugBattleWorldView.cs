using System.Collections.Generic;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.State;
using UnityEngine;

namespace FrontierBastion.Client.DebugBattle
{
    /// <summary>
    /// SpriteRenderer-based world-space view of the debug battle.
    /// Complements the IMGUI <see cref="DebugBattleStageView"/> — both run simultaneously.
    /// All GameObjects are created at runtime (no prefabs, no scene assets, no ProjectSettings).
    ///
    /// Readability features (Phase 2r):
    ///   • TextMesh labels P1/P2/E1/E2 above each entity marker (display-order only, no gameplay meaning).
    ///   • Base HP colour blends from near-black (0 HP) to full colour (full HP).
    ///   • Non-selected lane bars and their markers are dimmed to alpha × 0.35.
    ///   • A result banner (VICTORY / DEFEAT / TIMEOUT …) appears on battle termination.
    ///
    /// World coordinate convention (matches OnDrawGizmos in DebugBattleRunner):
    ///   X axis : -5 = player base wall,  +5 = enemy base wall
    ///   Y axis : lane i at  y = -1.5 × i  (lane 0 → y=0, lane 1 → y=-1.5, …)
    ///   Z axis : 0 for sprites;  -0.1 for TextMesh so they render in front.
    ///
    /// Result determination uses BattleState.IsTerminated / EndReason / HP values,
    /// so DebugBattleRunner.Render signature is unchanged.
    ///
    /// Entity markers and labels are pooled: activated/deactivated each frame, never
    /// destroyed during play.
    /// </summary>
    internal sealed class DebugBattleWorldView : MonoBehaviour
    {
        // ── World layout ──────────────────────────────────────────────────────
        private const float LaneHalfWidth = 5f;      // world x: -5 (player) .. +5 (enemy)
        private const float LaneYStep     = -1.5f;   // world Y delta per lane index
        private const float LaneBarThick  = 0.07f;   // thin horizontal bar height in world units
        private const float BaseWidth     = 0.35f;   // base pillar width in world units
        private const float BaseYPadding  = 0.40f;   // extra Y padding above/below lane span
        private const float EntitySize    = 0.28f;   // square marker side in world units
        private const float EntityYSpread = 0.20f;   // Y offset per entity index (reduces overlap)

        // ── Display-only motion / feedback constants ──────────────────────────
        // All values are used exclusively for visual presentation.
        // Time.time is the only time source; it is NEVER passed to BattleSim.Core.
        /// <summary>Vertical bob amplitude in world units.</summary>
        private const float BobAmplitude          = 0.04f;
        /// <summary>Bob oscillation speed in radians per second.</summary>
        private const float BobSpeed              = 3.5f;
        /// <summary>Max extra scale added during idle pulse (world units).</summary>
        private const float ScalePulseAmplitude   = 0.05f;
        /// <summary>Idle scale pulse speed in rad/s.</summary>
        private const float ScalePulseSpeed       = 2.0f;
        /// <summary>Per-marker phase offset (radians) to desync neighbouring markers.</summary>
        private const float PhaseOffset           = 1.2f;
        /// <summary>Normalised distance [0..1] below which a contact pulse is triggered.</summary>
        private const float ContactPulseDistance  = 0.15f;
        /// <summary>Extra scale bonus at full contact (world units).</summary>
        private const float ContactPulseAmplitude = 0.08f;
        /// <summary>Speed of the contact scale pulse in rad/s (faster = more urgent).</summary>
        private const float ContactPulseSpeed     = 6.0f;
        /// <summary>HP ratio below which the base starts flickering.</summary>
        private const float BaseLowHpThreshold    = 0.30f;
        /// <summary>Max extra brightness added per flicker cycle.</summary>
        private const float BasePulseBrightness   = 0.12f;
        /// <summary>Base flicker speed in rad/s.</summary>
        private const float BasePulseSpeed        = 5.0f;

        // ── TextMesh label sizing ─────────────────────────────────────────────
        /// <summary>
        /// Character height in world units for entity labels (P1/E1).
        /// At EntitySize=0.28, this gives a label slightly narrower than the marker.
        /// </summary>
        private const float LabelCharSize    = 0.18f;
        private const int   LabelFontSize    = 10;    // rendering quality
        /// <summary>Character height for the result banner.</summary>
        private const float ResultCharSize   = 0.35f;
        private const int   ResultFontSize   = 14;

        // ── Sorting orders (all on "Default" sorting layer) ───────────────────
        private const int SortLane   = 0;
        private const int SortBase   = 1;
        private const int SortMarker = 2;
        private const int SortLabel  = 3;
        private const int SortResult = 5;   // result banner above everything

        // ── Colours ───────────────────────────────────────────────────────────
        private static readonly Color ColLaneSelected  = new Color(1.00f, 0.85f, 0.00f, 1.00f);
        private static readonly Color ColLaneDimmed    = new Color(0.28f, 0.28f, 0.28f, 0.32f);
        private static readonly Color ColPlayerBase    = new Color(0.25f, 0.50f, 1.00f, 0.90f);
        private static readonly Color ColEnemyBase     = new Color(1.00f, 0.30f, 0.30f, 0.90f);
        /// <summary>Colour used when a base reaches 0 HP (near-black).</summary>
        private static readonly Color ColBaseDestroyed = new Color(0.15f, 0.15f, 0.20f, 0.70f);
        private static readonly Color ColPlayerMarker  = new Color(0.00f, 0.90f, 0.90f, 1.00f);
        private static readonly Color ColEnemyMarker   = new Color(1.00f, 0.55f, 0.10f, 1.00f);
        private static readonly Color ColPlayerLabel   = new Color(0.75f, 1.00f, 1.00f, 1.00f);
        private static readonly Color ColEnemyLabel    = new Color(1.00f, 0.90f, 0.60f, 1.00f);

        // ── Shared 1×1 white sprite (lazy-init, used by every SpriteRenderer) ─
        private static Sprite _sharedSprite;

        private static Sprite GetSprite()
        {
            if (_sharedSprite != null) return _sharedSprite;

            Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color32[] px  = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            tex.hideFlags  = HideFlags.DontSave;

            _sharedSprite           = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            _sharedSprite.hideFlags = HideFlags.DontSave;
            return _sharedSprite;
        }

        // ── Runtime state ─────────────────────────────────────────────────────
        private DebugBattleScenario _lastScenario;

        // Structural objects — rebuilt when scenario switches
        private readonly List<SpriteRenderer> _laneSRs = new List<SpriteRenderer>();
        private SpriteRenderer                _playerBaseSR;
        private SpriteRenderer                _enemyBaseSR;

        // Entity marker pool (SpriteRenderer) + label pool (TextMesh) — parallel arrays
        // Both live under _markerRoot as separate GameObjects.
        // Label GO is independent so its scale is not inherited from the marker.
        private Transform                  _markerRoot;
        private readonly List<SpriteRenderer> _markerPool = new List<SpriteRenderer>();
        private readonly List<TextMesh>       _labelPool  = new List<TextMesh>();

        // Result banner — one TextMesh under this.transform, shown on battle end
        private TextMesh _resultLabel;

        // Optional camera owned by this view (created only when Camera.main is absent)
        private Camera _ownedCamera;

        // ── MonoBehaviour ─────────────────────────────────────────────────────

        private void Awake()
        {
            GameObject mrGo = new GameObject("MarkerPool");
            mrGo.transform.SetParent(transform, worldPositionStays: false);
            mrGo.hideFlags = HideFlags.DontSave;
            _markerRoot = mrGo.transform;

            EnsureCamera();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the existing <see cref="DebugBattleWorldView"/> child of <paramref name="host"/>,
        /// or creates one if none exists.
        /// </summary>
        public static DebugBattleWorldView GetOrCreate(GameObject host)
        {
            DebugBattleWorldView existing =
                host.GetComponentInChildren<DebugBattleWorldView>(includeInactive: true);
            if (existing != null) return existing;

            GameObject viewGo = new GameObject("Debug Battle World View");
            viewGo.transform.SetParent(host.transform, worldPositionStays: false);
            viewGo.hideFlags = HideFlags.DontSave;
            return viewGo.AddComponent<DebugBattleWorldView>();
        }

        /// <summary>
        /// Refreshes the world-space view to match the current simulation state.
        /// Call once per frame from <see cref="DebugBattleRunner"/>'s LateUpdate.
        /// The signature is unchanged from the previous world-view implementation.
        /// </summary>
        public void Render(
            DebugBattleScenario scenario,
            BattleState         state,
            string              selectedLaneId)
        {
            if (scenario == null)
            {
                DeactivateAllMarkers();
                HideResultLabel();
                return;
            }

            // Rebuild structural objects when scenario switches (F1 / F2 / F3 / F4)
            if (!ReferenceEquals(scenario, _lastScenario))
            {
                RebuildStructure(scenario);
                _lastScenario = scenario;
            }

            // Lane highlight: selected = yellow, others = dimmed
            LaneDefinition[] lanes = scenario.Config.Lanes;
            for (int i = 0; i < _laneSRs.Count && i < lanes.Length; i++)
            {
                _laneSRs[i].color = (lanes[i].LaneId == selectedLaneId)
                    ? ColLaneSelected
                    : ColLaneDimmed;
            }

            if (state != null)
            {
                SyncMarkers(state, lanes, selectedLaneId);
                UpdateBaseHp(state, scenario);
                UpdateResultLabel(state, scenario);
            }
            else
            {
                DeactivateAllMarkers();
                HideResultLabel();
            }
        }

        // ── Structure (lanes + bases + result banner) ─────────────────────────

        private void RebuildStructure(DebugBattleScenario scenario)
        {
            // Tear down previous structural renderers
            foreach (SpriteRenderer sr in _laneSRs)
                if (sr != null) Destroy(sr.gameObject);
            _laneSRs.Clear();

            if (_playerBaseSR != null) { Destroy(_playerBaseSR.gameObject); _playerBaseSR = null; }
            if (_enemyBaseSR  != null) { Destroy(_enemyBaseSR.gameObject);  _enemyBaseSR  = null; }
            if (_resultLabel  != null) { Destroy(_resultLabel.gameObject);  _resultLabel  = null; }

            LaneDefinition[] lanes     = scenario.Config.Lanes;
            int              laneCount = (lanes != null) ? lanes.Length : 0;

            // ── Lane bars ─────────────────────────────────────────────────────
            for (int i = 0; i < laneCount; i++)
            {
                float y  = LaneYStep * i;
                SpriteRenderer sr = MakeSR("Lane_" + lanes[i].LaneId, transform, SortLane);
                sr.transform.localPosition = new Vector3(0f, y, 0f);
                sr.transform.localScale    = new Vector3(LaneHalfWidth * 2f, LaneBarThick, 1f);
                sr.color = ColLaneDimmed;
                _laneSRs.Add(sr);
            }

            // ── Shared bases (single pillar spanning all lanes) ───────────────
            float topY    = 0f;
            float bottomY = (laneCount > 1) ? LaneYStep * (laneCount - 1) : 0f;
            float midY    = (topY + bottomY) * 0.5f;
            float spanH   = Mathf.Max(0.8f, Mathf.Abs(topY - bottomY) + BaseYPadding * 2f);

            _playerBaseSR = MakeSR("PlayerBase", transform, SortBase);
            _playerBaseSR.transform.localPosition = new Vector3(-LaneHalfWidth, midY, 0f);
            _playerBaseSR.transform.localScale    = new Vector3(BaseWidth, spanH, 1f);
            _playerBaseSR.color = ColPlayerBase;

            _enemyBaseSR = MakeSR("EnemyBase", transform, SortBase);
            _enemyBaseSR.transform.localPosition  = new Vector3(LaneHalfWidth,  midY, 0f);
            _enemyBaseSR.transform.localScale     = new Vector3(BaseWidth, spanH, 1f);
            _enemyBaseSR.color = ColEnemyBase;

            // ── Result banner (hidden until battle ends) ──────────────────────
            // Positioned 1 world unit above the topmost lane.
            _resultLabel = MakeLabel("ResultLabel", transform, SortResult);
            _resultLabel.transform.localPosition = new Vector3(0f, topY + 1.0f, -0.1f);
            _resultLabel.fontSize      = ResultFontSize;
            _resultLabel.characterSize = ResultCharSize;
            _resultLabel.alignment     = TextAlignment.Center;
            _resultLabel.anchor        = TextAnchor.MiddleCenter;
            _resultLabel.gameObject.SetActive(false);

            // ── Reposition owned camera to frame the scene ────────────────────
            if (_ownedCamera != null)
            {
                _ownedCamera.transform.localPosition = new Vector3(0f, midY, -10f);
                _ownedCamera.orthographicSize        = Mathf.Max(2f, spanH * 0.5f + 1.2f);
            }
        }

        // ── Entity markers + labels ────────────────────────────────────────────

        /// <summary>
        /// Iterates BattleState lane entities, positions markers and labels,
        /// and deactivates unused pool entries.
        ///
        /// Display-only motion applied here:
        ///   • Y bob: Mathf.Sin(Time.time × BobSpeed + index × PhaseOffset) × BobAmplitude
        ///   • Scale pulse: idle sine + contact-proximity boost
        ///   • Label tracks the marker's animated top edge so it never drifts apart
        /// Time.time is used exclusively for visual effect and is never sent to BattleSim.Core.
        /// </summary>
        private void SyncMarkers(BattleState state, LaneDefinition[] lanes, string selectedLaneId)
        {
            float tNow       = Time.time;   // display clock only — never forwarded to Core
            int   activeCount = 0;

            for (int i = 0; i < state.Lanes.Count && i < lanes.Length; i++)
            {
                LaneState      laneState    = state.Lanes[i];
                LaneDefinition laneDef      = lanes[i];
                bool           laneSelected = (laneDef.LaneId == selectedLaneId);
                float          laneY        = LaneYStep * i;
                long           laneLen      = laneDef.LaneLengthMilli;

                // Pre-pass: contact intensity for this lane (display-only, not combat logic).
                // Ranges 0 (no contact) to 1 (full contact).
                float contactIntensity = ComputeLaneContactIntensity(laneState, laneLen);

                // Per-lane P#/E# counters (display order only, no gameplay meaning).
                int pCount = 0;
                int eCount = 0;

                for (int j = 0; j < laneState.Entities.Count; j++)
                {
                    BattleEntity entity = laneState.Entities[j];

                    // ── Position mapping: PositionMilli → world X ─────────────
                    float t      = (laneLen > 0L)
                        ? Mathf.Clamp01((float)entity.PositionMilli / (float)laneLen)
                        : 0f;
                    float worldX = Mathf.Lerp(-LaneHalfWidth, LaneHalfWidth, t);
                    float baseY  = laneY + j * EntityYSpread;

                    // ── Display-only motion ───────────────────────────────────
                    // Each marker slot has a unique phase so neighbours don't bob in unison.
                    float markerPhase = tNow * BobSpeed + activeCount * PhaseOffset;
                    float bobY        = Mathf.Sin(markerPhase) * BobAmplitude;
                    float displayY    = baseY + bobY;

                    // Scale: idle pulse + contact proximity boost
                    float idleSin      = 0.5f + 0.5f * Mathf.Sin(tNow * ScalePulseSpeed + activeCount * PhaseOffset * 0.7f);
                    float idleBonus    = idleSin * ScalePulseAmplitude;
                    float contactBonus = contactIntensity
                        * ContactPulseAmplitude
                        * (0.5f + 0.5f * Mathf.Sin(tNow * ContactPulseSpeed + activeCount * 0.5f));
                    float displayScale = EntitySize + idleBonus + contactBonus;

                    // ── Type label ────────────────────────────────────────────
                    bool   isPlayer  = entity.Side == BattleSide.SideA;
                    int    typeIndex = isPlayer ? ++pCount : ++eCount;
                    string labelText = (isPlayer ? "P" : "E") + typeIndex;

                    // ── Colours with optional lane dimming ────────────────────
                    Color markerColor = isPlayer ? ColPlayerMarker : ColEnemyMarker;
                    Color labelColor  = isPlayer ? ColPlayerLabel   : ColEnemyLabel;
                    if (!laneSelected)
                    {
                        markerColor.a *= 0.35f;
                        labelColor.a  *= 0.35f;
                    }

                    // ── Marker (SpriteRenderer) ───────────────────────────────
                    SpriteRenderer marker = GetPooledMarker(activeCount);
                    marker.transform.localPosition = new Vector3(worldX, displayY, 0f);
                    marker.transform.localScale    = new Vector3(displayScale, displayScale, 1f);
                    marker.color = markerColor;
                    marker.gameObject.SetActive(true);

                    // ── Label (TextMesh) — tracks the animated marker top ─────
                    // Sibling of the marker under _markerRoot: not affected by marker scale.
                    // Top-edge of marker = displayY + displayScale * 0.5 (centred pivot).
                    TextMesh lm = GetPooledLabel(activeCount);
                    lm.transform.localPosition = new Vector3(
                        worldX,
                        displayY + displayScale * 0.5f + 0.04f,  // just above marker top
                        -0.1f);                                    // in front of sprites
                    lm.text  = labelText;
                    lm.color = labelColor;
                    lm.gameObject.SetActive(true);

                    activeCount++;
                }
            }

            // Deactivate unused pool entries
            for (int k = activeCount; k < _markerPool.Count; k++)
                _markerPool[k].gameObject.SetActive(false);
            for (int k = activeCount; k < _labelPool.Count; k++)
                _labelPool[k].gameObject.SetActive(false);
        }

        private void DeactivateAllMarkers()
        {
            for (int k = 0; k < _markerPool.Count; k++)
                if (_markerPool[k] != null) _markerPool[k].gameObject.SetActive(false);
            for (int k = 0; k < _labelPool.Count; k++)
                if (_labelPool[k] != null) _labelPool[k].gameObject.SetActive(false);
        }

        private SpriteRenderer GetPooledMarker(int index)
        {
            while (_markerPool.Count <= index)
            {
                SpriteRenderer sr = MakeSR("Marker_" + _markerPool.Count, _markerRoot, SortMarker);
                sr.gameObject.SetActive(false);
                _markerPool.Add(sr);
            }
            return _markerPool[index];
        }

        private TextMesh GetPooledLabel(int index)
        {
            while (_labelPool.Count <= index)
            {
                TextMesh tm = MakeLabel("Label_" + _labelPool.Count, _markerRoot, SortLabel);
                tm.gameObject.SetActive(false);
                _labelPool.Add(tm);
            }
            return _labelPool[index];
        }

        // ── Base HP visual ────────────────────────────────────────────────────

        /// <summary>
        /// Blends each base pillar's colour from near-black (HP=0) to full colour (HP=max).
        /// HP ratio is computed from Fp.Raw values — no cast operator required.
        ///
        /// Display-only addition: when HP drops below <see cref="BaseLowHpThreshold"/>,
        /// a brightness flicker is overlaid to signal critical state.
        /// Time.time is used exclusively for the flicker — never forwarded to Core.
        /// </summary>
        private void UpdateBaseHp(BattleState state, DebugBattleScenario scenario)
        {
            if (_playerBaseSR == null || _enemyBaseSR == null) return;

            float pRatio = HpRatio(GetSideBaseHp(state, BattleSide.SideA), scenario.Config.SideA.BaseInitialHp);
            float eRatio = HpRatio(GetSideBaseHp(state, BattleSide.SideB), scenario.Config.SideB.BaseInitialHp);

            Color pCol = Color.Lerp(ColBaseDestroyed, ColPlayerBase, pRatio);
            Color eCol = Color.Lerp(ColBaseDestroyed, ColEnemyBase,  eRatio);

            // Low-HP flicker: intensity scales with how close the base is to 0.
            // pRatio > 0 guard avoids flickering on an already-destroyed base.
            if (pRatio > 0f && pRatio < BaseLowHpThreshold)
            {
                float danger = 1f - pRatio / BaseLowHpThreshold;    // 0→1 as HP→0
                float flicker = (0.5f + 0.5f * Mathf.Sin(Time.time * BasePulseSpeed)) * BasePulseBrightness * danger;
                pCol = new Color(Mathf.Min(1f, pCol.r + flicker),
                                 Mathf.Min(1f, pCol.g + flicker),
                                 Mathf.Min(1f, pCol.b + flicker),
                                 pCol.a);
            }
            if (eRatio > 0f && eRatio < BaseLowHpThreshold)
            {
                float danger  = 1f - eRatio / BaseLowHpThreshold;
                // Slightly offset phase so player/enemy don't flicker in sync
                float flicker = (0.5f + 0.5f * Mathf.Sin(Time.time * BasePulseSpeed * 1.1f)) * BasePulseBrightness * danger;
                eCol = new Color(Mathf.Min(1f, eCol.r + flicker),
                                 Mathf.Min(1f, eCol.g + flicker),
                                 Mathf.Min(1f, eCol.b + flicker),
                                 eCol.a);
            }

            _playerBaseSR.color = pCol;
            _enemyBaseSR.color  = eCol;
        }

        // ── Result banner ─────────────────────────────────────────────────────

        /// <summary>
        /// Shows the result banner when the battle terminates.
        /// Outcome is derived from BattleState fields — BattleResult is not required.
        ///   EnemyBaseDestroyed → VICTORY
        ///   PlayerBaseDestroyed → DEFEAT
        ///   TimeOut → compare HP ratios (player > enemy = TIMEOUT VICTORY, else TIMEOUT DEFEAT)
        /// </summary>
        private void UpdateResultLabel(BattleState state, DebugBattleScenario scenario)
        {
            if (_resultLabel == null) return;

            if (!state.IsTerminated)
            {
                _resultLabel.gameObject.SetActive(false);
                return;
            }

            string text;
            Color  col;

            // Local player = SideA. Victory when SideA wins.
            switch (state.EndReason)
            {
                case BattleEndReason.SideBBaseDestroyed:  // SideA wins
                    text = "VICTORY!";
                    col  = new Color(0.30f, 1.00f, 0.40f);
                    break;

                case BattleEndReason.SideABaseDestroyed:  // SideB wins
                    text = "DEFEAT";
                    col  = new Color(1.00f, 0.30f, 0.30f);
                    break;

                default: // BattleEndReason.TimeOut — resolve by HP ratio + tiebreaker
                    float aRatio = HpRatio(GetSideBaseHp(state, BattleSide.SideA), scenario.Config.SideA.BaseInitialHp);
                    float bRatio = HpRatio(GetSideBaseHp(state, BattleSide.SideB), scenario.Config.SideB.BaseInitialHp);
                    BattleSide timeoutWinner;
                    if (aRatio > bRatio)      timeoutWinner = BattleSide.SideA;
                    else if (bRatio > aRatio) timeoutWinner = BattleSide.SideB;
                    else                      timeoutWinner = scenario.Config.TimeOutTieWinnerSide;
                    bool localWins = timeoutWinner == BattleSide.SideA;
                    text = localWins ? "TIMEOUT VICTORY" : "TIMEOUT DEFEAT";
                    col  = localWins ? new Color(0.30f, 1.00f, 0.40f) : new Color(1.00f, 0.30f, 0.30f);
                    break;
            }

            _resultLabel.text  = text;
            _resultLabel.color = col;
            _resultLabel.gameObject.SetActive(true);
        }

        private void HideResultLabel()
        {
            if (_resultLabel != null)
                _resultLabel.gameObject.SetActive(false);
        }

        // ── Camera ────────────────────────────────────────────────────────────

        private void EnsureCamera()
        {
            if (Camera.main != null)
                return;

            GameObject camGo = new GameObject("Debug Battle Camera");
            camGo.transform.SetParent(transform, worldPositionStays: false);
            camGo.transform.localPosition = new Vector3(0f, -0.75f, -10f);
            camGo.hideFlags = HideFlags.DontSave;

            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic     = true;
            cam.orthographicSize = 3f;
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = new Color(0.08f, 0.08f, 0.12f, 1f);
            cam.nearClipPlane    = 0.1f;
            cam.farClipPlane     = 20f;
            cam.depth            = 0f;
            cam.tag              = "MainCamera";

            _ownedCamera = cam;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private SpriteRenderer MakeSR(string goName, Transform parent, int sortingOrder)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.hideFlags = HideFlags.DontSave;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = GetSprite();
            sr.sortingOrder = sortingOrder;
            sr.color        = Color.white;
            return sr;
        }

        /// <summary>
        /// Creates a child GameObject with a <see cref="TextMesh"/> component.
        /// The associated <see cref="MeshRenderer"/> sortingOrder is set to keep
        /// labels above sprites in the same camera.
        /// </summary>
        private TextMesh MakeLabel(string goName, Transform parent, int sortingOrder)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.hideFlags = HideFlags.DontSave;

            var tm = go.AddComponent<TextMesh>();
            tm.fontSize      = LabelFontSize;
            tm.characterSize = LabelCharSize;
            tm.alignment     = TextAlignment.Center;
            tm.anchor        = TextAnchor.LowerCenter;
            tm.color         = Color.white;

            // TextMesh shares its GameObject with a MeshRenderer — set sortingOrder there too
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = sortingOrder;

            return tm;
        }

        /// <summary>
        /// Returns a contact intensity in [0..1] based on the minimum normalised distance
        /// between any player entity and any enemy entity in <paramref name="lane"/>.
        /// Returns 0 when no opposite-side pair exists or when all pairs are farther apart
        /// than <see cref="ContactPulseDistance"/>.
        ///
        /// Used exclusively for display (scale pulse boost) — not for combat logic.
        /// </summary>
        private static float ComputeLaneContactIntensity(LaneState lane, long laneLen)
        {
            if (laneLen <= 0L) return 0f;

            float minNormDist = 1f;

            for (int p = 0; p < lane.Entities.Count; p++)
            {
                if (lane.Entities[p].Side != BattleSide.SideA) continue;

                for (int e = 0; e < lane.Entities.Count; e++)
                {
                    if (lane.Entities[e].Side != BattleSide.SideB) continue;

                    long diff = lane.Entities[p].PositionMilli - lane.Entities[e].PositionMilli;
                    if (diff < 0L) diff = -diff;

                    float normDist = (float)diff / (float)laneLen;
                    if (normDist < minNormDist) minNormDist = normDist;
                }
            }

            if (minNormDist >= ContactPulseDistance) return 0f;
            return 1f - minNormDist / ContactPulseDistance;
        }

        /// <summary>
        /// Returns the BaseHp for <paramref name="side"/> from <paramref name="state"/>.
        /// Returns Fp.Zero if the side is not found.
        /// Read-only — never used for combat judgment.
        /// </summary>
        private static Fp GetSideBaseHp(BattleState state, BattleSide side)
        {
            if (state?.Sides == null) return Fp.Zero;
            for (int i = 0; i < state.Sides.Count; i++)
                if (state.Sides[i].Side == side) return state.Sides[i].BaseHp;
            return Fp.Zero;
        }

        /// <summary>
        /// Returns [0..1] HP ratio from Fp values.
        /// Uses Fp.Raw directly — no cast operator required.
        /// Scale cancels in the division since both values share the same Fp scale.
        /// </summary>
        private static float HpRatio(Fp current, Fp max)
        {
            if (max.Raw <= 0L) return 0f;
            return Mathf.Clamp01((float)current.Raw / (float)max.Raw);
        }
    }
}
