using RimWorld.Planet;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>Drives occasional abstract harassment events while the colony is travelling by caravan (there is
    /// no map to animate on, so these resolve as moodlets + a letter). Auto-discovered by RimWorld.</summary>
    public class WorldComponent_CaravanHarassment : WorldComponent
    {
        private int nextTick = -1;

        public WorldComponent_CaravanHarassment(World world) : base(world) { }

        public override void WorldComponentTick()
        {
            var s = RimJobWorldSexualHarassmentMod.Settings;
            if (s == null || !s.masterEnabled) return;
            int now = Find.TickManager.TicksGame;
            if (nextTick < 0) { nextTick = now + Rand.Range(20000, 60000); return; }
            if (now < nextTick) return;
            nextTick = now + Rand.Range(30000, 90000);   // roughly every 0.5-1.5 days of travel
            try { HarassmentEngine.TryCaravanHarassment(); } catch { }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextTick, "rjwshCaravanTick", -1);
        }
    }
}
