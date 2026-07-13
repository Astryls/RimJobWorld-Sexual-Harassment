using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    internal static class MasoStage
    {
        public static ThoughtState At(Pawn p)
        {
            bool maso = false;
            try { maso = rjw.xxx.is_masochist(p); } catch { }
            return ThoughtState.ActiveAtStage(maso ? 1 : 0);
        }
    }

    /// <summary>Situational mood while an owner forces the pet to stay naked (forceNudity). Negative normally,
    /// positive for a masochist. Stage 0 = humiliated, stage 1 = enjoys being displayed.</summary>
    public class ThoughtWorker_KeptNaked : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || !p.RaceProps.Humanlike || p.Dead) return ThoughtState.Inactive;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            if (prof == null || !prof.forceNudity) return ThoughtState.Inactive;
            return MasoStage.At(p);
        }
    }

    /// <summary>Situational mood while the pet is bound and on display - tied to a public spot or locked in an
    /// onahole bed. Negative normally, positive for a masochist.</summary>
    public class ThoughtWorker_BoundExposed : ThoughtWorker
    {
        private static HediffDef _onaholeBound;
        private static bool _tried;

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || !p.RaceProps.Humanlike || p.Dead) return ThoughtState.Inactive;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            bool bound = prof != null && prof.boundInPublic;
            if (!bound)
            {
                if (!_tried) { _tried = true; _onaholeBound = DefDatabase<HediffDef>.GetNamedSilentFail("RJWSH_OnaholeBound"); }
                try { bound = _onaholeBound != null && p.health?.hediffSet?.GetFirstHediffOfDef(_onaholeBound) != null; }
                catch { }
            }
            if (!bound) return ThoughtState.Inactive;
            return MasoStage.At(p);
        }
    }
}
