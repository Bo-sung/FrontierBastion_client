using System.Collections.Generic;
using System.Text;
using BattleSim.Core.Config;
using BattleSim.Core.FixedPoint;
using BattleSim.Core.Results;
using BattleSim.Core.State;

namespace FrontierBastion.Client.DebugBattle
{
    /// <summary>
    /// Read-only data bundle passed from <see cref="DebugBattleRunner"/> to
    /// <see cref="DebugBattleStatusTextBuilder.Build"/>. Contains only values
    /// needed for display — no simulator references, no write access.
    /// </summary>
    internal struct DebugBattleStatusContext
    {
        public DebugBattleScenario Scenario;
        public BattleState         LastState;
        public BattleResult        Result;             // null = not yet terminated
        public bool                IsPaused;
        public int                 SelectedSlotIndex;
        public int                 SelectedSlotCursor;
        public string              SelectedLaneId;
        public int                 SelectedLaneCursor;
        public bool                SideBAutoEnabled;
        public bool                SideBAutoAvailable; // false before first scenario load
        public IEnumerable<string> EventLines;
    }

    /// <summary>
    /// Builds the IMGUI status text string for the Debug Battle panel.
    /// Pure display logic — reads <see cref="DebugBattleStatusContext"/>, never mutates anything.
    ///
    /// Several helpers are <c>internal</c> so <see cref="DebugBattleRunner"/> can reuse them
    /// in event-log messages without duplicating formatting code:
    ///   • <see cref="FormatResult"/>
    ///   • <see cref="FpDisplay"/>
    ///   • <see cref="FpInt"/>
    ///   • <see cref="GetSlotRoleName"/>
    /// </summary>
    internal static class DebugBattleStatusTextBuilder
    {
        // Reused each frame on the Unity main thread — never accessed concurrently.
        private static readonly StringBuilder _sb = new StringBuilder(4096);

        // ── Public entry point ────────────────────────────────────────────────

