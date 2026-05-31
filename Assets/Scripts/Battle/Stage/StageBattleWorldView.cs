using System;
using System.Collections.Generic;
using UnityEngine;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.State;
using BattleSim.Core.Events;
using FrontierBastion.Client.App;
using FrontierBastion.Client.UI;
using FrontierBastion.Client.Stage.View;

namespace FrontierBastion.Client.Stage
{
    /// <summary>
    /// Pure runtime-generated world space view of a Stage battle session (v0.5+v0.7 event-driven).
    /// Redesigned to use generic pools, dedicated View MonoBehaviours, and deterministic visibility cap + distance bucket.
    /// </summary>
    internal sealed class StageBattleWorldView : MonoBehaviour
    {
        private struct EntityStateWrapper
        {
            public string EntityId;
            public int NumericId;
            public string LaneId;
            public BattleSide Side;
            public long PositionMilli;
        }

        // View configuration constants
        private const float LaneBarThick = 0.07f;
        private const float BaseWidth = 0.35f;
        private const float EntitySize = 0.28f;

        // Visibility Cap Configuration
        private const int MaxVisibleEntities = 600; // per-side default 300
        private const int BucketSizeMilli = 200;

        // Prefab Resources paths
        private const string PrefabPathEntityMarker = "Battle/EntityMarker";
        private const string PrefabPathProjectile = "Battle/Projectile";
        private const string PrefabPathBaseColumn = "Battle/BaseColumn";
        private const string PrefabPathFloatingDamageText = "Battle/FloatingDamageText";
        private const string PrefabPathResultBanner = "Battle/ResultBanner";

        // Cached prefabs
        private GameObject _prefabEntityMarker;
        private GameObject _prefabProjectile;
        private GameObject _prefabBaseColumn;
        private GameObject _prefabFloatingDamageText;
        private GameObject _prefabResultBanner;


        private static Sprite _whiteSprite;

        private readonly List<GameObject> _lineObjects = new List<GameObject>();
        private BaseColumnView _baseAView;
        private BaseColumnView _baseBView;
        private ResultBannerView _resultBannerViewInstance;
        private Camera _spawnedCamera;

        // Pools (using generic GameObjectPool)
        private GameObjectPool<EntityMarkerView> _entityPool;
        private GameObjectPool<ProjectileView> _projectilePool;
        private GameObjectPool<BaseColumnView> _baseColumnPool;
        private GameObjectPool<FloatingTextView> _floatingTextPool;
        private GameObjectPool<ResultBannerView> _resultBannerPool;

        // Active Registries
        private readonly Dictionary<string, EntityMarkerView> _activeEntities = new Dictionary<string, EntityMarkerView>();
        private readonly Dictionary<string, ProjectileView> _activeProjectiles = new Dictionary<string, ProjectileView>();

        // Reusable lists to minimize garbage allocations
        private readonly List<EntityStateWrapper> _candidatesA = new List<EntityStateWrapper>();
        private readonly List<EntityStateWrapper> _candidatesB = new List<EntityStateWrapper>();
        private readonly Dictionary<string, EntityStateWrapper> _bucketMap = new Dictionary<string, EntityStateWrapper>();
        private readonly HashSet<string> _visibleEntityIds = new HashSet<string>();

        // Dying / Recalling visual storage for entities that were destroyed in core but performing fade outs
        private readonly List<EntityMarkerView> _fadingEntities = new List<EntityMarkerView>();

        // Event-driven state
        private int _lastProcessedTick = -1;

        private void Awake()
        {
            LoadPrefabs();
            InitializePools();
        }

        private void LoadPrefabs()
        {
            _prefabEntityMarker = Resources.Load<GameObject>(PrefabPathEntityMarker);
            if (_prefabEntityMarker == null)
            {
                Debug.LogWarning($"[StageBattleWorldView] Prefab not found at Resources/{PrefabPathEntityMarker}. Using procedural fallback.");
            }

            _prefabProjectile = Resources.Load<GameObject>(PrefabPathProjectile);
            if (_prefabProjectile == null)
            {
                Debug.LogWarning($"[StageBattleWorldView] Prefab not found at Resources/{PrefabPathProjectile}. Using procedural fallback.");
            }

            _prefabBaseColumn = Resources.Load<GameObject>(PrefabPathBaseColumn);
            if (_prefabBaseColumn == null)
            {
                Debug.LogWarning($"[StageBattleWorldView] Prefab not found at Resources/{PrefabPathBaseColumn}. Using procedural fallback.");
            }

            _prefabFloatingDamageText = Resources.Load<GameObject>(PrefabPathFloatingDamageText);
            if (_prefabFloatingDamageText == null)
            {
                Debug.LogWarning($"[StageBattleWorldView] Prefab not found at Resources/{PrefabPathFloatingDamageText}. Using procedural fallback.");
            }

            _prefabResultBanner = Resources.Load<GameObject>(PrefabPathResultBanner);
            if (_prefabResultBanner == null)
            {
                Debug.LogWarning($"[StageBattleWorldView] Prefab not found at Resources/{PrefabPathResultBanner}. Using procedural fallback.");
            }
        }

