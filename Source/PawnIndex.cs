using System;
using System.Collections.Generic;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Fast, self-healing id -> Pawn resolution routed through each map's per-tick index
    /// (MapComponent_HarassmentScan). Replaces the old linear AllPawnsSpawned scans that turned the per-tick
    /// control upkeep into O(n^2) (a full spawned-pawn scan per profiled pet, every 60 ticks). The index is
    /// rebuilt at most once per game tick, so lookups are O(1) during play; a paused UI frame safely reuses the
    /// last build. All entry points are try/catch guarded and degrade to null (fail-safe rule).
    /// </summary>
    public static class PawnLookup
    {
        /// <summary>Resolve a spawned pawn by thingIDNumber on a specific map (null if not spawned there).</summary>
        public static Pawn OnMap(Map map, int id)
        {
            if (map == null || id < 0) return null;
            try { return MapComponent_HarassmentScan.For(map)?.PawnById(id); }
            catch { return null; }
        }

        /// <summary>Resolve a spawned pawn by thingIDNumber across every loaded map.</summary>
        public static Pawn AnyMap(int id)
        {
            if (id < 0) return null;
            var maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                var p = OnMap(maps[i], id);
                if (p != null) return p;
            }
            return null;
        }
    }

    /// <summary>
    /// Tick-scoped memo for the hot per-pawn apparel/bed predicates (control collar, slavery collar, onahole
    /// binding) that the upkeep loops otherwise recompute several times per pawn per tick. The store is cleared
    /// whenever the game tick advances, so a value can never be more than one tick stale, and the handful of
    /// collar equip/remove sites call Invalidate for exactness within a tick. Keys are pawn ids (ints, never Pawn
    /// references) so the static store cannot root a dead Game graph. Fully self-healing: a missed Invalidate can
    /// only ever yield a one-pass-stale boolean, never a crash (fail-safe rule).
    /// </summary>
    internal static class PawnFlagCache
    {
        internal const int WearControlCollar = 0;
        internal const int SlaveryCollar = 1;
        internal const int InOnahole = 2;

        private static int _stamp = -1;
        // Per pawn: bits 0..2 = "computed" for that flag, bits 16..18 = the cached boolean value.
        private static readonly Dictionary<int, int> _state = new Dictionary<int, int>(64);

        public static bool Get(Pawn p, int flag, Func<Pawn, bool> compute)
        {
            if (p == null) return false;
            try
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                if (now != _stamp) { _stamp = now; _state.Clear(); }

                int id = p.thingIDNumber;
                _state.TryGetValue(id, out int st);
                int computedBit = 1 << flag;
                if ((st & computedBit) != 0)
                    return (st & (1 << (flag + 16))) != 0;

                bool v = compute(p);
                st |= computedBit;
                if (v) st |= (1 << (flag + 16));
                _state[id] = st;
                return v;
            }
            catch { return compute(p); }
        }

        /// <summary>Drop a pawn's cached flags after a collar equip/remove so the next read recomputes.</summary>
        public static void Invalidate(Pawn p)
        {
            if (p == null) return;
            try { _state.Remove(p.thingIDNumber); } catch { }
        }
    }
}
