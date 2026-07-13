using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>Situational mood scaling with a pawn's sexual trauma attribute: stage 0 (>=40) haunted,
    /// stage 1 (>=75) deeply scarred. Masochists shrug it off (they reframed the source), so it stays inactive
    /// for them.</summary>
    public class ThoughtWorker_Traumatized : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || p.Dead || p.RaceProps == null || !p.RaceProps.Humanlike) return ThoughtState.Inactive;
            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(p);
            var sx = prof?.sex;
            if (sx == null || !sx.seeded) return ThoughtState.Inactive;
            try { if (rjw.xxx.is_masochist(p)) return ThoughtState.Inactive; } catch { }
            if (sx.trauma >= 75f) return ThoughtState.ActiveAtStage(1);
            if (sx.trauma >= 40f) return ThoughtState.ActiveAtStage(0);
            return ThoughtState.Inactive;
        }
    }
}
