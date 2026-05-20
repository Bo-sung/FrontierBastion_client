using System.Collections.Generic;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.State;
using UnityEngine;

namespace FrontierBastion.Client.DebugBattle
{
    /// <summary>
    /// Draws a 2D battlefield overview panel in the Game View using Unity IMGUI.
    /// Call <see cref="Draw"/> once per <c>OnGUI</c> from <see cref="DebugBattleRunner"/>.
    /// Pure display helper — no MonoBehaviour, no scene objects, no side-effects.
    ///
    /// Phase 1 — lane lines, base pillars, base HP bars.
    /// Phase 2 — entity markers on each lane, position-mapped from PositionMilli,
    ///            player side = cyan, enemy side = orange, HP integer shown above each marker.
    ///
    /// Layout (screen-space, absolute coordinates):
    ///   x=584  right of the 560-wide status panel
    ///   Title row / Lane area (N × LaneSpacing tall) / HP area
    ///   Player base = blue pillar on left edge of lane area.
    ///   Enemy  base = red  pillar on right edge of lane area.
    ///   Lane lines connect the two pillars; selected lane is yellow.
    ///   Entity markers sit on each lane line, mapped via PositionMilli / LaneLengthMilli.
    /// </summary>
    internal static class DebugBattleStageView
    {
        // ── Layout constants ─────────────────────────────────────────────────
        private const float PanelX      = 584f;   // starts right of the 560-wide status panel
        private const float PanelY      = 12f;
        private const float PanelWidth  = 480f;
        private const float PadH        = 14f;    // horizontal inner padding
        private const float TitleRowH   = 28f;
        private const float LaneSpacing = 60f;    // vertical distance between lane centres
        private const float LaneThick   = 8f;     // lane-line height in px
        private const float BaseW       = 42f;    // width of each base pillar
        private const float HpAreaH     = 38f;    // height reserved below lanes for HP bars
        private const float HpBarH      = 8f;

        // ── Entity marker dimensions (Phase 2) ───────────────────────────────
        private const float MarkerW     = 12f;
        private const float MarkerH     = 12f;
        /// <summary>
        /// Vertical distance (px) between stacked entity markers on the same lane.
        /// Entities are stacked downward from the lane centre so they don't overlap
        /// with the lane label drawn above the line.
        /// </summary>
        private const float MarkerStackStep = 14f;

        // ── 1×1 colour textures (lazy-init, recreated on domain reload) ──────
        // Phase 1
        private static Texture2D _tDark;
        private static Texture2D _tBlue;
        private static Texture2D _tRed;
        private static Texture2D _tYellow;
        private static Texture2D _tGray;
        // Phase 2 — entity marker colours
        private static Texture2D _tCyan;    // player-side entities
        private static Texture2D _tOrange;  // enemy-side entities

        private static Texture2D Tex(ref Texture2D field, Color c)
        {
            if (field != null) return field;
            field = new Texture2D(1, 1);
            field.SetPixel(0, 0, c);
            field.Apply();
            return field;
        }

        // Phase 1 colours
        private static Texture2D TDark   => Tex(ref _tDark,   new Color(0.05f, 0.05f, 0.10f, 0.88f));
        private static Texture2D TBlue   => Tex(ref _tBlue,   new Color(0.25f, 0.50f, 1.00f, 0.90f));
        private static Texture2D TRed    => Tex(ref _tRed,    new Color(1.00f, 0.30f, 0.30f, 0.90f));
        private static Texture2D TYellow => Tex(ref _tYellow, new Color(1.00f, 0.85f, 0.00f, 1.00f));
        private static Texture2D TGray   => Tex(ref _tGray,   new Color(0.45f, 0.45f, 0.45f, 0.75f));
        // Phase 2 colours
        private static Texture2D TCyan   => Tex(ref _tCyan,   new Color(0.00f, 0.90f, 0.90f, 1.00f));
        private static Texture2D TOrange => Tex(ref _tOrange, new Color(1.00f, 0.55f, 0.10f, 1.00f));

        // ── Entry point ──────────────────────────────────────────────────────

