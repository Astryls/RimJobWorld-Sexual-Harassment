using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// One-time pause prompt when a player pawn is about to be stripped by force. The choice sets how
    /// the in-world strip plays out: Resist (slower, the offender roughs them up, a chance to break
    /// free per layer), Submit (faster, no damage), or send a colonist to intervene (one-shot rescue).
    /// </summary>
    public class Dialog_StruggleStrip : Window
    {
        private readonly Pawn harasser;
        private readonly Pawn victim;
        private bool resolved;
        private string lastMsg;

        public override Vector2 InitialSize => new Vector2(470f, 250f);

        public Dialog_StruggleStrip(Pawn harasser, Pawn victim)
        {
            this.harasser = harasser;
            this.victim = victim;
            forcePause = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            preventCameraMotion = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (harasser == null || victim == null || harasser.Dead || victim.Dead || !harasser.Spawned || !victim.Spawned)
            {
                resolved = true;
                HarassmentEngine.EndPhysical(victim);
                Close();
                return;
            }

            var l = new Listing_Standard();
            l.Begin(inRect);
            Text.Font = GameFont.Medium;
            l.Label("Harassment");
            Text.Font = GameFont.Small;
            l.Gap(4f);
            l.Label(harasser.LabelShort + " has cornered " + victim.LabelShort + " and is about to strip them by force.");
            if (!lastMsg.NullOrEmpty())
            {
                GUI.color = new Color(1f, 0.85f, 0.6f);
                l.Label(lastMsg);
                GUI.color = Color.white;
            }
            l.End();

            float btnH = 34f;
            float y = inRect.height - btnH;
            float halfW = (inRect.width - 10f) / 2f;
            float rowY = y - btnH - 6f;

            if (Widgets.ButtonText(new Rect(0f, rowY, halfW, btnH), "Resist"))
            {
                resolved = true;
                Close();
                HarassmentEngine.StartStripJob(harasser, victim, submitted: false);
            }
            if (Widgets.ButtonText(new Rect(halfW + 10f, rowY, halfW, btnH), "Submit"))
            {
                resolved = true;
                Close();
                HarassmentEngine.StartStripJob(harasser, victim, submitted: true);
            }
            if (Widgets.ButtonText(new Rect(0f, y, inRect.width, btnH), "Send someone to intervene"))
            {
                if (HarassmentEngine.TryIntervene(harasser, victim))
                {
                    resolved = true;
                    HarassmentEngine.EndPhysical(victim);
                    Close();
                }
                else
                {
                    lastMsg = "Nobody could stop it. Resist or submit.";
                }
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            // Dismissed without a choice defaults to resisting.
            if (!resolved) HarassmentEngine.StartStripJob(harasser, victim, submitted: false);
        }
    }
}
