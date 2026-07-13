using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Standalone inspect tab on every humanlike showing their deep sexual attributes. When Modern Bio Tab is
    /// present this tab is NOT injected (SexualityTabInjector suppresses it) - the same content is shown as a
    /// registered panel inside the bio tab instead. Rendering is shared via SexualityPanelDrawer.
    /// </summary>
    public class ITab_Pawn_Sexuality : ITab
    {
        private static readonly Vector2 WinSize = new Vector2(360f, 480f);
        private Vector2 _scroll;

        public ITab_Pawn_Sexuality()
        {
            size = WinSize;
            labelKey = "RJWSH_TabSexuality";
        }

        public override bool IsVisible
        {
            get
            {
                if (!(RimJobWorldSexualHarassmentMod.Settings?.showSexualityTab ?? false)) return false;
                var p = PawnToShow;
                return p != null && p.RaceProps != null && p.RaceProps.Humanlike;
            }
        }

        private Pawn PawnToShow => SelPawn ?? (SelThing as Corpse)?.InnerPawn;

        protected override void FillTab()
        {
            var pawn = PawnToShow;
            if (pawn == null) return;
            var outer = new Rect(0f, 0f, WinSize.x, WinSize.y).ContractedBy(12f);
            SexualityPanelDrawer.Draw(outer, pawn, ref _scroll);
        }
    }
}
