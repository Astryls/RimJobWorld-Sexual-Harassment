using Verse;

namespace RJWSexualHarassment
{
    /// <summary>One discrete conditioning/rapport event on a pet's timeline (discipline, reward, shock, forced
    /// act, breakout), for the dashboard's events overlay: shows how each event moved conditioning vs rapport.</summary>
    public class CondEvent : IExposable
    {
        public int tick;
        public string label;
        public float condDelta;   // change to conditioning (hypnosisLevel)
        public float rapDelta;    // change to rapport (their will/resistance axis)

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "t", 0);
            Scribe_Values.Look(ref label, "l");
            Scribe_Values.Look(ref condDelta, "c", 0f);
            Scribe_Values.Look(ref rapDelta, "r", 0f);
        }
    }
}