        /// <summary>
        /// Builds and returns the full IMGUI status string from <paramref name="ctx"/>.
        /// Safe to call every frame from <c>OnGUI</c>.
        /// </summary>
        public static string Build(DebugBattleStatusContext ctx)
        {
            _sb.Length = 0;

            // ── Header ────────────────────────────────────────────────────────
            _sb.AppendLine("── Frontier Bastion  Debug Battle ─────────────────");
            _sb.AppendLine("Scenario : " + (ctx.Scenario != null ? ctx.Scenario.DisplayName : "<none>"));

            if (ctx.LastState != null)
            {
                string tickLine = "Tick     : " + ctx.LastState.CurrentTick
                    + "   " + (ctx.IsPaused ? "[PAUSED]" : "[running]");
                if (ctx.LastState.IsTerminated)
                    tickLine += "   TERMINATED / " + ctx.LastState.EndReason;
                _sb.AppendLine(tickLine);
            }
            else
            {
                _sb.AppendLine("           " + (ctx.IsPaused ? "[PAUSED]" : "[running]"));
            }
            _sb.AppendLine();

            // ── Controls ──────────────────────────────────────────────────────
            _sb.AppendLine("── Controls ────────────────────────────────────────");
            _sb.AppendLine(" F1 SideA Victory  F2 SideB Victory  F3 Interactive  F4 Timeout");
            _sb.AppendLine(" Backspace : reset (paused)");
            _sb.AppendLine(" Space : pause/resume   T : manual step");
            _sb.AppendLine(" Q / E : prev / next slot   Z / X : prev / next lane");
            _sb.AppendLine(" 1 SpawnDrone   2 DeployPilot   R Recall  [selected slot+lane]");
            _sb.AppendLine(" A : SideB Auto ON/OFF  [F3 default: ON]");
            _sb.AppendLine();

            if (ctx.LastState != null)
            {
                BattleSideState  sideAState = GetSideState(ctx.LastState, BattleSide.SideA);
                BattleSideState  sideBState = GetSideState(ctx.LastState, BattleSide.SideB);
                SlotDefinition[] sideADefs  = ctx.Scenario?.InitialState?.SideA?.Slots;
                SlotDefinition[] sideBDefs  = ctx.Scenario?.InitialState?.SideB?.Slots;

                // ── SideA ──────────────────────────────────────────────────────
                _sb.Append("── SideA (player)");
                if (sideAState != null)
                {
                    Fp maxE = ctx.Scenario?.Config?.SideA?.MaxEnergy ?? Fp.Zero;
                    _sb.Append("  E:" + FpDisplay(sideAState.Energy) + "/" + FpInt(maxE));
                    _sb.Append("  BaseHP:" + FpDisplay(sideAState.BaseHp));
                }
                _sb.AppendLine(" ─────────────────────");
                _sb.AppendLine("    [#] Role       Cost  Cooldown  Pilot");

                if (sideAState != null)
                {
                    for (int i = 0; i < sideAState.Slots.Count; i++)
                    {
                        SlotState      ss  = sideAState.Slots[i];
                        SlotDefinition def = FindSlotDef(sideADefs, ss.SlotIndex);
                        AppendSlotLine(_sb, ss, def, selected: ss.SlotIndex == ctx.SelectedSlotIndex);
                    }
                }
                _sb.AppendLine();

                // ── Lanes ──────────────────────────────────────────────────────
                int laneCount = ctx.Scenario?.Config?.Lanes?.Length ?? 0;
                int slotCount = ctx.Scenario?.InitialState?.SideA?.Slots?.Length ?? 0;
                _sb.AppendLine(
                    "── Lanes [Z/X]  (" + (ctx.SelectedLaneCursor + 1) + "/" + laneCount + ")"
                    + "   Slots [Q/E]  (" + (ctx.SelectedSlotCursor + 1) + "/" + slotCount + ") ──");

                for (int i = 0; i < ctx.LastState.Lanes.Count; i++)
                {
                    LaneState lane    = ctx.LastState.Lanes[i];
                    bool      laneSel = lane.LaneId == ctx.SelectedLaneId;
                    _sb.Append(laneSel ? " >> " : "    ");
                    _sb.Append(lane.LaneId);
                    if (laneSel) _sb.Append(" [SEL]");
                    _sb.AppendLine("  entities=" + lane.Entities.Count);

                    for (int j = 0; j < lane.Entities.Count; j++)
                    {
                        BattleEntity e = lane.Entities[j];
                        _sb.AppendLine("      " + e.EntityId
                            + "  " + (e.Side == BattleSide.SideA ? "A" : "B")
                            + "  hp=" + FpInt(e.Hp)
                            + "  pos=" + e.PositionMilli);
                    }
                }
                _sb.AppendLine();

                // ── SideB ──────────────────────────────────────────────────────
                string autoTag = ctx.SideBAutoAvailable
                    ? (ctx.SideBAutoEnabled ? "Auto:ON" : "Auto:OFF")
                    : "Auto:n/a";
                _sb.Append("── SideB (opponent)  " + autoTag);
                if (sideBState != null)
                {
                    Fp maxE = ctx.Scenario?.Config?.SideB?.MaxEnergy ?? Fp.Zero;
                    _sb.Append("  E:" + FpDisplay(sideBState.Energy) + "/" + FpInt(maxE));
                    _sb.Append("  BaseHP:" + FpDisplay(sideBState.BaseHp));
                }
                _sb.AppendLine(" ──────────────────");
                _sb.AppendLine("    [#] Role       Cost  Cooldown  Pilot");

                if (sideBState != null)
                {
                    for (int i = 0; i < sideBState.Slots.Count; i++)
                    {
                        SlotState      ss  = sideBState.Slots[i];
                        SlotDefinition def = FindSlotDef(sideBDefs, ss.SlotIndex);
                        AppendSlotLine(_sb, ss, def, selected: false);
                    }
                }
            }

            // ── Result ────────────────────────────────────────────────────────
            if (ctx.Result != null)
            {
                _sb.AppendLine();
                _sb.AppendLine("── Result ──────────────────────────────────────────");
                _sb.AppendLine("  " + FormatResult(ctx.Result));
            }

            // ── Events ────────────────────────────────────────────────────────
            _sb.AppendLine();
            _sb.AppendLine("── Events ──────────────────────────────────────────");
            if (ctx.EventLines != null)
                foreach (string line in ctx.EventLines)
                    _sb.AppendLine("  " + line);

            return _sb.ToString();
        }