        private void InitializePools()
        {
            _entityPool = new GameObjectPool<EntityMarkerView>(
                null,
                transform,
                30,
                () => {
                    if (_prefabEntityMarker != null)
                    {
                        var go = Instantiate(_prefabEntityMarker, transform, false);
                        var comp = go.GetComponent<EntityMarkerView>();
                        if (comp == null) comp = go.AddComponent<EntityMarkerView>();
                        return comp;
                    }
                    return CreateProceduralEntityFallback();
                }
            );

            _projectilePool = new GameObjectPool<ProjectileView>(
                null,
                transform,
                20,
                () => {
                    if (_prefabProjectile != null)
                    {
                        var go = Instantiate(_prefabProjectile, transform, false);
                        var comp = go.GetComponent<ProjectileView>();
                        if (comp == null) comp = go.AddComponent<ProjectileView>();
                        return comp;
                    }
                    return CreateProceduralProjectileFallback();
                }
            );

            _baseColumnPool = new GameObjectPool<BaseColumnView>(
                null,
                transform,
                2,
                () => {
                    if (_prefabBaseColumn != null)
                    {
                        var go = Instantiate(_prefabBaseColumn, transform, false);
                        var comp = go.GetComponent<BaseColumnView>();
                        if (comp == null) comp = go.AddComponent<BaseColumnView>();
                        return comp;
                    }
                    return CreateProceduralBaseFallback();
                }
            );

            _floatingTextPool = new GameObjectPool<FloatingTextView>(
                null,
                transform,
                15,
                () => {
                    if (_prefabFloatingDamageText != null)
                    {
                        var go = Instantiate(_prefabFloatingDamageText, transform, false);
                        var comp = go.GetComponent<FloatingTextView>();
                        if (comp == null) comp = go.AddComponent<FloatingTextView>();
                        return comp;
                    }
                    return CreateProceduralFloatingTextFallback();
                }
            );

            _resultBannerPool = new GameObjectPool<ResultBannerView>(
                null,
                transform,
                1,
                () => {
                    if (_prefabResultBanner != null)
                    {
                        var go = Instantiate(_prefabResultBanner, transform, false);
                        var comp = go.GetComponent<ResultBannerView>();
                        if (comp == null) comp = go.AddComponent<ResultBannerView>();
                        return comp;
                    }
                    return CreateProceduralResultBannerFallback();
                }
            );
        }

        private EntityMarkerView CreateProceduralEntityFallback()
        {
            GameObject go = new GameObject("VisualEntity");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetWhiteSprite();
            var marker = go.AddComponent<EntityMarkerView>();
            return marker;
        }

        private ProjectileView CreateProceduralProjectileFallback()
        {
            GameObject go = new GameObject("VisualProjectile");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetWhiteSprite();
            var proj = go.AddComponent<ProjectileView>();
            return proj;
        }

        private BaseColumnView CreateProceduralBaseFallback()
        {
            GameObject go = new GameObject("BaseColumn");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetWhiteSprite();
            var col = go.AddComponent<BaseColumnView>();
            return col;
        }

        private FloatingTextView CreateProceduralFloatingTextFallback()
        {
            GameObject go = new GameObject("FloatingText");
            go.transform.SetParent(transform, false);
            var tm = go.AddComponent<TextMesh>();
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = 0.08f;
            tm.fontSize = 40;
            tm.fontStyle = FontStyle.Bold;
            var ft = go.AddComponent<FloatingTextView>();
            return ft;
        }

        private ResultBannerView CreateProceduralResultBannerFallback()
        {
            GameObject go = new GameObject("ResultBanner");
            go.transform.SetParent(transform, false);
            var tm = go.AddComponent<TextMesh>();
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = 0.15f;
            tm.fontSize = 90;
            tm.fontStyle = FontStyle.Bold;
            var rb = go.AddComponent<ResultBannerView>();
            return rb;
        }

