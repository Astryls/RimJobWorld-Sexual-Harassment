using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Pause prompt when a player pawn is blackmailed with a scandalous photo. Comply (submit to the
    /// demands), Refuse (the photos get distributed -> humiliation), or Intimidate (chance to scare the
    /// blackmailer off and destroy the photo).
    /// </summary>
    public class Dialog_Blackmail : Window
    {
        private readonly Pawn harasser;
        private readonly Pawn target;
        private bool resolved;
        private string lastMsg;

        public override Vector2 InitialSize => new Vector2(480f, 250f);

        public Dialog_Blackmail(Pawn harasser, Pawn target)
        {
            this.harasser = harasser;
            this.target = target;
            forcePause = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (harasser == null || target == null || harasser.Dead || target.Dead || !harasser.Spawned || !target.Spawned)
            {
                resolved = true;
                Close();
                return;
            }

            var l = new Listing_Standard();
            l.Begin(inRect);
            Text.Font = GameFont.Medium;
            l.Label("Blackmail");
            Text.Font = GameFont.Small;
            l.Gap(4f);
            l.Label(harasser.LabelShort + " is blackmailing " + target.LabelShort + " with a scandalous photo and demanding compliance.");
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

            if (Widgets.ButtonText(new Rect(0f, rowY, halfW, btnH), "Comply"))
            {
                resolved = true;
                Close();
                HarassmentEngine.BlackmailComply(harasser, target);
            }
            if (Widgets.ButtonText(new Rect(halfW + 10f, rowY, halfW, btnH), "Refuse (photos spread)"))
            {
                resolved = true;
                Close();
                HarassmentEngine.BlackmailRefuse(harasser, target);
            }
            if (Widgets.ButtonText(new Rect(0f, y, inRect.width, btnH), "Intimidate (destroy the photo)"))
            {
                if (HarassmentEngine.BlackmailIntimidate(harasser, target))
                {
                    resolved = true;
                    Close();
                }
                else
                {
                    lastMsg = "It did not work. Comply, or refuse and let the photos spread.";
                }
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            if (!resolved) HarassmentEngine.BlackmailComply(harasser, target);
        }
    }
}
