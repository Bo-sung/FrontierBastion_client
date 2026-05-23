using System;
using System.Collections.Generic;
using UnityEngine;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.State;

namespace FrontierBastion.Client.Stage
{
    /// <summary>
    /// Pure runtime-generated world space view of a Stage battle session (v0.4 native).
    /// Renders lanes, bases (color-lerped by HP), entity markers, and a victory/defeat banner.
    /// Excludes any dependency on legacy debug runner/world views.
    /// </summary>
    internal sealed class StageBattleWorldView : MonoBehaviour
    {
        // View configuration constants
        private const float LaneHalfWidth = 5f;
        private const float LaneBarThick = 0.07f;
        private const float BaseWidth = 0.35f;
        private const float EntitySize = 0.28f;

        private static Sprite _whiteSprite;

        private readonly List<GameObject> _lineObjects = new List<GameObject>();
        private GameObject _baseAObject;
        private GameObject _baseBObject;
        private GameObject _resultBannerObject;
        private TextMesh _resultBannerText;
        private readonly List<SpriteRenderer> _entityPool = new List<SpriteRenderer>();
        private Camera _spawnedCamera;

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

        /// <summary>
        /// Renders the stage state to the world view.
        /// If lanes or state is null, all view components are hidden.
        /// </summary>
        public void Render(
            IReadOnlyList<LaneDefinition> lanes,
            BattleState                   state,
            Fp                            sideAInitialBaseHp,
            Fp                            sideBInitialBaseHp,
            BattleSide                    timeOutTieWinnerSide)
        {
            if (lanes == null || state == null)
            {
                HideAll();
                return;
            }

            // 1. Camera check
            EnsureCamera(lanes);

            // 2. Compute dynamic Y metrics for the base columns
            float yMin = float.MaxValue;
            float yMax = float.MinValue;
            foreach (var lane in lanes)
            {
                float ly = (-lane.LaneWorldYMilli) / 1000f;
                yMin = Mathf.Min(yMin, ly);
                yMax = Mathf.Max(yMax, ly);
            }
            float centerY = (yMin + yMax) / 2.0f;
            float baseHeight = (yMax - yMin) + 1.2f;

            // 3. Render base pillars
            RenderBases(state, sideAInitialBaseHp, sideBInitialBaseHp, centerY, baseHeight);

            // 4. Render lane bars
            RenderLanes(lanes);

            // 5. Render entity markers
            RenderEntities(lanes, state);

            // 6. Render termination result banner
            RenderResultBanner(state, sideAInitialBaseHp, sideBInitialBaseHp, timeOutTieWinnerSide, centerY);
        }

        private void EnsureCamera(IReadOnlyList<LaneDefinition> lanes)
        {
            if (Camera.main == null && _spawnedCamera == null)
            {
                GameObject camGO = new GameObject("StageWorldCamera");
                camGO.transform.SetParent(transform, false);
                camGO.transform.position = new Vector3(0f, -0.75f, -10f);
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
                float yMin = float.MaxValue;
                float yMax = float.MinValue;
                foreach (var lane in lanes)
                {
                    float ly = (-lane.LaneWorldYMilli) / 1000f;
                    yMin = Mathf.Min(yMin, ly);
                    yMax = Mathf.Max(yMax, ly);
                }
                float centerY = (yMin + yMax) / 2.0f;
                float baseHeight = (yMax - yMin) + 1.2f;

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
            _baseAObject.transform.position = new Vector3(-LaneHalfWidth, centerY, 0f);
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
            _baseBObject.transform.position = new Vector3(LaneHalfWidth, centerY, 0f);
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
                _baseAObject.GetComponent<SpriteRenderer>().color = Color.Lerp(baseMinColor, new Color(0.1f, 0.5f, 0.9f, 1f), ratio);
            }

            if (sideB != null && sideBInitialBaseHp.Raw > 0)
            {
                float ratio = Mathf.Clamp01((float)sideB.BaseHp.Raw / (float)sideBInitialBaseHp.Raw);
                _baseBObject.GetComponent<SpriteRenderer>().color = Color.Lerp(baseMinColor, new Color(0.9f, 0.2f, 0.2f, 1f), ratio);
            }
        }