        /// <summary>
        /// Gets or attaches a <see cref="StageBattleWorldView"/> component to the host.
        /// </summary>
        public static StageBattleWorldView GetOrCreate(GameObject host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var view = host.GetComponent<StageBattleWorldView>();
            if (view == null)
            {
                view = host.AddComponent<StageBattleWorldView>();
            }
            return view;
        }

        private static Sprite GetWhiteSprite()
        {
            if (_whiteSprite == null)
            {
                Texture2D tex = new Texture2D(4, 4);
                Color[] colors = new Color[16];
                for (int i = 0; i < 16; i++) colors[i] = Color.white;
                tex.SetPixels(colors);
                tex.Apply();
                _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            }
            return _whiteSprite;
        }

        // Triangle prism projection coordinates
        private const float LaneLeftX = -5f;
        private const float LaneRightX = 5f;
        private const float LaneTilt = 0f;

        private (Vector2 start, Vector2 end) GetLaneSegment(string laneId)
        {
            float baseY;
            if (laneId == "lane_air") baseY = 1.4f;
            else if (laneId == "lane_ground_2") baseY = -1.6f;
            else baseY = -0.1f;
            return (new Vector2(LaneLeftX, baseY), new Vector2(LaneRightX, baseY + LaneTilt));
        }

        private Vector3 GetWorldPosition(string laneId, long positionMilli, long laneLengthMilli, float z = -0.1f)
        {
            var seg = GetLaneSegment(laneId);
            float t = laneLengthMilli > 0 ? (float)positionMilli / laneLengthMilli : (float)positionMilli / 10000f;
            t = Mathf.Clamp01(t);
            Vector2 p = Vector2.Lerp(seg.start, seg.end, t);
            return new Vector3(p.x, p.y, z);
        }

        private void GetYExtents(IReadOnlyList<LaneDefinition> lanes, out float centerY, out float height)
        {
            float yMin = float.MaxValue, yMax = float.MinValue;
            foreach (var lane in lanes)
            {
                var seg = GetLaneSegment(lane.LaneId);
                yMin = Mathf.Min(yMin, Mathf.Min(seg.start.y, seg.end.y));
                yMax = Mathf.Max(yMax, Mathf.Max(seg.start.y, seg.end.y));
            }
            centerY = (yMin + yMax) * 0.5f;
            height = (yMax - yMin) + 1.2f;
        }

        private long GetLaneLength(BattleConfigSnapshot config, string laneId)
        {
            if (config?.Lanes != null)
            {
                foreach (var lane in config.Lanes)
                {
                    if (lane.LaneId == laneId) return lane.LaneLengthMilli;
                }
            }
            return 10000L;
        }

        private static int GetNumericId(string entityId)
        {
            if (entityId != null && entityId.StartsWith("e_") && int.TryParse(entityId.Substring(2), out int parsedId))
            {
                return parsedId;
            }
            return 0;
        }

        // Deterministic sorting comparators
        private static int CompareSideA(EntityStateWrapper x, EntityStateWrapper y)
        {
            int cmp = y.PositionMilli.CompareTo(x.PositionMilli); // Position descending (closer to enemy base)
            if (cmp != 0) return cmp;
            return x.NumericId.CompareTo(y.NumericId); // Tie-breaker by NumericId ascending
        }

        private static int CompareSideB(EntityStateWrapper x, EntityStateWrapper y)
        {
            int cmp = x.PositionMilli.CompareTo(y.PositionMilli); // Position ascending (closer to player base)
            if (cmp != 0) return cmp;
            return x.NumericId.CompareTo(y.NumericId); // Tie-breaker by NumericId ascending
        }

