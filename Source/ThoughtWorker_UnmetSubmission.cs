using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>Restless/anxious mood when a conditioned pet's Submission need runs low - they crave direction
    /// and to serve. Stage 0 = uneasy, stage 1 = distressed. Inactive when the need isn't present or is filled.</summary>
    public class ThoughtWorker_UnmetSubmission : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || !p.RaceProps.Humanlike || p.Dead) return ThoughtState.Inactive;
            var need = Need_Submission.For(p);
            if (need == null) return ThoughtState.Inactive;
            if (need.CurLevel < 0.15f) return ThoughtState.ActiveAtStage(1);
            if (need.CurLevel < 0.35f) return ThoughtState.ActiveAtStage(0);
            return ThoughtState.Inactive;
        }
    }
}
