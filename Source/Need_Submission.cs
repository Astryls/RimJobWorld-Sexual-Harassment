using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The conditioned/owned pet's need to submit and serve. Falls over time (unmet -> restless/anxious,
    /// feeds a negative mood via ThoughtWorker_UnmetSubmission and more eager self-presentation in
    /// DepthAutonomousTick); serving, being rewarded, and being disciplined raise it (SatisfySubmission).
    /// Present only on tracked conditioned/owned pets - gated in Pawn_NeedsTracker.ShouldHaveNeed.
    /// </summary>
    public class Need_Submission : Need
    {
        public Need_Submission(Pawn pawn) : base(pawn) { }

        // Left to itself the need drains, so the bar trends down.
        public override int GUIChangeArrow => -1;

        public override void SetInitialLevel() => CurLevel = 0.5f;

        public override void NeedInterval()
        {
            if (IsFrozen) return;
            float fall = 0.0016f;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pawn);
            if (prof?.sex != null && prof.sex.seeded)
                fall *= 1f + Mathf.Clamp01(-prof.sex.subDom / 100f); // a submissive psyche hungers for it faster
            CurLevel -= fall;
        }

        public static Need_Submission For(Pawn p) => p?.needs?.TryGetNeed<Need_Submission>();
    }
}
