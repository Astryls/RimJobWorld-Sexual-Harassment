using System.Collections.Generic;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>A saved harem regimen - role + conditioning focus + 24-hour schedule - that can be applied to
    /// one pet or the whole harem in bulk from the Pet Dashboard's Harem view. Stored on the GameComponent.</summary>
    public class HaremPreset : IExposable
    {
        public string name;
        public int role;
        public string focus;
        public List<int> schedule;

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref role, "role", 0);
            Scribe_Values.Look(ref focus, "focus");
            Scribe_Collections.Look(ref schedule, "schedule", LookMode.Value);
        }
    }
}
