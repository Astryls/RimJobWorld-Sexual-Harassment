using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Ties our collar/conditioning system into vanilla (Ideology) slavery. Conditioning suppresses
    /// rebellion via the vanilla Need_Suppression, and a fully-conditioned collared prisoner can be turned
    /// into a real colony slave. All guarded so it no-ops without Ideology / on non-slave pawns.
    /// </summary>
    public static class SlaveryHooks
    {
        /// <summary>Keep a collared, conditioned slave's vanilla Suppression need topped up so they will not
        /// rebel - the collar and conditioning do the suppressing. A volatile, low-rapport slave is deliberately
        /// left to vanilla rebellion (fear-broken pets are unstable, which is the whole point of the rapport axis).</summary>
        public static void SyncSuppression(Pawn pawn, PawnProfile prof)
        {
            try
            {
                if (pawn == null || prof == null || !pawn.IsSlaveOfColony) return;
                if (!HarassmentEngine.WearingControlCollar(pawn)) return;
                if (pawn.needs == null || !pawn.needs.TryGetNeed(out Need_Suppression need)) return;
                if (prof.IsVolatile) return;   // fear-broken slaves stay rebellious on purpose
                // Suppression floor scales with conditioning: a fully conditioned slave is fully suppressed.
                float target = UnityEngine.Mathf.Clamp01(prof.hypnosisLevel / 100f);
                if (need.CurLevel < target) need.CurLevel = target;
            }
            catch { }
        }

        /// <summary>A collared, fully-conditioned prisoner is ready to be made a permanent colony slave.</summary>
        public static bool CanEnslave(Pawn prisoner)
        {
            return prisoner != null && prisoner.IsPrisonerOfColony
                && HarassmentEngine.WearingControlCollar(prisoner)
                && HarassmentEngine.IsFullyConditioned(prisoner);
        }

        /// <summary>Converts a conditioned collared prisoner into a real colony slave via vanilla guest status.</summary>
        public static void Enslave(Pawn prisoner, Pawn owner)
        {
            try
            {
                if (prisoner?.guest == null || !prisoner.IsPrisonerOfColony) return;
                prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);
                if (owner != null) HarassmentEngine.EnsureOwnerRelation(owner, prisoner);
                Messages.Message(prisoner.LabelShortCap + " has been broken in and enslaved to the colony. Their collar and conditioning keep them docile.",
                    new LookTargets(prisoner), MessageTypeDefOf.PositiveEvent, false);
            }
            catch { }
        }
    }
}