        /// <summary>
        /// Renders the battlefield panel. Safe to call every frame from OnGUI.
        /// Must be called after any GUILayout.EndArea so it draws at absolute screen coords.
        /// </summary>
        /// <param name="scenario">Current scenario (provides Config.Lanes and base HP caps).</param>
        /// <param name="lastState">Latest state snapshot from BattleSimulator.GetState().</param>
        /// <param name="selectedLaneId">LaneId currently selected by Z/X input.</param>
        public static void Draw(
            DebugBattleScenario scenario,
            BattleState         lastState,
            string              selectedLaneId)
        {
            if (scenario == null) return;

            LaneDefinition[] lanes = scenario.Config.Lanes;
            int laneCount = lanes != null ? lanes.Length : 0;
            if (laneCount == 0) return;

            // Skip gracefully if the Game View is too narrow to fit the panel.
            if (Screen.width - PanelX < 180f) return;

            float panelW    = Mathf.Min(PanelWidth, Screen.width - PanelX - 4f);
            float laneAreaH = laneCount * LaneSpacing;
            float panelH    = TitleRowH + laneAreaH + HpAreaH;

            Color origColor = GUI.color;

            // ── Background ───────────────────────────────────────────────────
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(PanelX, PanelY, panelW, panelH), TDark);

            // ── Title ────────────────────────────────────────────────────────
            GUI.Label(new Rect(PanelX + PadH, PanelY + 6f, panelW - PadH * 2f, 20f),
                "Battlefield  [" + scenario.DisplayName + "]");

            float laneTop = PanelY + TitleRowH;
            float innerL  = PanelX + PadH;
            float innerR  = PanelX + panelW - PadH;

            // ── Player base pillar (left) ─────────────────────────────────────
            float playerPillarX = innerL;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(playerPillarX, laneTop, BaseW, laneAreaH), TBlue);

            // ── Enemy base pillar (right) ─────────────────────────────────────
            float enemyPillarX = innerR - BaseW;
            GUI.DrawTexture(new Rect(enemyPillarX, laneTop, BaseW, laneAreaH), TRed);

            // ── Lane lines + entity markers ───────────────────────────────────
            float lineX0 = playerPillarX + BaseW + 4f;
            float lineX1 = enemyPillarX  - 4f;
            float lineW  = Mathf.Max(0f, lineX1 - lineX0);

            for (int i = 0; i < laneCount; i++)
            {
                LaneDefinition def      = lanes[i];
                bool           selected = def.LaneId == selectedLaneId;
                float          centreY  = laneTop + i * LaneSpacing + LaneSpacing * 0.5f;
                float          lineY    = centreY - LaneThick * 0.5f;

                // Lane line
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(lineX0, lineY, lineW, LaneThick),
                    selected ? TYellow : TGray);

                // Lane label above the line  ("> lane_id  [LaneType]" when selected)
                GUI.color = selected ? Color.yellow : new Color(0.75f, 0.75f, 0.75f);
                string prefix = selected ? "> " : "  ";
                GUI.Label(new Rect(lineX0 + 4f, centreY - 20f, 200f, 18f),
                    prefix + def.LaneId + "  [" + def.LaneType + "]");