        /// <summary>
        /// Renders the stage state with pool caching, visibility cap, and distance quantization bucket.
        /// </summary>
        public void Render(
            BattleConfigSnapshot config,
            BattleState state,
            Fp sideAInitialBaseHp,
            Fp sideBInitialBaseHp,
            BattleSide timeOutTieWinnerSide,
            string selectedLaneId = null)
        {
            if (config == null || state == null)
            {
                HideAll();
                return;
            }

            // 1. Camera check
            EnsureCamera(config.Lanes);

            // 2. Base heights
            GetYExtents(config.Lanes, out float centerY, out float baseHeight);

            // 3. Render Bases
            RenderBases(state, sideAInitialBaseHp, sideBInitialBaseHp, centerY, baseHeight);

            // 4. Render Lanes
            RenderLanes(config.Lanes, selectedLaneId);

            // 5. Compute deterministic Visibility Cap + Distance Buckets
            CalculateVisibility(state);

            // 6. Process Tick Events
            if (state.CurrentTick != _lastProcessedTick)
            {
                // Reset safety if time goes backwards
                if (state.CurrentTick < _lastProcessedTick || state.CurrentTick <= 0)
                {
                    ClearAllActiveVisuals();
                }

                _lastProcessedTick = state.CurrentTick;

                if (state.RecentEvents != null)
                {
                    foreach (var evt in state.RecentEvents)
                    {
                        ProcessEvent(evt, config);
                    }
                }
            }

            // 7. Sync active entities with visible ones
            SyncEntities(state, config);

            // 8. Sync projectiles
            SyncProjectiles(state, config);

            // 9. Render result banner
            RenderResultBanner(state, sideAInitialBaseHp, sideBInitialBaseHp, timeOutTieWinnerSide, centerY);
        }

        private void CalculateVisibility(BattleState state)
        {
            _candidatesA.Clear();
            _candidatesB.Clear();
            _bucketMap.Clear();
            _visibleEntityIds.Clear();

            if (state.Lanes == null) return;

            // 5-1. Distance Quantization Bucket
            foreach (var laneState in state.Lanes)
            {
                if (laneState.Entities == null) continue;

                foreach (var ent in laneState.Entities)
                {
                    int numId = GetNumericId(ent.EntityId);
                    long quantPos = ent.PositionMilli / BucketSizeMilli;
                    string bucketKey = $"{laneState.LaneId}_{ent.Side}_{quantPos}";

                    var wrapper = new EntityStateWrapper
                    {
                        EntityId = ent.EntityId,
                        NumericId = numId,
                        LaneId = laneState.LaneId,
                        Side = ent.Side,
                        PositionMilli = ent.PositionMilli
                    };

                    if (_bucketMap.TryGetValue(bucketKey, out var existing))
                    {
                        // Represent the bucket by the minimum NumericId (deterministic)
                        if (wrapper.NumericId < existing.NumericId)
                        {
                            _bucketMap[bucketKey] = wrapper;
                        }
                    }
                    else
                    {
                        _bucketMap[bucketKey] = wrapper;
                    }
                }
            }

            // Group bucket representatives by side
            foreach (var wrapper in _bucketMap.Values)
            {
                if (wrapper.Side == BattleSide.SideA)
                {
                    _candidatesA.Add(wrapper);
                }
                else
                {
                    _candidatesB.Add(wrapper);
                }
            }

            // 5-2. Sort by progress/frontline priority
            _candidatesA.Sort(CompareSideA);
            _candidatesB.Sort(CompareSideB);

            // 5-3. Visibility Cap Security
            int visibleCountA = _candidatesA.Count;
            int visibleCountB = _candidatesB.Count;
            int perSide = MaxVisibleEntities / 2;

            int needA = Math.Min(visibleCountA, perSide);
            int needB = Math.Min(visibleCountB, perSide);
            int leftover = MaxVisibleEntities - needA - needB;

            int extraA = Math.Min(visibleCountA - needA, leftover);
            leftover -= extraA;
            int extraB = Math.Min(visibleCountB - needB, leftover);

            int capA = needA + extraA;
            int capB = needB + extraB;

            // Collect visible IDs
            for (int i = 0; i < capA; i++)
            {
                _visibleEntityIds.Add(_candidatesA[i].EntityId);
            }
            for (int i = 0; i < capB; i++)
            {
                _visibleEntityIds.Add(_candidatesB[i].EntityId);
            }
        }

