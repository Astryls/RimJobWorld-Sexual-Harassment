using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Psycast-style readout on a collared pawn: two stacked bars - conditioning (how broken to the collar
    /// they are) and rapport (trust vs fear). Mirrors the psychic-entropy gizmo layout.
    /// </summary>
    [StaticConstructorOnStartup] // has static Texture2D fields - must load them on the main thread
    public class Gizmo_Conditioning : Gizmo
    {
        private readonly Pawn pawn;

        private static readonly Texture2D CondBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.55f, 0.3f, 0.62f));
        private static readonly Texture2D TrustBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.85f, 0.68f, 0.28f));
        private static readonly Texture2D FearBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.72f, 0.22f, 0.22f));
        private static readonly Texture2D EmptyBarTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.12f, 0.12f, 0.12f));

        public Gizmo_Conditioning(Pawn pawn)
        {
            this.pawn = pawn;
            Order = -90f;
        }

        public override float GetWidth(float maxWidth) => 168f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Rect inner = rect.ContractedBy(6f);
            Widgets.DrawWindowBackground(rect);

            var prof = GameComponent_Harassment.Instance?.GetProfileIfExists(pawn);
            float cond = prof != null ? Mathf.Clamp01(prof.hypnosisLevel / 100f) : 0f;
            float rapport = prof != null ? Mathf.Clamp01(prof.rapport / 100f) : 0.5f;

            Text.Font = GameFont.Tiny;
            var l1 = inner; l1.y += 2f; l1.height = Text.LineHeight;
            Widgets.Label(l1, "Conditioned");
            var l2 = inner; l2.y += 36f; l2.height = Text.LineHeight;
            Widgets.Label(l2, "Rapport");

            var b1 = new Rect(inner.x + 66f, inner.y + 2f, inner.width - 66f, 24f);
            Widgets.FillableBar(b1, cond, CondBarTex, EmptyBarTex, doBorder: true);
            var b2 = new Rect(inner.x + 66f, inner.y + 36f, inner.width - 66f, 24f);
            Widgets.FillableBar(b2, rapport, rapport < 0.4f ? FearBarTex : TrustBarTex, EmptyBarTex, doBorder: true);

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(b1, ((int)(cond * 100f)) + "%");
            Widgets.Label(b2, ((int)(rapport * 100f)) + "%");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            string ctrl = (prof != null && prof.aiControlled) ? (HarassmentEngine.ControllerLabel(pawn) ?? "someone") : null;
            TooltipHandler.TipRegion(rect, () =>
                "Conditioning: how deeply " + pawn.LabelShort + " is broken to the collar. It rises with each use by the key-holder, scaled by their vulnerability.\n\n" +
                "Rapport: whether they obey out of trust or fear. Reward and affection raise it; discipline and shocks lower it. A pet broken by the whip (low rapport) stays volatile and keeps fighting back even when deeply conditioned, while one won over with kindness (high rapport) turns placid and is far harder to snap free." +
                (ctrl != null ? "\n\nControlled by " + ctrl + "." : ""),
                Gen.HashCombineInt(pawn.GetHashCode(), 0x5A1346));

            return new GizmoResult(GizmoState.Clear);
        }
    }
}
