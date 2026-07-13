using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    public class HediffCompProperties_OnaholeBound : HediffCompProperties
    {
        public HediffCompProperties_OnaholeBound() { compClass = typeof(HediffComp_OnaholeBound); }
    }

    /// <summary>
    /// The "trapped in an onahole" status. Tracks how long the pawn has been bound (shown in the health-tab
    /// tooltip as a timer) and self-removes once they are cut down / freed (no longer in an onahole bed).
    /// </summary>
    public class HediffComp_OnaholeBound : HediffComp
    {
        private int ticksBound;

        public override void CompPostTick(ref float severityAdjustment)
        {
            ticksBound++;
            // When the onahole is deconstructed/unbound the pawn leaves the bed; drop severity so the hediff
            // auto-removes (severity <= 0). Done via severityAdjustment to avoid removing a hediff mid-tick.
            if (Pawn != null && Pawn.IsHashIntervalTick(200) && !HarassmentEngine.IsInOnaholeBed(Pawn))
                severityAdjustment = -10f;
        }

        public override string CompTipStringExtra
        {
            get
            {
                string s = "Bound for " + ticksBound.ToStringTicksToPeriod();
                var vp = GameComponent_Harassment.Instance?.GetProfileIfExists(Pawn);
                if (vp != null && vp.onaholeReleaseTick > 0)
                {
                    int now = Find.TickManager.TicksGame;
                    s += now >= vp.onaholeReleaseTick
                        ? "\nBegging to be cut down"
                        : "\nWill start begging in " + (vp.onaholeReleaseTick - now).ToStringTicksToPeriod();
                }
                return s;
            }
        }

        public override void CompExposeData()
        {
            Scribe_Values.Look(ref ticksBound, "ticksBound", 0);
        }
    }
}
