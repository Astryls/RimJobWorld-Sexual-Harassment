using Verse;

namespace RJWSexualHarassment
{
    public class HediffCompProperties_ShockLust : HediffCompProperties
    {
        public float severityPerDay = -0.2f; // how fast the craving fades between shocks
        public HediffCompProperties_ShockLust() { compClass = typeof(HediffComp_ShockLust); }
    }

    /// <summary>Masochist shock-lust: the accumulated craving decays slowly between shocks. Each shock pumps
    /// severity back up (HarassmentEngine.OnShockApplied); when it decays to nothing the hediff removes itself.</summary>
    public class HediffComp_ShockLust : HediffComp
    {
        private int tick;
        public HediffCompProperties_ShockLust Props => (HediffCompProperties_ShockLust)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (++tick < 500) return; // ~once every ~8s of game time
            tick = 0;
            float perStep = Props.severityPerDay / (60000f / 500f); // fraction of a day per step
            severityAdjustment += perStep;
            if (parent.Severity + perStep <= 0.001f)
                parent.pawn?.health?.RemoveHediff(parent);
        }
    }
}
