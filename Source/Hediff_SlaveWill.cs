using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Visible "will" meter on a pawn under another pawn's collar control. The live percentage is read from
    /// the pawn's profile (slaveWill); the control system rolls breakouts against it and removes this hediff
    /// when the pawn is freed.
    /// </summary>
    public class Hediff_SlaveWill : HediffWithComps
    {
        public override string LabelInBrackets
        {
            get
            {
                var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pawn);
                int w = prof != null ? (int)prof.slaveWill : (int)(Severity * 100f);
                return w + "%";
            }
        }

        // Managed by the control system (removed on breakout / when control ends), not by severity decay.
        public override bool ShouldRemove => false;
    }
}