        private void SyncEntities(BattleState state, BattleConfigSnapshot config)
        {
            // Core entities present in current frame
            HashSet<string> coreEntityIds = new HashSet<string>();

            if (state.Lanes != null)
            {
                foreach (var laneState in state.Lanes)
                {
                    if (laneState.Entities == null) continue;

                    foreach (var ent in laneState.Entities)
                    {
                        coreEntityIds.Add(ent.EntityId);

                        // If not selected for visibility cap, return visual immediately (prevent flicker)
                        if (!_visibleEntityIds.Contains(ent.EntityId))
                        {
                            if (_activeEntities.TryGetValue(ent.EntityId, out var inactiveView))
                            {
                                _entityPool.Return(inactiveView);
                                _activeEntities.Remove(ent.EntityId);
                            }
                            continue;
                        }

                        long len = GetLaneLength(config, laneState.LaneId);
                        Vector3 targetPos = GetWorldPosition(laneState.LaneId, ent.PositionMilli, len);

                        if (!_activeEntities.TryGetValue(ent.EntityId, out var ve))
                        {
                            // Spawn new from pool
                            ve = _entityPool.Get();
                            ve.SetSide(ent.Side);
                            ve.SetPositionImmediate(targetPos);
                            _activeEntities[ent.EntityId] = ve;
                        }

                        // Update current stats
                        ve.gameObject.name = $"VisualEntity_{ent.EntityId}";
                        ve.LaneId = laneState.LaneId;
                        int numericId = GetNumericId(ent.EntityId);
                        ve.SetSortingOrder(100 + numericId);

                        float hpVal = (float)ent.Hp.Raw / Fp.Scale;
                        ve.SetHp(hpVal);

                        // Smooth interpolation move
                        ve.MoveTo(targetPos);
                    }
                }
            }

            // Identify entities that died or were removed in core
            List<string> entitiesToFade = new List<string>();
            foreach (var kvp in _activeEntities)
            {
                if (!coreEntityIds.Contains(kvp.Key))
                {
                    entitiesToFade.Add(kvp.Key);
                }
            }

            foreach (var id in entitiesToFade)
            {
                var ve = _activeEntities[id];
                _activeEntities.Remove(id);

                // Add to active fading queue so it executes its fade timer before returning to pool
                ve.PlayDeathFade();
                _fadingEntities.Add(ve);
            }

            // Maintain fading entities and return them when completed
            for (int i = _fadingEntities.Count - 1; i >= 0; i--)
            {
                var ve = _fadingEntities[i];
                if (ve == null || !ve.gameObject.activeSelf)
                {
                    if (ve != null)
                    {
                        _entityPool.Return(ve);
                    }
                    _fadingEntities.RemoveAt(i);
                }
            }
        }

        private void SyncProjectiles(BattleState state, BattleConfigSnapshot config)
        {
            HashSet<string> coreProjIds = new HashSet<string>();

            if (state.Projectiles != null)
            {
                foreach (var proj in state.Projectiles)
                {
                    coreProjIds.Add(proj.ProjectileId);

                    long len = GetLaneLength(config, proj.ProjectileLaneId);
                    Vector3 targetPos = GetWorldPosition(proj.ProjectileLaneId, proj.PositionMilli, len, -0.2f);

                    if (!_activeProjectiles.TryGetValue(proj.ProjectileId, out var vp))
                    {
                        vp = _projectilePool.Get();
                        vp.SetSide(proj.Side);
                        vp.SetWorldPosition(targetPos);
                        _activeProjectiles[proj.ProjectileId] = vp;
                    }

                    vp.gameObject.name = $"VisualProjectile_{proj.ProjectileId}";
                    int numId = 0;
                    if (proj.ProjectileId != null && proj.ProjectileId.StartsWith("p_") && int.TryParse(proj.ProjectileId.Substring(2), out int parsedId))
                    {
                        numId = parsedId;
                    }
                    vp.SetSortingOrder(200 + numId);
                    vp.SetWorldPosition(targetPos);
                }
            }

            // Remove expired projectiles
            List<string> projsToRemove = new List<string>();
            foreach (var kvp in _activeProjectiles)
            {
                if (!coreProjIds.Contains(kvp.Key))
                {
                    projsToRemove.Add(kvp.Key);
                }
            }

            foreach (var id in projsToRemove)
            {
                var vp = _activeProjectiles[id];
                _activeProjectiles.Remove(id);
                vp.PlayMissFade(); // Fade out and auto-despawn
            }
        }

        private void EnsureCamera(IReadOnlyList<LaneDefinition> lanes)
        {
            if (Camera.main == null && _spawnedCamera == null)
            {
                GameObject camGO = new GameObject("StageWorldCamera");
                camGO.transform.SetParent(transform, false);
                camGO.transform.position = new Vector3(0f, -1.5f, -10f);
                _spawnedCamera = camGO.AddComponent<Camera>();
                _spawnedCamera.orthographic = true;
                _spawnedCamera.orthographicSize = 4.5f;
                _spawnedCamera.clearFlags = CameraClearFlags.SolidColor;
                _spawnedCamera.backgroundColor = new Color(0.08f, 0.08f, 0.12f, 1f);
                camGO.tag = "MainCamera";
                camGO.hideFlags = HideFlags.DontSave;
            }

            if (_spawnedCamera != null)
            {
                GetYExtents(lanes, out float centerY, out float baseHeight);
                _spawnedCamera.transform.position = new Vector3(0f, centerY, -10f);
                _spawnedCamera.orthographicSize = Mathf.Max(4.5f, baseHeight * 1.5f);
            }
        }

