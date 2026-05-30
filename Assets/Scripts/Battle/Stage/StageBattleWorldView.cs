using System;
using System.Collections.Generic;
using UnityEngine;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.State;
using BattleSim.Core.Events;

namespace FrontierBastion.Client.Stage
{
    /// <summary>
    /// Pure runtime-generated world space view of a Stage battle session (v0.5+v0.7 event-driven).
    /// Renders lanes, bases (color-lerped by HP with 피격 flash), visual entities (with damage floating text,
    /// attack scale pulsing, knockback smoothing, died fade, recall recall fade), projectiles, and a victory/defeat banner.
    /// Excludes any dependency on legacy debug runner/world views.
    /// </summary>
    internal sealed class StageBattleWorldView : MonoBehaviour
    {
        private enum EntityVisualState
        {
            Alive,
            Dying,
            Recalling
        }

        private sealed class VisualEntity
        {
            public string EntityId;
            public GameObject GameObject;
            public SpriteRenderer SpriteRenderer;
            public string LaneId;
            public BattleSide Side;
            public EntityVisualState State;
            public float StateTimer;

            // Knockback/movement interpolation
            public Vector3 CurrentPos;
            public Vector3 TargetPos;
            public float InterpolationTimer;
            public float InterpolationDuration;

            // Pulse/Scale timer
            public float PulseTimer;
        }

        private sealed class VisualProjectile
        {
            public string ProjectileId;
            public GameObject GameObject;
            public SpriteRenderer SpriteRenderer;
            public string LaneId;
            public BattleSide Side;

            public bool IsDying;
            public float FadeTimer;
        }

        private sealed class FloatingText
        {
            public GameObject GameObject;
            public TextMesh TextMesh;
            public float Timer;
            public Vector3 StartPos;
        }

        // View configuration constants
        private const float LaneBarThick = 0.07f;
        private const float BaseWidth = 0.35f;
        private const float EntitySize = 0.28f;

        private static Sprite _whiteSprite;

        private readonly List<GameObject> _lineObjects = new List<GameObject>();
        private GameObject _baseAObject;
        private GameObject _baseBObject;
        private GameObject _resultBannerObject;
        private TextMesh _resultBannerText;
        private Camera _spawnedCamera;

        // Visual Registries
        private readonly Dictionary<string, VisualEntity> _activeEntities = new Dictionary<string, VisualEntity>();
        private readonly Dictionary<string, VisualProjectile> _activeProjectiles = new Dictionary<string, VisualProjectile>();

        // Pools (to avoid GC allocations)
        private readonly List<VisualEntity> _entityPool = new List<VisualEntity>();
        private readonly List<VisualProjectile> _projectilePool = new List<VisualProjectile>();
        private readonly List<FloatingText> _floatingTextPool = new List<FloatingText>();

        // Event-driven state
        private int _lastProcessedTick = -1;
        private float _baseAFlashTimer = 0f;
        private float _baseBFlashTimer = 0f;

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

        // Oblique "lying triangular prism" projection.
        // The base-to-base progress axis runs left (player) → right (enemy).
        // The three lane edges are parallel segments, each tilted slightly upward
        // off that axis (oblique feel), stacked top→bottom:
        //   lane_air      = top edge
        //   lane_ground_1 = middle edge (sits on the base-to-base axis)
        //   lane_ground_2 = bottom edge
        private const float LaneLeftX  = -5f;  // player base side
        private const float LaneRightX =  5f;  // enemy base side
        private const float LaneTilt   = 0f; // end.y - start.y (0 = level lanes)

