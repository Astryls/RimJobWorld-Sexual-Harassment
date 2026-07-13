using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// A forced-action memory whose mood flips with masochism: stage 0 (negative - resents being used) for a
    /// normal pawn, stage 1 (positive - revels in it) once the pawn is a masochist. The conditioning system
    /// (CompCollarConditioning) permanently grants the vanilla Masochist trait at ~50% conditioning, so a
    /// well-conditioned pet's degradation memories turn into pleasures. The flip is live - existing memories
    /// re-evaluate their stage every time the mood is read, so they swing positive the instant the trait lands.
    /// Each ThoughtDef using this class MUST define exactly two stages (0 = negative, 1 = positive).
    /// </summary>
    public class Thought_MaybeMasochist : Thought_Memory
    {
        public override int CurStageIndex
        {
            get
            {
                try { return rjw.xxx.is_masochist(pawn) ? 1 : 0; }
                catch { return 0; }
            }
        }
    }
}
