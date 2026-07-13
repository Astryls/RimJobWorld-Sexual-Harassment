using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Persistent situational mood for a collared / owned pawn, scaled by conditioning:
    ///   stage 0 - unconditioned: resentful (negative mood)
    ///   stage 1 - conditioned (hypnosis >= 60): resigned / content (slight positive)
    ///   stage 2 - fully conditioned (hypnosis >= 90 or Stockholm syndrome): devoted (strong positive)
    /// Inactive for anyone who is neither collared nor owned.
    /// </summary>
    public class ThoughtWorker_Collared : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || !p.RaceProps.Humanlike || p.Dead) return ThoughtState.Inactive;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            bool owned = prof != null && (prof.ownerId >= 0 || prof.relationshipOwnerId >= 0);
            bool collared = HarassmentEngine.WearingControlCollar(p) || HarassmentEngine.IsLockedPawn(p);
            if (!owned && !collared) return ThoughtState.Inactive;
            if (HarassmentEngine.IsFullyConditioned(p)) return ThoughtState.ActiveAtStage(2);
            if (prof != null && prof.IsConditioned) return ThoughtState.ActiveAtStage(1);
            return ThoughtState.ActiveAtStage(0);
        }
    }
}
