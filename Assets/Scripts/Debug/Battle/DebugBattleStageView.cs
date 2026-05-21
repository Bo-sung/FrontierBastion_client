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
    /// Phase 2r — improved marker readability: type label (P#/E#) inside marker,
    ///             separate player/enemy counters, legend row at panel bottom.
    ///
    /// Layout (screen-space, absolute coordinates):
    ///   x=584  right of the 560-wide status panel
    ///   Title row / Lane area (N × LaneSpacing tall) / HP area / Legend row
    ///   Player base = blue pillar on left edge of lane area.
    ///   Enemy  base = red  pillar on right edge of lane area.
    ///   Lane lines connect the two pillars; selected lane is yellow.
    ///   Entity markers sit on each lane line, mapped via PositionMilli / LaneLengthMilli.
    ///   Each marker shows P# (player) or E# (enemy) where # is display order in the lane.
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

        // ── Entity marker dimensions (Phase 2 / 2r) ─────────────────────────
        /// <summary>Marker width — widened to 18 px so "P1"/"E2" label fits inside.</summary>
        private const float MarkerW     = 18f;
        /// <summary>Marker height — 16 px for readable two-character label.</summary>
        private const float MarkerH     = 16f;
        /// <summary>
        /// Vertical distance (px) between stacked entity markers on the same lane.
        /// Step matches MarkerH + 2 so successive markers don't overlap.
        /// Entities are stacked downward from the lane centre so they don't overlap
        /// with the lane label drawn above the line.
        /// </summary>
        private const float MarkerStackStep = 18f;
        /// <summary>Height of the legend row at the bottom of the panel.</summary>
        private const float LegendH     = 16f;

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
            float panelH    = TitleRowH + laneAreaH + HpAreaH + LegendH;

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

                float pRatio = HpRatio(GetSideBaseHp(lastState, BattleSide.SideA), scenario.Config.SideA.BaseInitialHp);
                DrawHpBar(new Rect(playerPillarX, barY, barW, HpBarH), pRatio, TBlue, TGray);

                float eRatio = HpRatio(GetSideBaseHp(lastState, BattleSide.SideB), scenario.Config.SideB.BaseInitialHp);
                DrawHpBar(new Rect(enemyPillarX - 20f, barY, barW, HpBarH), eRatio, TRed, TGray);
            }

            // ── Legend row (below HP area) ────────────────────────────────────
            // Explains the P#/E# marker convention without claiming gameplay meaning.
            float legendY = hpTop + HpAreaH;
            GUI.color = new Color(0.45f, 0.45f, 0.45f);
            GUI.Label(new Rect(PanelX + PadH, legendY + 1f, panelW - PadH * 2f, LegendH - 2f),
                "P=player  E=enemy  # = display order in lane");

            GUI.color = origColor;
        }

        // ── Phase 2 / 2r: Entity marker rendering ───────────────────────────

        /// <summary>
        /// Draws entity markers for one lane (Phase 2r — improved readability).
        ///
        /// Label convention:
        ///   Player-side entities → "P1", "P2", … (cyan background, dark label text).
        ///   Enemy-side  entities → "E1", "E2", … (orange background, dark label text).
        ///   Player and enemy counters are independent; # is display order only — it
        ///   carries no gameplay meaning (drone vs. pilot cannot be distinguished from
        ///   the public BattleEntity API; SlotIndex is private to BattleSimulator).
        ///
        /// Position mapping:
        ///   t = Clamp01(entity.PositionMilli / laneLength)
        ///   screenX = Lerp(lineX0, lineX1, t)
        ///   t=0 → player base edge (spawn point), t=1 → enemy base edge.
        ///
        /// Vertical stacking:
        ///   j (0-based, overall entity index in lane list) drives the downward offset.
        ///   j=0 sits on the lane centre line; j=1,2,… step down by MarkerStackStep (18 px).
        ///   Stacking is downward so markers stay clear of the lane label above the line.
        ///
        /// HP display:
        ///   Integer HP shown above each marker in matching tinted text.
        ///   Computed as entity.Hp.Raw / Fp.Scale (no cast operator required).
        /// </summary>
        private static void DrawLaneEntities(
            IReadOnlyList<BattleEntity> entities,
            long  laneLength,
            float lineX0,
            float lineX1,
            float centreY)
        {
            if (entities == null || entities.Count == 0) return;

            // Separate counters so P# and E# are each numbered from 1.
            int playerCount = 0;
            int enemyCount  = 0;

            for (int j = 0; j < entities.Count; j++)
            {
                BattleEntity entity = entities[j];

                // ── Position → screen X ───────────────────────────────────────
                float t  = laneLength > 0L
                    ? Mathf.Clamp01((float)entity.PositionMilli / (float)laneLength)
                    : 0f;
                float cx = Mathf.Lerp(lineX0, lineX1, t);

                // ── Vertical stagger (downward from lane centre) ──────────────
                // j=0 sits on the lane line; j=1,2,… step down by MarkerStackStep.
                float my = centreY - MarkerH * 0.5f + j * MarkerStackStep;
                float mx = cx - MarkerW * 0.5f;

                // ── Type label: P# for SideA (local), E# for SideB (opponent) ──
                bool   isPlayer  = entity.Side == BattleSide.SideA;
                int    typeIndex = isPlayer ? ++playerCount : ++enemyCount;
                string typeLabel = (isPlayer ? "P" : "E") + typeIndex;

                // ── Coloured background (18 × 16 px) ─────────────────────────
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(mx, my, MarkerW, MarkerH),
                    isPlayer ? TCyan : TOrange);

                // ── Label inside marker — near-black for contrast on bright bg ─
                GUI.color = new Color(0.05f, 0.05f, 0.05f);
                GUI.Label(new Rect(mx, my, MarkerW, MarkerH), typeLabel);

                // ── HP integer above the marker (colour-tinted, separate row) ──
                // Drawn above the marker so it never overlaps the type label.
                long   hpInt = entity.Hp.Raw >= 0L ? entity.Hp.Raw / Fp.Scale : 0L;
                string hpStr = hpInt.ToString();
                GUI.color = isPlayer
                    ? new Color(0.70f, 1.00f, 1.00f)   // light cyan
                    : new Color(1.00f, 0.85f, 0.60f);  // light orange
                GUI.Label(new Rect(cx - 15f, my - 14f, 30f, 13f), hpStr);
            }

            GUI.color = Color.white;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the BaseHp for <paramref name="side"/> from <paramref name="state"/>.
        /// Returns Fp.Zero if the side is not found.
        /// </summary>
        private static Fp GetSideBaseHp(BattleState state, BattleSide side)
        {
            if (state?.Sides == null) return Fp.Zero;
            for (int i = 0; i < state.Sides.Count; i++)
                if (state.Sides[i].Side == side) return state.Sides[i].BaseHp;
            return Fp.Zero;
        }

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
