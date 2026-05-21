using System.Collections.Generic;
using BattleSim.Core.Config;
using BattleSim.Core.State;
using UnityEngine;

namespace FrontierBastion.Client.DebugBattle
{
    /// <summary>
    /// SpriteRenderer-based world-space view of the debug battle.
    /// Complements the IMGUI <see cref="DebugBattleStageView"/> — both run simultaneously.
    /// All GameObjects are created at runtime (no prefabs, no scene assets, no ProjectSettings).
    ///
    /// World coordinate convention (matches OnDrawGizmos in DebugBattleRunner):
    ///   X axis : -5 = player base wall,  +5 = enemy base wall
    ///   Y axis : lane i is at  y = -1.5 * i  (lane 0 → y=0, lane 1 → y=-1.5, …)
    ///   Z axis : 0 for all world objects; draw order via SpriteRenderer.sortingOrder
    ///
    /// Entity position mapping:
    ///   t = Clamp01(entity.PositionMilli / laneLength)
    ///   worldX = Lerp(-5, +5, t)
    ///   Player entities spawn at t=0 (x=-5) and advance toward t=1 (x=+5).
    ///   Enemy  entities spawn at t=1 (x=+5) and advance toward t=0 (x=-5).
    ///
    /// Entity markers are pooled: activated/deactivated each frame, never destroyed during play.
    /// </summary>
    internal sealed class DebugBattleWorldView : MonoBehaviour
    {
        // ── World layout ──────────────────────────────────────────────────────
        private const float LaneHalfWidth = 5f;      // world x: -5 (player) to +5 (enemy)
        private const float LaneYStep     = -1.5f;   // world Y delta per lane index
        private const float LaneBarThick  = 0.07f;   // thin horizontal bar height in world units
        private const float BaseWidth     = 0.35f;   // base pillar width in world units
        private const float BaseYPadding  = 0.40f;   // extra Y padding above/below lane span
        private const float EntitySize    = 0.28f;   // square marker side in world units
        private const float EntityYSpread = 0.20f;   // Y offset per entity index (reduces overlap)

        // ── Sorting orders (all on "Default" sorting layer) ───────────────────
        private const int SortLane   = 0;
        private const int SortBase   = 1;
        private const int SortMarker = 2;

        // ── Colours ───────────────────────────────────────────────────────────
        private static readonly Color ColLaneNormal   = new Color(0.40f, 0.40f, 0.40f, 0.80f);
        private static readonly Color ColLaneSelected = new Color(1.00f, 0.85f, 0.00f, 1.00f);
        private static readonly Color ColPlayerBase   = new Color(0.25f, 0.50f, 1.00f, 0.90f);
        private static readonly Color ColEnemyBase    = new Color(1.00f, 0.30f, 0.30f, 0.90f);
        private static readonly Color ColPlayerMarker = new Color(0.00f, 0.90f, 0.90f, 1.00f);
        private static readonly Color ColEnemyMarker  = new Color(1.00f, 0.55f, 0.10f, 1.00f);

        // ── Shared 1×1 white sprite (lazy-init, used for every SpriteRenderer) ─
        // All objects tint this white sprite via SpriteRenderer.color.
        // pixelsPerUnit = 4: a 4×4-pixel texture fills exactly 1×1 world unit at scale 1.
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

        // Structural objects — rebuilt when scenario changes
        private readonly List<SpriteRenderer> _laneSRs = new List<SpriteRenderer>();
        private SpriteRenderer                _playerBaseSR;
        private SpriteRenderer                _enemyBaseSR;

        // Entity marker pool — reused across ticks, never destroyed during play
        private Transform                     _markerRoot;
        private readonly List<SpriteRenderer> _markerPool = new List<SpriteRenderer>();

        // Optional camera created by this view when the scene has no Camera.main
        private Camera _ownedCamera;

        // ── MonoBehaviour ─────────────────────────────────────────────────────