        private void RenderBases(BattleState state, Fp sideAInitialBaseHp, Fp sideBInitialBaseHp, float centerY, float baseHeight)
        {
            if (_baseAView == null)
            {
                _baseAView = _baseColumnPool.Get();
                _baseAView.gameObject.name = "StageBaseColumn_SideA";
                _baseAView.gameObject.hideFlags = HideFlags.DontSave;
            }
            _baseAView.SetWorldPosition(new Vector3(-5.0f, centerY, 0f));
            _baseAView.transform.localScale = new Vector3(BaseWidth, baseHeight, 1f);

            if (_baseBView == null)
            {
                _baseBView = _baseColumnPool.Get();
                _baseBView.gameObject.name = "StageBaseColumn_SideB";
                _baseBView.gameObject.hideFlags = HideFlags.DontSave;
            }
            _baseBView.SetWorldPosition(new Vector3(5.0f, centerY, 0f));
            _baseBView.transform.localScale = new Vector3(BaseWidth, baseHeight, 1f);

            BattleSideState sideA = null;
            BattleSideState sideB = null;
            if (state.Sides != null)
            {
                for (int i = 0; i < state.Sides.Count; i++)
                {
                    if (state.Sides[i].Side == BattleSide.SideA) sideA = state.Sides[i];
                    else if (state.Sides[i].Side == BattleSide.SideB) sideB = state.Sides[i];
                }
            }

            Color baseMinColor = new Color(0.05f, 0.05f, 0.05f, 1f);

            if (sideA != null && sideAInitialBaseHp.Raw > 0)
            {
                float ratio = Mathf.Clamp01((float)sideA.BaseHp.Raw / (float)sideAInitialBaseHp.Raw);
                _baseAView.SetHpColor(ratio, new Color(0.1f, 0.5f, 0.9f, 1f), baseMinColor);
            }

            if (sideB != null && sideBInitialBaseHp.Raw > 0)
            {
                float ratio = Mathf.Clamp01((float)sideB.BaseHp.Raw / (float)sideBInitialBaseHp.Raw);
                _baseBView.SetHpColor(ratio, new Color(0.9f, 0.2f, 0.2f, 1f), baseMinColor);
            }
        }

        private void RenderLanes(IReadOnlyList<LaneDefinition> lanes, string selectedLaneId)
        {
            int needed = lanes.Count + 1;
            while (_lineObjects.Count < needed)
            {
                GameObject lineGO = new GameObject($"StageLaneBar_{_lineObjects.Count}");
                lineGO.transform.SetParent(transform, false);
                var sr = lineGO.AddComponent<SpriteRenderer>();
                sr.sprite = GetWhiteSprite();
                sr.color = new Color(0.3f, 0.3f, 0.3f, 0.7f);
                lineGO.hideFlags = HideFlags.DontSave;
                _lineObjects.Add(lineGO);
            }

            for (int i = 0; i < _lineObjects.Count; i++)
            {
                if (i < lanes.Count)
                {
                    var seg = GetLaneSegment(lanes[i].LaneId);
                    bool selected = lanes[i].LaneId == selectedLaneId;
                    Color col = selected ? new Color(1f, 0.85f, 0.2f, 0.95f) : new Color(0.3f, 0.3f, 0.3f, 0.7f);
                    float thick = selected ? LaneBarThick * 2f : LaneBarThick;
                    PlaceSegment(_lineObjects[i], seg.start, seg.end, thick, col);
                }
                else if (i == lanes.Count)
                {
                    GetYExtents(lanes, out float cy, out _);
                    PlaceSegment(_lineObjects[i], new Vector2(LaneLeftX, cy), new Vector2(LaneRightX, cy),
                        0.03f, new Color(0.55f, 0.55f, 0.6f, 0.4f));
                }
                else
                {
                    _lineObjects[i].SetActive(false);
                }
            }
        }