        private (Vector2 start, Vector2 end) GetLaneSegment(string laneId)
        {
            float baseY;
            if (laneId == "lane_air")            baseY = 1.4f;   // top
            else if (laneId == "lane_ground_2")  baseY = -1.6f;  // bottom
            else                                 baseY = -0.1f;  // ground_1 / axis (middle)
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

        private VisualEntity GetPooledEntity(string entityId, string laneId, BattleSide side)
        {
            VisualEntity ve = null;
            foreach (var e in _entityPool)
            {
                if (e != null && e.GameObject != null && !e.GameObject.activeSelf && e.State == EntityVisualState.Alive)
                {
                    ve = e;
                    break;
                }
            }
            if (ve == null)
            {
                GameObject go = new GameObject("VisualEntity");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = GetWhiteSprite();
                ve = new VisualEntity
                {
                    GameObject = go,
                    SpriteRenderer = sr
                };
                _entityPool.Add(ve);
            }

            ve.EntityId = entityId;
            ve.LaneId = laneId;
            ve.Side = side;
            ve.State = EntityVisualState.Alive;
            ve.StateTimer = 0f;
            ve.InterpolationTimer = -1f;
            ve.PulseTimer = 0f;
            ve.GameObject.name = $"VisualEntity_{entityId}";
            ve.GameObject.SetActive(true);

            int numericId = 0;
            if (entityId != null && entityId.StartsWith("e_") && int.TryParse(entityId.Substring(2), out int parsedId))
            {
                numericId = parsedId;
            }
            ve.SpriteRenderer.sortingOrder = 100 + numericId;

            ve.SpriteRenderer.color = side == BattleSide.SideA
                ? new Color(0.1f, 0.8f, 0.9f, 1f)  // Cyan
                : new Color(0.9f, 0.6f, 0.1f, 1f); // Orange

            ve.GameObject.transform.localScale = new Vector3(EntitySize, EntitySize, 1f);

            return ve;
        }

        private VisualProjectile GetPooledProjectile(string projectileId, string laneId, BattleSide side)
        {
            VisualProjectile vp = null;
            foreach (var p in _projectilePool)
            {
                if (p != null && p.GameObject != null && !p.GameObject.activeSelf)
                {
                    vp = p;
                    break;
                }
            }
            if (vp == null)
            {
                GameObject go = new GameObject("VisualProjectile");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = GetWhiteSprite();
                vp = new VisualProjectile
                {
                    GameObject = go,
                    SpriteRenderer = sr
                };
                _projectilePool.Add(vp);
            }

            vp.ProjectileId = projectileId;
            vp.LaneId = laneId;
            vp.Side = side;
            vp.IsDying = false;
            vp.FadeTimer = 0f;
            vp.GameObject.name = $"VisualProjectile_{projectileId}";
            vp.GameObject.SetActive(true);

            int numericId = 0;
            if (projectileId != null && projectileId.StartsWith("p_") && int.TryParse(projectileId.Substring(2), out int parsedId))
            {
                numericId = parsedId;
            }
            vp.SpriteRenderer.sortingOrder = 200 + numericId;

            vp.SpriteRenderer.color = side == BattleSide.SideA
                ? new Color(0.4f, 0.95f, 1.0f, 1f)  // Light Cyan
                : new Color(1.0f, 0.75f, 0.3f, 1f); // Light Orange

            vp.GameObject.transform.localScale = new Vector3(0.18f, 0.18f, 1f);

            return vp;
        }

        private void GetPooledFloatingText(string text, Vector3 position)
        {
            FloatingText ft = null;
            foreach (var t in _floatingTextPool)
            {
                if (t != null && t.GameObject != null && !t.GameObject.activeSelf)
                {
                    ft = t;
                    break;
                }
            }
            if (ft == null)
            {
                GameObject go = new GameObject("FloatingText");
                go.transform.SetParent(transform, false);
                var tm = go.AddComponent<TextMesh>();
                tm.alignment = TextAlignment.Center;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.characterSize = 0.08f;
                tm.fontSize = 40;
                tm.fontStyle = FontStyle.Bold;
                ft = new FloatingText
                {
                    GameObject = go,
                    TextMesh = tm
                };
                _floatingTextPool.Add(ft);
            }

            ft.TextMesh.text = text;
            ft.TextMesh.color = Color.red;
            ft.StartPos = position;
            ft.Timer = 0f;
            ft.GameObject.transform.position = position;
            ft.GameObject.SetActive(true);
        }

        /// <summary>
        /// Renders the stage state to the world view using event-driven visuals.
        /// If config or state is null, all view components are hidden.
        /// </summary>
        public void Render(
            BattleConfigSnapshot  config,
            BattleState           state,
            Fp                    sideAInitialBaseHp,
            Fp                    sideBInitialBaseHp,
            BattleSide            timeOutTieWinnerSide)
        {
            if (config == null || state == null)
            {
                HideAll();
                return;
            }

            // 1. Camera check
            EnsureCamera(config.Lanes);

            // 2. Compute dynamic Y metrics for the base columns
            GetYExtents(config.Lanes, out float centerY, out float baseHeight);

            // 3. Render base pillars
            RenderBases(state, sideAInitialBaseHp, sideBInitialBaseHp, centerY, baseHeight);

            // 4. Render lane bars
            RenderLanes(config.Lanes);

            // 5. Process new events from the core state
            if (state.CurrentTick != _lastProcessedTick)
            {
                // Reset safety: if state goes back in time (e.g. restart Sandbox)
                if (state.CurrentTick < _lastProcessedTick || state.CurrentTick <= 0)
                {
                    _activeEntities.Clear();
                    _activeProjectiles.Clear();
                    foreach (var ve in _entityPool) if (ve?.GameObject != null) ve.GameObject.SetActive(false);
                    foreach (var vp in _projectilePool) if (vp?.GameObject != null) vp.GameObject.SetActive(false);
                    foreach (var ft in _floatingTextPool) if (ft?.GameObject != null) ft.GameObject.SetActive(false);
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

            // 6. Safety Sync visual entities with state core entities
            HashSet<string> coreEntityIds = new HashSet<string>();
            if (state.Lanes != null)
            {
                foreach (var laneState in state.Lanes)
                {
                    if (laneState.Entities != null)
                    {
                        foreach (var ent in laneState.Entities)
                        {
                            coreEntityIds.Add(ent.EntityId);

                            if (!_activeEntities.TryGetValue(ent.EntityId, out var ve) || ve.State != EntityVisualState.Alive)
                            {
                                ve = GetPooledEntity(ent.EntityId, laneState.LaneId, ent.Side);
                                _activeEntities[ent.EntityId] = ve;

                                long len = GetLaneLength(config, laneState.LaneId);
                                ve.GameObject.transform.position = GetWorldPosition(laneState.LaneId, ent.PositionMilli, len);
                                ve.CurrentPos = ve.GameObject.transform.position;
                                ve.TargetPos = ve.CurrentPos;
                                ve.InterpolationTimer = -1f;
                            }

                            if (ve.InterpolationTimer < 0f)
                            {
                                long len = GetLaneLength(config, laneState.LaneId);
                                Vector3 targetPos = GetWorldPosition(laneState.LaneId, ent.PositionMilli, len);
                                ve.GameObject.transform.position = targetPos;
                                ve.CurrentPos = targetPos;
                                ve.TargetPos = targetPos;
                            }
                        }
                    }
                }
            }

            List<string> entitiesToForceRemove = new List<string>();
            foreach (var kvp in _activeEntities)
            {
                if (kvp.Value.State == EntityVisualState.Alive && !coreEntityIds.Contains(kvp.Key))
                {
                    entitiesToForceRemove.Add(kvp.Key);
                }
            }
            foreach (var id in entitiesToForceRemove)
            {
                var ve = _activeEntities[id];
                ve.State = EntityVisualState.Dying;
                ve.StateTimer = 0.3f;
            }

            // 7. Safety Sync projectiles
            HashSet<string> coreProjIds = new HashSet<string>();
            if (state.Projectiles != null)
            {
                foreach (var proj in state.Projectiles)
                {
                    coreProjIds.Add(proj.ProjectileId);

                    if (!_activeProjectiles.TryGetValue(proj.ProjectileId, out var vp) || vp.IsDying)
                    {
                        vp = GetPooledProjectile(proj.ProjectileId, proj.ProjectileLaneId, proj.Side);
                        _activeProjectiles[proj.ProjectileId] = vp;
                    }

                    long len = GetLaneLength(config, proj.ProjectileLaneId);
                    Vector3 targetPos = GetWorldPosition(proj.ProjectileLaneId, proj.PositionMilli, len, -0.2f);
                    vp.GameObject.transform.position = targetPos;
                }
            }

            List<string> projsToForceRemove = new List<string>();
            foreach (var kvp in _activeProjectiles)
            {
                if (!kvp.Value.IsDying && !coreProjIds.Contains(kvp.Key))
                {
                    projsToForceRemove.Add(kvp.Key);
                }
            }
            foreach (var id in projsToForceRemove)
            {
                var vp = _activeProjectiles[id];
                vp.IsDying = true;
                vp.FadeTimer = 0.1f;
            }

            // 8. Render termination result banner
            RenderResultBanner(state, sideAInitialBaseHp, sideBInitialBaseHp, timeOutTieWinnerSide, centerY);
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
            if (_baseAObject == null)
            {
                _baseAObject = new GameObject("StageBaseColumn_SideA");
                _baseAObject.transform.SetParent(transform, false);
                var sr = _baseAObject.AddComponent<SpriteRenderer>();
                sr.sprite = GetWhiteSprite();
                sr.color = new Color(0.1f, 0.5f, 0.9f, 1f);
                _baseAObject.hideFlags = HideFlags.DontSave;
            }
            _baseAObject.SetActive(true);
            _baseAObject.transform.position = new Vector3(-5.0f, centerY, 0f);
            _baseAObject.transform.localScale = new Vector3(BaseWidth, baseHeight, 1f);

            if (_baseBObject == null)
            {
                _baseBObject = new GameObject("StageBaseColumn_SideB");
                _baseBObject.transform.SetParent(transform, false);
                var sr = _baseBObject.AddComponent<SpriteRenderer>();
                sr.sprite = GetWhiteSprite();
                sr.color = new Color(0.9f, 0.2f, 0.2f, 1f);
                _baseBObject.hideFlags = HideFlags.DontSave;
            }
            _baseBObject.SetActive(true);
            _baseBObject.transform.position = new Vector3(5.0f, centerY, 0f);
            _baseBObject.transform.localScale = new Vector3(BaseWidth, baseHeight, 1f);

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
                Color baseColor = Color.Lerp(baseMinColor, new Color(0.1f, 0.5f, 0.9f, 1f), ratio);
                if (_baseAFlashTimer > 0f) baseColor += new Color(0.3f, 0.3f, 0.3f, 0f);
                _baseAObject.GetComponent<SpriteRenderer>().color = baseColor;
            }

            if (sideB != null && sideBInitialBaseHp.Raw > 0)
            {
                float ratio = Mathf.Clamp01((float)sideB.BaseHp.Raw / (float)sideBInitialBaseHp.Raw);
                Color baseColor = Color.Lerp(baseMinColor, new Color(0.9f, 0.2f, 0.2f, 1f), ratio);
                if (_baseBFlashTimer > 0f) baseColor += new Color(0.3f, 0.3f, 0.3f, 0f);
                _baseBObject.GetComponent<SpriteRenderer>().color = baseColor;
            }
        }

        private void RenderLanes(IReadOnlyList<LaneDefinition> lanes)
        {
            // One object per lane, plus one faint base-to-base axis guide line.
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
                    PlaceSegment(_lineObjects[i], seg.start, seg.end, LaneBarThick, new Color(0.3f, 0.3f, 0.3f, 0.7f));
                }
                else if (i == lanes.Count)
                {
                    // Base-to-base axis guide: horizontal line through the vertical center.
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
            switch (evt.EventType)
            {
                case BattleEventType.EntitySpawned:
                    {
                        string entityId = evt.EntityId;
                        string laneId = evt.LaneId;
                        BattleSide side = evt.SourceSide;

                        if (_activeEntities.TryGetValue(entityId, out var existing))
                        {
                            existing.State = EntityVisualState.Alive;
                            existing.StateTimer = 0f;
                            existing.GameObject.SetActive(true);
                        }
                        else
                        {
                            var ve = GetPooledEntity(entityId, laneId, side);
                            _activeEntities[entityId] = ve;

                            long len = GetLaneLength(config, laneId);
                            ve.GameObject.transform.position = GetWorldPosition(laneId, evt.PositionMilli, len);
                            ve.CurrentPos = ve.GameObject.transform.position;
                            ve.TargetPos = ve.CurrentPos;
                            ve.InterpolationTimer = -1f;
                        }
                    }
                    break;

                case BattleEventType.EntityRemoved:
                    {
                        string entityId = evt.EntityId;
                        if (entityId != null && _activeEntities.TryGetValue(entityId, out var ve))
                        {
                            ve.State = EntityVisualState.Recalling;
                            ve.StateTimer = 0.2f;
                        }
                    }
                    break;

                case BattleEventType.AttackStarted:
                    {
                        string sourceId = evt.SourceEntityId;
                        if (sourceId != null && _activeEntities.TryGetValue(sourceId, out var ve))
                        {
                            ve.PulseTimer = 0.1f;
                        }
                    }
                    break;

                case BattleEventType.ProjectileSpawned:
                    {
                        string projId = evt.ProjectileId;
                        string laneId = evt.LaneId;
                        BattleSide side = evt.SourceSide;

                        if (_activeProjectiles.TryGetValue(projId, out var existing))
                        {
                            existing.IsDying = false;
                            existing.FadeTimer = 0f;
                            existing.GameObject.SetActive(true);
                        }
                        else
                        {
                            var vp = GetPooledProjectile(projId, laneId, side);
                            _activeProjectiles[projId] = vp;

                            long len = GetLaneLength(config, laneId);
                            vp.GameObject.transform.position = GetWorldPosition(laneId, evt.PositionMilli, len, -0.2f);
                        }
                    }
                    break;

                case BattleEventType.ProjectileHit:
                    {
                        string projId = evt.ProjectileId;
                        if (projId != null && _activeProjectiles.TryGetValue(projId, out var vp))
                        {
                            vp.IsDying = true;
                            vp.FadeTimer = 0.1f;
                        }
                    }
                    break;

                case BattleEventType.ProjectileMiss:
                    {
                        string projId = evt.ProjectileId;
                        if (projId != null && _activeProjectiles.TryGetValue(projId, out var vp))
                        {
                            vp.IsDying = true;
                            vp.FadeTimer = 0.1f;
                        }
                    }
                    break;

                case BattleEventType.DamageApplied:
                    {
                        string targetId = evt.TargetEntityId;
                        if (targetId != null && _activeEntities.TryGetValue(targetId, out var ve))
                        {
                            Vector3 targetPos = ve.GameObject.transform.position;
                            double dmgVal = (double)evt.DamageAmount.Raw / Fp.Scale;
                            GetPooledFloatingText($"-{Math.Round(dmgVal)}", targetPos + Vector3.up * 0.4f);
                        }
                    }
                    break;

                case BattleEventType.KnockbackApplied:
                    {
                        string targetId = evt.TargetEntityId;
                        if (targetId != null && _activeEntities.TryGetValue(targetId, out var ve))
                        {
                            long len = GetLaneLength(config, ve.LaneId);
                            ve.CurrentPos = GetWorldPosition(ve.LaneId, evt.PreviousPositionMilli, len);
                            ve.TargetPos = GetWorldPosition(ve.LaneId, evt.PositionMilli, len);
                            ve.InterpolationTimer = 0f;
                            ve.InterpolationDuration = 0.1f;
                        }
                    }
                    break;

                case BattleEventType.EntityDied:
                    {
                        string entityId = evt.EntityId;
                        if (entityId != null && _activeEntities.TryGetValue(entityId, out var ve))
                        {
                            ve.State = EntityVisualState.Dying;
                            ve.StateTimer = 0.3f;
                        }
                    }
                    break;

                case BattleEventType.BaseDamaged:
                    {
                        BattleSide side = evt.TargetSide;
                        if (side == BattleSide.SideA)
                        {
                            _baseAFlashTimer = 0.15f;
                        }
                        else if (side == BattleSide.SideB)
                        {
                            _baseBFlashTimer = 0.15f;
                        }
                    }
                    break;
            }
        }

        private void RenderResultBanner(BattleState state, Fp sideAInitialBaseHp, Fp sideBInitialBaseHp, BattleSide timeOutTieWinnerSide, float centerY)
        {
            if (!state.IsTerminated)
            {
                if (_resultBannerObject != null)
                {
                    _resultBannerObject.SetActive(false);
                }
                return;
            }

            if (_resultBannerObject == null)
            {
                _resultBannerObject = new GameObject("StageResultBanner");
                _resultBannerObject.transform.SetParent(transform, false);
                _resultBannerText = _resultBannerObject.AddComponent<TextMesh>();
                _resultBannerText.alignment = TextAlignment.Center;
                _resultBannerText.anchor = TextAnchor.MiddleCenter;
                _resultBannerText.characterSize = 0.15f;
                _resultBannerText.fontSize = 90;
                _resultBannerText.fontStyle = FontStyle.Bold;
                _resultBannerObject.hideFlags = HideFlags.DontSave;
            }

            _resultBannerObject.SetActive(true);
            _resultBannerObject.transform.position = new Vector3(0f, centerY + 2.5f, -1f);

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
            _resultBannerText.text = isPlayerWinner ? prefix + "VICTORY" : prefix + "DEFEAT";
            _resultBannerText.color = isPlayerWinner ? Color.green : Color.red;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_baseAFlashTimer > 0f) _baseAFlashTimer -= dt;
            if (_baseBFlashTimer > 0f) _baseBFlashTimer -= dt;

            // 1. Update entities
            List<string> entitiesToRemove = new List<string>();
            foreach (var kvp in _activeEntities)
            {
                var ve = kvp.Value;

                if (ve.PulseTimer > 0f)
                {
                    ve.PulseTimer -= dt;
                    float scaleFactor = ve.PulseTimer > 0f ? 1.4f : 1f;
                    ve.GameObject.transform.localScale = new Vector3(EntitySize * scaleFactor, EntitySize * scaleFactor, 1f);
                }

                if (ve.InterpolationTimer >= 0f)
                {
                    ve.InterpolationTimer += dt;
                    float t = Mathf.Clamp01(ve.InterpolationTimer / ve.InterpolationDuration);
                    Vector3 pos = Vector3.Lerp(ve.CurrentPos, ve.TargetPos, t);
                    ve.GameObject.transform.position = pos;
                    if (t >= 1f)
                    {
                        ve.InterpolationTimer = -1f;
                        ve.CurrentPos = ve.TargetPos;
                    }
                }

                if (ve.State == EntityVisualState.Dying)
                {
                    ve.StateTimer -= dt;
                    float progress = Mathf.Clamp01(ve.StateTimer / 0.3f);

                    Color baseCol = ve.Side == BattleSide.SideA ? new Color(0.1f, 0.8f, 0.9f, 1f) : new Color(0.9f, 0.6f, 0.1f, 1f);
                    Color grayCol = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                    Color finalCol = Color.Lerp(new Color(0.3f, 0.3f, 0.3f, 0f), Color.Lerp(grayCol, baseCol, progress), progress);
                    ve.SpriteRenderer.color = finalCol;

                    if (ve.StateTimer <= 0f)
                    {
                        ve.GameObject.SetActive(false);
                        entitiesToRemove.Add(kvp.Key);
                    }
                }
                else if (ve.State == EntityVisualState.Recalling)
                {
                    ve.StateTimer -= dt;
                    float progress = Mathf.Clamp01(ve.StateTimer / 0.2f);

                    Color baseCol = ve.Side == BattleSide.SideA ? new Color(0.1f, 0.8f, 0.9f, 1f) : new Color(0.9f, 0.6f, 0.1f, 1f);
                    Color finalCol = new Color(baseCol.r, baseCol.g, baseCol.b, progress);
                    ve.SpriteRenderer.color = finalCol;

                    if (ve.StateTimer <= 0f)
                    {
                        ve.GameObject.SetActive(false);
                        entitiesToRemove.Add(kvp.Key);
                    }
                }
            }

            foreach (var id in entitiesToRemove)
            {
                _activeEntities.Remove(id);
            }

            // 2. Update projectiles
            List<string> projsToRemove = new List<string>();
            foreach (var kvp in _activeProjectiles)
            {
                var vp = kvp.Value;
                if (vp.IsDying)
                {
                    vp.FadeTimer -= dt;
                    float progress = Mathf.Clamp01(vp.FadeTimer / 0.1f);
                    Color baseCol = vp.Side == BattleSide.SideA ? new Color(0.4f, 0.95f, 1.0f, 1f) : new Color(1.0f, 0.75f, 0.3f, 1f);
                    vp.SpriteRenderer.color = new Color(baseCol.r, baseCol.g, baseCol.b, progress);

                    vp.GameObject.transform.localScale = new Vector3(0.18f * (1f + (1f - progress) * 0.5f), 0.18f * (1f + (1f - progress) * 0.5f), 1f);

                    if (vp.FadeTimer <= 0f)
                    {
                        vp.GameObject.SetActive(false);
                        projsToRemove.Add(kvp.Key);
                    }
                }
            }
            foreach (var id in projsToRemove)
            {
                _activeProjectiles.Remove(id);
            }

            // 3. Update floating texts
            foreach (var ft in _floatingTextPool)
            {
                if (ft != null && ft.GameObject != null && ft.GameObject.activeSelf)
                {
                    ft.Timer += dt;
                    float progress = Mathf.Clamp01(ft.Timer / 0.6f);
                    ft.GameObject.transform.position = ft.StartPos + Vector3.up * (0.4f + progress * 0.8f);
                    ft.TextMesh.color = new Color(1f, 0f, 0f, 1f - progress);

                    if (ft.Timer >= 0.6f)
                    {
                        ft.GameObject.SetActive(false);
                    }
                }
            }
        }

        private void HideAll()
        {
            foreach (var line in _lineObjects)
            {
                if (line != null) line.SetActive(false);
            }
            if (_baseAObject != null) _baseAObject.SetActive(false);
            if (_baseBObject != null) _baseBObject.SetActive(false);
            if (_resultBannerObject != null) _resultBannerObject.SetActive(false);

            foreach (var ve in _entityPool)
            {
                if (ve != null && ve.GameObject != null) ve.GameObject.SetActive(false);
            }
            _activeEntities.Clear();

            foreach (var vp in _projectilePool)
            {
                if (vp != null && vp.GameObject != null) vp.GameObject.SetActive(false);
            }
            _activeProjectiles.Clear();

            foreach (var ft in _floatingTextPool)
            {
                if (ft != null && ft.GameObject != null) ft.GameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            foreach (var line in _lineObjects)
            {
                if (line != null) Destroy(line);
            }
            _lineObjects.Clear();

            if (_baseAObject != null) Destroy(_baseAObject);
            if (_baseBObject != null) Destroy(_baseBObject);
            if (_resultBannerObject != null) Destroy(_resultBannerObject);

            foreach (var ve in _entityPool)
            {
                if (ve != null && ve.GameObject != null) Destroy(ve.GameObject);
            }
            _entityPool.Clear();
            _activeEntities.Clear();

            foreach (var vp in _projectilePool)
            {
                if (vp != null && vp.GameObject != null) Destroy(vp.GameObject);
            }
            _projectilePool.Clear();
            _activeProjectiles.Clear();

            foreach (var ft in _floatingTextPool)
            {
                if (ft != null && ft.GameObject != null) Destroy(ft.GameObject);
            }
            _floatingTextPool.Clear();

            if (_spawnedCamera != null) Destroy(_spawnedCamera.gameObject);
        }
    }
}
