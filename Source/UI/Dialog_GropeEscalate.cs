using UnityEngine;
using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Pops when a directed grope lands. The player can force it further - rolling the victim's resist chance:
    /// success means the victim fights it off, failure escalates to a forced strip and act - or back off and
    /// leave it at a grope. The shown % is the victim's chance to successfully resist being forced down.
    /// </summary>
    public class Dialog_GropeEscalate : Window
    {
        private readonly Pawn harasser;
        private readonly Pawn victim;
        private readonly float resistChance;

        public override Vector2 InitialSize => new Vector2(470f, 230f);

        public Dialog_GropeEscalate(Pawn harasser, Pawn victim)
        {
            this.harasser = harasser;
            this.victim = victim;
            resistChance = HarassmentEngine.GropeResistChance(harasser, victim);
            forcePause = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (harasser == null || victim == null || harasser.Dead || victim.Dead || !harasser.Spawned || !victim.Spawned)
            {
                Close();
                return;
            }

            var l = new Listing_Standard();
            l.Begin(inRect);
            Text.Font = GameFont.Medium;
            l.Label("Groping");
            Text.Font = GameFont.Small;
            l.Gap(4f);
            l.Label(harasser.LabelShort + " is groping " + victim.LabelShort + ".");
            l.Gap(2f);
            GUI.color = new Color(1f, 0.85f, 0.6f);
            l.Label(victim.LabelShort + " has a " + resistChance.ToStringPercent() + " chance to resist being forced down.");
            GUI.color = Color.white;
            l.End();

            float btnH = 36f;
            float y = inRect.height - btnH;
            float halfW = (inRect.width - 10f) / 2f;

            if (Widgets.ButtonText(new Rect(0f, y, halfW, btnH), "Force them down"))
            {
                Close();
                if (Rand.Chance(resistChance))
                    HarassmentEngine.GropeFoughtOff(harasser, victim);
                else
                    HarassmentEngine.StartStripJob(harasser, victim, submitted: false);
            }
            if (Widgets.ButtonText(new Rect(halfW + 10f, y, halfW, btnH), "Back off"))
            {
                Close();
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            // Dismissed without choosing = back off (it stays a grope).
        }
    }
}
