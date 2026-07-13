using Verse;

namespace RJWSexualHarassment
{
    /// <summary>One dated line in a pawn's life story - the narrative ledger behind the Command deck's
    /// History tab. Distinct from CondEvent (which graphs stat deltas): a ChronicleEntry is prose,
    /// the moments worth remembering across years (collared, broke, paraded, sold, owner died).</summary>
    public class ChronicleEntry : IExposable
    {
        public int tick;
        public string text;
        public int kind;   // 0 neutral, 1 dark (collar/abuse/loss), 2 bright (freedom/reward), 3 world (scandal/market)

        public ChronicleEntry() { }
        public ChronicleEntry(int tick, string text, int kind)
        {
            this.tick = tick; this.text = text; this.kind = kind;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "t", 0);
            Scribe_Values.Look(ref text, "x");
            Scribe_Values.Look(ref kind, "k", 0);
        }
    }
}