        private void RenderLanes(IReadOnlyList<LaneDefinition> lanes)
        {
            while (_lineObjects.Count < lanes.Count)
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
                    var lane = lanes[i];
                    float ly = (-lane.LaneWorldYMilli) / 1000f;
                    _lineObjects[i].SetActive(true);
                    _lineObjects[i].transform.position = new Vector3(0f, ly, 0f);
                    _lineObjects[i].transform.localScale = new Vector3(LaneHalfWidth * 2f, LaneBarThick, 1f);
                }
                else
                {
                    _lineObjects[i].SetActive(false);
                }
            }
        }

        private void RenderEntities(IReadOnlyList<LaneDefinition> lanes, BattleState state)
        {
            int index = 0;
            if (state.Lanes != null)
            {
                foreach (var laneState in state.Lanes)
                {
                    LaneDefinition laneDef = null;
                    foreach (var l in lanes)
                    {
                        if (l.LaneId == laneState.LaneId)
                        {
                            laneDef = l;
                            break;
                        }
                    }
                    if (laneDef == null) continue;

                    float ly = (-laneDef.LaneWorldYMilli) / 1000f;
                    long lenMilli = laneDef.LaneLengthMilli > 0 ? laneDef.LaneLengthMilli : 10000L;

                    if (laneState.Entities != null)
                    {
                        foreach (var ent in laneState.Entities)
                        {
                            if (index >= _entityPool.Count)
                            {
                                GameObject entGO = new GameObject($"StageEntityMarker_{_entityPool.Count}");
                                entGO.transform.SetParent(transform, false);
                                var sr = entGO.AddComponent<SpriteRenderer>();
                                sr.sprite = GetWhiteSprite();
                                entGO.hideFlags = HideFlags.DontSave;
                                _entityPool.Add(sr);
                            }

                            var marker = _entityPool[index];
                            marker.gameObject.SetActive(true);

                            float normX = (float)ent.PositionMilli / lenMilli;
                            float worldX = -LaneHalfWidth + normX * (LaneHalfWidth * 2f);

                            // Parse NumericId from EntityId (e.g. "e_12" -> 12)
                            int numericId = 0;
                            if (ent.EntityId != null && ent.EntityId.StartsWith("e_") && int.TryParse(ent.EntityId.Substring(2), out int parsedId))
                            {
                                numericId = parsedId;
                            }

                            marker.sortingOrder = numericId;
                            marker.transform.position = new Vector3(worldX, ly, -0.1f);
                            marker.transform.localScale = new Vector3(EntitySize, EntitySize, 1f);

                            marker.color = ent.Side == BattleSide.SideA
                                ? new Color(0.1f, 0.8f, 0.9f, 1f)  // Cyan
                                : new Color(0.9f, 0.6f, 0.1f, 1f); // Orange

                            index++;
                        }
                    }
                }
            }

            for (int i = index; i < _entityPool.Count; i++)
            {
                _entityPool[i].gameObject.SetActive(false);
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

            // Determine winner
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

        private void HideAll()
        {
            foreach (var line in _lineObjects)
            {
                if (line != null) line.SetActive(false);
            }
            if (_baseAObject != null) _baseAObject.SetActive(false);
            if (_baseBObject != null) _baseBObject.SetActive(false);
            if (_resultBannerObject != null) _resultBannerObject.SetActive(false);
            foreach (var ent in _entityPool)
            {
                if (ent != null) ent.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            // Memory cleanup
            foreach (var line in _lineObjects)
            {
                if (line != null) Destroy(line);
            }
            _lineObjects.Clear();

            if (_baseAObject != null) Destroy(_baseAObject);
            if (_baseBObject != null) Destroy(_baseBObject);
            if (_resultBannerObject != null) Destroy(_resultBannerObject);

            foreach (var ent in _entityPool)
            {
                if (ent != null && ent.gameObject != null) Destroy(ent.gameObject);
            }
            _entityPool.Clear();

            if (_spawnedCamera != null) Destroy(_spawnedCamera.gameObject);
        }
    }
}