                // ── Phase 2: entity markers ───────────────────────────────────
                if (lastState != null && i < lastState.Lanes.Count)
                {
                    DrawLaneEntities(
                        lastState.Lanes[i].Entities,
                        def.LaneLengthMilli,
                        lineX0, lineX1, centreY);
                }
            }

            // ── HP area (below lane area) ─────────────────────────────────────
            float hpTop = laneTop + laneAreaH;

            // "Player" / "Enemy" labels
            GUI.color = new Color(0.60f, 0.75f, 1.00f);
            GUI.Label(new Rect(playerPillarX, hpTop + 2f, 60f, 14f), "Player");

            GUI.color = new Color(1.00f, 0.55f, 0.55f);
            GUI.Label(new Rect(enemyPillarX,  hpTop + 2f, 60f, 14f), "Enemy");

            // HP bars
            if (lastState != null)
            {
                float barY = hpTop + 18f;
                float barW = BaseW + 20f;

                float pRatio = HpRatio(lastState.PlayerBaseHp, scenario.Config.PlayerBaseInitialHp);
                DrawHpBar(new Rect(playerPillarX, barY, barW, HpBarH), pRatio, TBlue, TGray);

                float eRatio = HpRatio(lastState.EnemyBaseHp, scenario.Config.EnemyBaseInitialHp);
                DrawHpBar(new Rect(enemyPillarX - 20f, barY, barW, HpBarH), eRatio, TRed, TGray);
            }

            GUI.color = origColor;
        }

        // ── Phase 2: Entity marker rendering ────────────────────────────────

        /// <summary>
        /// Draws entity markers for one lane.
        ///
        /// Colour:
        ///   Player-side (drone or pilot) → cyan.
        ///   Enemy-side                   → orange.
        ///   (The public BattleEntity API does not distinguish drone from pilot;
        ///    SlotIndex lives only in the private RuntimeEntity inside BattleSimulator.)
        ///
        /// Position mapping:
        ///   t = Clamp01(entity.PositionMilli / laneLength)
        ///   screenX = Lerp(lineX0, lineX1, t)
        ///   t=0 → player base edge, t=1 → enemy base edge.
        ///
        /// Vertical stacking:
        ///   Index j (0-based) within the lane's entity list.
        ///   Stacked downward from the lane centre in steps of MarkerStackStep,
        ///   so they don't overlap the lane label drawn above the line.
        ///   j=0 → centred on lane line, j=1 → 14 px below, j=2 → 28 px below, …
        ///
        /// HP display:
        ///   Integer part of Fp HP shown as a label above each marker.
        ///   Computed as entity.Hp.Raw / Fp.Scale to avoid needing a cast operator.
        /// </summary>
        private static void DrawLaneEntities(
            IReadOnlyList<BattleEntity> entities,
            long  laneLength,
            float lineX0,
            float lineX1,
            float centreY)
        {
            if (entities == null || entities.Count == 0) return;

            for (int j = 0; j < entities.Count; j++)
            {
                BattleEntity entity = entities[j];

                // ── Position mapping ──────────────────────────────────────────
                float t = laneLength > 0L
                    ? Mathf.Clamp01((float)entity.PositionMilli / (float)laneLength)
                    : 0f;
                float cx = Mathf.Lerp(lineX0, lineX1, t);

                // ── Vertical stagger (downward from lane centre) ──────────────
                // j=0: centred on lane line; j=1,2,…: step below.
                // Keeps markers away from the lane label drawn above the line.
                float my = centreY - MarkerH * 0.5f + j * MarkerStackStep;
                float mx = cx - MarkerW * 0.5f;

                // ── Coloured square marker ────────────────────────────────────
                Texture2D markerTex = entity.OwnerSide == OwnerSide.Player ? TCyan : TOrange;
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(mx, my, MarkerW, MarkerH), markerTex);

                // ── HP integer label above each marker ────────────────────────
                // hp.Raw / Fp.Scale gives the integer part (Fp.Scale == 10000).
                long   hpInt = entity.Hp.Raw >= 0L ? entity.Hp.Raw / Fp.Scale : 0L;
                string hpStr = hpInt.ToString();
                GUI.color = entity.OwnerSide == OwnerSide.Player
                    ? new Color(0.70f, 1.00f, 1.00f)   // light cyan text
                    : new Color(1.00f, 0.85f, 0.60f);  // light orange text
                GUI.Label(new Rect(cx - 13f, my - 13f, 26f, 13f), hpStr);
            }

            GUI.color = Color.white;
        }

        // ── Phase 1 helpers (unchanged) ──────────────────────────────────────

        /// <summary>
        /// Returns [0..1] HP ratio using raw Fp values so no cast operator is required.
        /// Both current and max share the same scale, so the scale cancels in the division.
        /// </summary>
        private static float HpRatio(Fp current, Fp max)
        {
            if (max.Raw <= 0L) return 0f;
            return Mathf.Clamp01((float)current.Raw / (float)max.Raw);
        }

        private static void DrawHpBar(Rect outer, float ratio, Texture2D fillTex, Texture2D bgTex)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(outer, bgTex);
            if (ratio > 0f)
            {
                GUI.DrawTexture(
                    new Rect(outer.x, outer.y, outer.width * ratio, outer.height),
                    fillTex);
            }
        }
    }
}