        private void Awake()
        {
            // Dedicated sub-root keeps pooled markers out of the top-level hierarchy
            GameObject mrGo = new GameObject("MarkerPool");
            mrGo.transform.SetParent(transform, false);
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
        /// Call once per frame from <see cref="DebugBattleRunner.LateUpdate"/>.
        /// </summary>
        public void Render(
            DebugBattleScenario scenario,
            BattleState         state,
            string              selectedLaneId)
        {
            if (scenario == null)
            {
                DeactivateAllMarkers();
                return;
            }

            // Rebuild structural objects when scenario switches (F1 / F2 / F3 / F4)
            if (!ReferenceEquals(scenario, _lastScenario))
            {
                RebuildStructure(scenario);
                _lastScenario = scenario;
            }

            // Highlight the currently selected lane
            LaneDefinition[] lanes = scenario.Config.Lanes;
            for (int i = 0; i < _laneSRs.Count && i < lanes.Length; i++)
            {
                _laneSRs[i].color = (lanes[i].LaneId == selectedLaneId)
                    ? ColLaneSelected
                    : ColLaneNormal;
            }

            // Sync entity markers to BattleState
            if (state != null)
                SyncMarkers(state, lanes);
            else
                DeactivateAllMarkers();
        }

        // ── Structure (lanes + bases) ─────────────────────────────────────────

        private void RebuildStructure(DebugBattleScenario scenario)
        {
            // Destroy previous structural renderers
            foreach (SpriteRenderer sr in _laneSRs)
                if (sr != null) Destroy(sr.gameObject);
            _laneSRs.Clear();

            if (_playerBaseSR != null) { Destroy(_playerBaseSR.gameObject); _playerBaseSR = null; }
            if (_enemyBaseSR  != null) { Destroy(_enemyBaseSR.gameObject);  _enemyBaseSR  = null; }

            LaneDefinition[] lanes     = scenario.Config.Lanes;
            int              laneCount = (lanes != null) ? lanes.Length : 0;

            // ── Lane bars ─────────────────────────────────────────────────────
            for (int i = 0; i < laneCount; i++)
            {
                float y = LaneYStep * i;
                SpriteRenderer sr = MakeSR("Lane_" + lanes[i].LaneId, transform, SortLane);
                sr.transform.localPosition = new Vector3(0f, y, 0f);
                // Full lane width × thin bar height
                sr.transform.localScale    = new Vector3(LaneHalfWidth * 2f, LaneBarThick, 1f);
                sr.color = ColLaneNormal;
                _laneSRs.Add(sr);
            }

            // ── Shared bases (single pillar spanning the full lane area) ──────
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

            // Reposition owned camera to frame the new scene
            if (_ownedCamera != null)
            {
                _ownedCamera.transform.localPosition = new Vector3(0f, midY, -10f);
                _ownedCamera.orthographicSize        = Mathf.Max(2f, spanH * 0.5f + 1.2f);
            }
        }

        // ── Entity marker pool ────────────────────────────────────────────────

        private void SyncMarkers(BattleState state, LaneDefinition[] lanes)
        {
            int activeCount = 0;

            for (int i = 0; i < state.Lanes.Count && i < lanes.Length; i++)
            {
                LaneState      laneState = state.Lanes[i];
                LaneDefinition laneDef   = lanes[i];
                float          laneY    = LaneYStep * i;
                long           laneLen  = laneDef.LaneLengthMilli;

                for (int j = 0; j < laneState.Entities.Count; j++)
                {
                    BattleEntity   entity = laneState.Entities[j];
                    SpriteRenderer marker = GetPooledMarker(activeCount);

                    // Map PositionMilli → world X
                    // t=0 → player base (-LaneHalfWidth), t=1 → enemy base (+LaneHalfWidth)
                    float t      = (laneLen > 0L)
                        ? Mathf.Clamp01((float)entity.PositionMilli / (float)laneLen)
                        : 0f;
                    float worldX = Mathf.Lerp(-LaneHalfWidth, LaneHalfWidth, t);
                    // Small per-entity Y offset so overlapping entities are individually visible
                    float worldY = laneY + j * EntityYSpread;

                    marker.transform.localPosition = new Vector3(worldX, worldY, 0f);
                    marker.transform.localScale    = new Vector3(EntitySize, EntitySize, 1f);
                    marker.color = (entity.OwnerSide == OwnerSide.Player)
                        ? ColPlayerMarker
                        : ColEnemyMarker;

                    marker.gameObject.SetActive(true);
                    activeCount++;
                }
            }

            // Deactivate pool entries not used this frame
            for (int k = activeCount; k < _markerPool.Count; k++)
                _markerPool[k].gameObject.SetActive(false);
        }

        private void DeactivateAllMarkers()
        {
            for (int k = 0; k < _markerPool.Count; k++)
            {
                if (_markerPool[k] != null)
                    _markerPool[k].gameObject.SetActive(false);
            }
        }

        private SpriteRenderer GetPooledMarker(int index)
        {
            // Grow pool on demand — markers are never destroyed during play
            while (_markerPool.Count <= index)
            {
                SpriteRenderer sr = MakeSR("Marker_" + _markerPool.Count, _markerRoot, SortMarker);
                sr.gameObject.SetActive(false);
                _markerPool.Add(sr);
            }
            return _markerPool[index];
        }

        // ── Camera ────────────────────────────────────────────────────────────

        private void EnsureCamera()
        {
            // If the scene already has a camera, use it — don't create a conflicting one
            if (Camera.main != null)
                return;

            // No main camera found — create a minimal orthographic debug camera
            GameObject camGo = new GameObject("Debug Battle Camera");
            camGo.transform.SetParent(transform, worldPositionStays: false);
            camGo.transform.localPosition = new Vector3(0f, -0.75f, -10f);
            camGo.hideFlags = HideFlags.DontSave;

            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic     = true;
            cam.orthographicSize = 3f;                               // ~6 world units tall
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = new Color(0.08f, 0.08f, 0.12f, 1f);
            cam.nearClipPlane    = 0.1f;
            cam.farClipPlane     = 20f;
            cam.depth            = 0f;
            cam.tag              = "MainCamera";

            _ownedCamera = cam;
        }

        // ── Helper ────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a child GameObject with a SpriteRenderer using the shared white sprite.
        /// The object inherits the <paramref name="parent"/> transform.
        /// </summary>
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
    }
}