        // ── Shared helpers (internal — Runner uses these for event-log messages) ──

        /// <summary>
        /// Formats a <see cref="BattleResult"/> to a concise display string.
        /// Used in both the status panel and Runner event-log messages.
        /// </summary>
        internal static string FormatResult(BattleResult result)
        {
            return "winner=" + result.WinnerSide
                + " / " + result.EndReason
                + "  tick=" + result.ClearTimeTick
                + "  A=" + FpDisplay(result.SideABaseHpRatio)
                + "  B=" + FpDisplay(result.SideBBaseHpRatio);
        }

        /// <summary>
        /// Returns a Fp value as "INT.D" (1 decimal digit).
        /// E.g. 28.5 → "28.5", 300 → "300.0", ≤ 0 → "0".
        /// Display-only — uses <c>Fp.Raw</c> and <c>Fp.Scale</c> directly.
        /// </summary>
        internal static string FpDisplay(Fp fp)
        {
            if (fp.Raw <= 0L) return "0";
            long intPart  = fp.Raw / Fp.Scale;
            long fracPart = (fp.Raw % Fp.Scale) / (Fp.Scale / 10); // 1 digit
            return intPart + "." + fracPart;
        }

        /// <summary>Returns the integer (floor) part of a Fp value. Display-only.</summary>
        internal static int FpInt(Fp fp)
        {
            if (fp.Raw <= 0L) return 0;
            return (int)(fp.Raw / Fp.Scale);
        }

        /// <summary>
        /// Derives a human-readable role name from a pilot id string.
        /// Takes the last underscore-delimited token and capitalises it.
        /// E.g. "pilot_a_tank" → "Tank", "pilot_b_bruiser" → "Bruiser",
        ///      "pilot_high" → "High", "placeholder" → "Placeholder".
        /// Display-only.
        /// </summary>
        internal static string GetSlotRoleName(string pilotId)
        {
            if (string.IsNullOrEmpty(pilotId)) return "Unknown";
            int lastUnderscore = pilotId.LastIndexOf('_');
            string token = (lastUnderscore >= 0 && lastUnderscore < pilotId.Length - 1)
                ? pilotId.Substring(lastUnderscore + 1)
                : pilotId;
            if (token.Length == 0) return pilotId;
            return char.ToUpper(token[0]) + (token.Length > 1 ? token.Substring(1) : string.Empty);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Appends a single slot status line.
        /// Format (selected):   " >> [0] Tank        E:15  cd:  0  pilot:ON"
        /// Format (normal):     "    [1] Striker      E:25  cd: 18"
        /// </summary>
        private static void AppendSlotLine(
            StringBuilder  sb,
            SlotState      ss,
            SlotDefinition def,
            bool           selected)
        {
            sb.Append(selected ? " >> " : "    ");
            sb.Append("[" + ss.SlotIndex + "] ");

            string name = def != null ? GetSlotRoleName(def.PilotId) : ("Slot" + ss.SlotIndex);
            sb.Append(name.PadRight(10));

            if (def != null)
            {
                sb.Append("  E:");
                sb.Append(FpInt(def.EnergyCost).ToString().PadLeft(2));
            }
            else
            {
                sb.Append("  E: -");
            }

            sb.Append("  cd:");
            sb.Append(ss.DroneCooldownTick.ToString().PadLeft(3));

            if (ss.IsPilotDeployed)
                sb.Append("  pilot:ON");
            else if (ss.IsPilotKnockedOut)
                sb.Append("  pilot:KO");
            else if (ss.PilotCooldownTick > 0)
                sb.Append("  pcd:" + ss.PilotCooldownTick.ToString().PadLeft(3));

            sb.AppendLine();
        }

        private static SlotDefinition FindSlotDef(SlotDefinition[] defs, int slotIndex)
        {
            if (defs == null) return null;
            for (int i = 0; i < defs.Length; i++)
                if (defs[i].SlotIndex == slotIndex) return defs[i];
            return null;
        }

        private static BattleSideState GetSideState(BattleState state, BattleSide side)
        {
            if (state?.Sides == null) return null;
            for (int i = 0; i < state.Sides.Count; i++)
                if (state.Sides[i].Side == side) return state.Sides[i];
            return null;
        }
    }
}