        private void PlaceSegment(GameObject go, Vector2 start, Vector2 end, float thickness, Color color)
        {
            go.SetActive(true);
            Vector2 center = (start + end) * 0.5f;
            Vector2 diff = end - start;
            float length = diff.magnitude;
            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
            go.transform.position = new Vector3(center.x, center.y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.localScale = new Vector3(length, thickness, 1f);
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = color;
        }

        private void ProcessEvent(BattleEvent evt, BattleConfigSnapshot config)
        {
            // Null safety guards throughout to handle invisible entities receiving events
            switch (evt.EventType)
            {
                case BattleEventType.EntitySpawned:
                    {
                        // Spawn events are handled by the next SyncEntities update.
                    }
                    break;

                case BattleEventType.EntityRemoved:
                    {
                        string entityId = evt.EntityId;
                        if (entityId != null && _activeEntities.TryGetValue(entityId, out var ve))
                        {
                            _activeEntities.Remove(entityId);
                            ve.PlayRecallFade();
                            _fadingEntities.Add(ve);
                        }
                    }
                    break;

                case BattleEventType.AttackStarted:
                    {
                        string sourceId = evt.SourceEntityId;
                        if (sourceId != null && _activeEntities.TryGetValue(sourceId, out var ve))
                        {
                            ve.PlayAttackPulse();
                        }
                    }
                    break;

                case BattleEventType.ProjectileSpawned:
                    {
                        // Projectile spawn events are handled by SyncProjectiles updates.
                    }
                    break;

                case BattleEventType.ProjectileHit:
                    {
                        string projId = evt.ProjectileId;
                        if (projId != null && _activeProjectiles.TryGetValue(projId, out var vp))
                        {
                            _activeProjectiles.Remove(projId);
                            vp.PlayHitBurst();
                        }
                    }
                    break;

                case BattleEventType.ProjectileMiss:
                    {
                        string projId = evt.ProjectileId;
                        if (projId != null && _activeProjectiles.TryGetValue(projId, out var vp))
                        {
                            _activeProjectiles.Remove(projId);
                            vp.PlayMissFade();
                        }
                    }
                    break;

                case BattleEventType.DamageApplied:
                    {
                        string targetId = evt.TargetEntityId;
                        double dmgVal = (double)evt.DamageAmount.Raw / Fp.Scale;
                        string text = $"-{Math.Round(dmgVal)}";

                        if (targetId != null)
                        {
                            if (_activeEntities.TryGetValue(targetId, out var ve))
                            {
                                Vector3 targetPos = ve.transform.position;
                                ve.OnHit();

                                // Spawn floating damage text
                                var ft = _floatingTextPool.Get();
                                ft.Show(text, Color.red, targetPos + Vector3.up * 0.4f, (f) => _floatingTextPool.Return(f));
                            }
                            else
                            {
                                // Failsafe if target entity is out of visibility cap
                                // Search core state to get the approximate world position
                            }
                        }
                    }
                    break;

                case BattleEventType.KnockbackApplied:
                    {
                        string targetId = evt.TargetEntityId;
                        if (targetId != null && _activeEntities.TryGetValue(targetId, out var ve))
                        {
                            long len = GetLaneLength(config, ve.LaneId);
                            Vector3 from = GetWorldPosition(ve.LaneId, evt.PreviousPositionMilli, len);
                            Vector3 to = GetWorldPosition(ve.LaneId, evt.PositionMilli, len);
                            ve.KnockbackTo(from, to, 0.1f);
                        }
                    }
                    break;

                case BattleEventType.EntityDied:
                    {
                        string entityId = evt.EntityId;
                        if (entityId != null && _activeEntities.TryGetValue(entityId, out var ve))
                        {
                            _activeEntities.Remove(entityId);
                            ve.PlayDeathFade();
                            _fadingEntities.Add(ve);
                        }
                    }
                    break;

                case BattleEventType.BaseDamaged:
                    {
                        BattleSide side = evt.TargetSide;
                        if (side == BattleSide.SideA && _baseAView != null)
                        {
                            _baseAView.PlayDamageFlash();
                        }
                        else if (side == BattleSide.SideB && _baseBView != null)
                        {
                            _baseBView.PlayDamageFlash();
                        }
                    }
                    break;
            }
        }

        private void RenderResultBanner(BattleState state, Fp sideAInitialBaseHp, Fp sideBInitialBaseHp, BattleSide timeOutTieWinnerSide, float centerY)
        {
            if (!state.IsTerminated)
            {
                if (_resultBannerViewInstance != null)
                {
                    _resultBannerPool.Return(_resultBannerViewInstance);
                    _resultBannerViewInstance = null;
                }
                return;
            }

            if (_resultBannerViewInstance == null)
            {
                _resultBannerViewInstance = _resultBannerPool.Get();
                _resultBannerViewInstance.gameObject.name = "StageResultBanner";
                _resultBannerViewInstance.gameObject.hideFlags = HideFlags.DontSave;
            }

            _resultBannerViewInstance.SetWorldPosition(new Vector3(0f, centerY + 2.5f, -1f));

            BattleSide winner = BattleSide.SideA;
            if (state.EndReason == BattleEndReason.SideBBaseDestroyed)
            {
                winner = BattleSide.SideA;
            }
            else if (state.EndReason == BattleEndReason.SideABaseDestroyed)
            {
                winner = BattleSide.SideB;
            }
            else if (state.EndReason == BattleEndReason.TimeOut)
            {
                BattleSideState sideA = null;
                BattleSideState sideB = null;
                if (state.Sides != null)
                {
                    for (int i = 0; i < state.Sides.Count; i++)
                    {
                        if (state.Sides[i].Side == BattleSide.SideA) sideA = state.Sides[i];
                        else if (state.Sides[i].Side == BattleSide.SideB) sideB = state.Sides[i];
                    }
                }

                float ratioA = (sideA != null && sideAInitialBaseHp.Raw > 0)
                    ? (float)sideA.BaseHp.Raw / (float)sideAInitialBaseHp.Raw : 0f;
                float ratioB = (sideB != null && sideBInitialBaseHp.Raw > 0)
                    ? (float)sideB.BaseHp.Raw / (float)sideBInitialBaseHp.Raw : 0f;

                if (ratioA > ratioB) winner = BattleSide.SideA;
                else if (ratioB > ratioA) winner = BattleSide.SideB;
                else winner = timeOutTieWinnerSide;
            }

            bool isPlayerWinner = winner == BattleSide.SideA;
            string prefix = state.EndReason == BattleEndReason.TimeOut ? "TIMEOUT " : "";
            string text = isPlayerWinner ? prefix + "VICTORY" : prefix + "DEFEAT";
            Color color = isPlayerWinner ? Color.green : Color.red;

            _resultBannerViewInstance.Show(text, color);
        }

        // Pull dynamic references from AppRoot frame-by-frame
        private void LateUpdate()
        {
            var root = AppRoot.Instance;
            var mgr = root != null ? root.StageBattle : null;
            if (mgr != null && mgr.CurrentConfig != null && mgr.LastSession != null)
            {
                string selLane = root.Presenter != null ? root.Presenter.SelectedLaneId : null;
                Render(
                    mgr.CurrentConfig,
                    mgr.LastSession.LastState,
                    mgr.CurrentConfig.SideA.BaseInitialHp,
                    mgr.CurrentConfig.SideB.BaseInitialHp,
                    mgr.CurrentConfig.TimeOutTieWinnerSide,
                    selLane);
            }
            else
            {
                Render(null, null, Fp.Zero, Fp.Zero, BattleSide.SideB, null);
            }
        }

        private void ClearAllActiveVisuals()
        {
            _activeEntities.Clear();
            _activeProjectiles.Clear();
            _fadingEntities.Clear();

            _entityPool.ReturnAll();
            _projectilePool.ReturnAll();
            _floatingTextPool.ReturnAll();
            _baseColumnPool.ReturnAll();
            _resultBannerPool.ReturnAll();

            _baseAView = null;
            _baseBView = null;
            _resultBannerViewInstance = null;
        }

        private void HideAll()
        {
            foreach (var line in _lineObjects)
            {
                if (line != null) line.SetActive(false);
            }

            ClearAllActiveVisuals();
        }

        private void OnDestroy()
        {
            foreach (var line in _lineObjects)
            {
                if (line != null) Destroy(line);
            }
            _lineObjects.Clear();

            if (_entityPool != null) _entityPool.Clear();
            if (_projectilePool != null) _projectilePool.Clear();
            if (_baseColumnPool != null) _baseColumnPool.Clear();
            if (_floatingTextPool != null) _floatingTextPool.Clear();
            if (_resultBannerPool != null) _resultBannerPool.Clear();

            _activeEntities.Clear();
            _activeProjectiles.Clear();
            _fadingEntities.Clear();

            _baseAView = null;
            _baseBView = null;
            _resultBannerViewInstance = null;

            if (_spawnedCamera != null) Destroy(_spawnedCamera.gameObject);
        }
    }
}
