using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>Shared renderer for a pawn's deep sexual attributes. Used both by the standalone
    /// ITab_Pawn_Sexuality and (when Modern Bio Tab is present) by the registered bio-tab panel.</summary>
    public static class SexualityPanelDrawer
    {
        private static readonly Color HeaderCol = new Color(0.90f, 0.74f, 0.32f);
        private static readonly Color BarEmpty = new Color(0.16f, 0.16f, 0.18f);

        private static float _lastContentH = 300f;   // measured content height so the scroll view fits the text
        private const float CRowH = 18f;   // compact data-row pitch (MBT hook)
        private const float CHdrH = 20f;   // compact section-header pitch (MBT hook)
        // Row tooltips (parity with the standalone tab, which MBT suppresses when present).
        private const string ArousalTip   = "How aroused this pawn is right now, mirrored from RJW's sex need. High means frustrated and craving release.";
        private const string AlcoholTip   = "Blood alcohol intoxication, mirrored from the vanilla AlcoholHigh effect.";
        private const string WillpowerTip = "Resolve against coercion and conditioning. High willpower resists being broken and makes a pet more likely to flee a beating.";
        private const string EsteemTip    = "Sense of self-worth. Low self-esteem makes a pawn far easier to break down and control.";
        private const string SpiritTip    = "Inner drive and resilience - the fire that keeps them fighting back and bolting from punishment.";
        private const string AddictionTip = "Compulsion and craving for sex. High addiction makes a pawn seek it out and easier to bribe with it.";
        private const string TraumaTip    = "Accumulated sexual trauma - rises when raped, softened for masochists, fades slowly over time.";
        private const string SubDomTip    = "Submissive/Dominant orientation. Left is submissive (yields), right is dominant (takes charge). Seeded from traits/backstory/RimPsyche and shifted by domineering acts.";
        private const string RepTip       = "This pawn's sexual reputation in the world. Mirrored from Karma & Reputation when installed, otherwise derived from colony notoriety and rumor.";

        public static void Draw(Rect rect, Pawn pawn, ref Vector2 scroll)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return;
            var prof = GameComponent_Harassment.Instance?.GetProfile(pawn);
            var sx = prof?.SexAttr(pawn);
            if (sx == null) return;

            var view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(rect.height, _lastContentH));
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(rect, ref scroll, view);
            float y = 0f;

            y = Section(view, y, "Physical");
            if (SexAttributes.HasMouth(pawn)) y = Bar(view, y, "Oral wear", sx.wearOral, WearColor(sx.wearOral), WearTip("mouth and throat", sx.wearOral));
            if (SexAttributes.HasVagina(pawn)) y = Bar(view, y, "Vaginal wear", sx.wearVaginal, WearColor(sx.wearVaginal), WearTip("vagina", sx.wearVaginal));
            if (SexAttributes.HasPenis(pawn)) y = Bar(view, y, "Penis wear", sx.wearPenis, WearColor(sx.wearPenis), WearTip("penis", sx.wearPenis));
            if (SexAttributes.HasAnus(pawn)) y = Bar(view, y, "Anal wear", sx.wearAnal, WearColor(sx.wearAnal), WearTip("anus", sx.wearAnal));
            float arous = SexAttributes.Arousal(pawn);
            if (arous >= 0f) y = Bar(view, y, "Arousal", arous, new Color(0.85f, 0.35f, 0.55f), "How aroused this pawn is right now, mirrored from RJW's sex need. High means frustrated and craving release.");
            y = Bar(view, y, "Alcohol", SexAttributes.Alcohol(pawn), new Color(0.85f, 0.65f, 0.25f), "Blood alcohol intoxication, mirrored from the vanilla AlcoholHigh effect.");

            y += 6f;
            y = Section(view, y, "Psychological");
            y = Bar(view, y, "Willpower", sx.willpower, new Color(0.40f, 0.65f, 0.85f), "Resolve against coercion and conditioning. High willpower resists being broken and makes a pet more likely to flee a beating.");
            y = Bar(view, y, "Self-esteem", sx.selfEsteem, new Color(0.45f, 0.70f, 0.55f), "Sense of self-worth. Low self-esteem makes a pawn far easier to break down and control.");
            y = Bar(view, y, "Spirit", sx.spirit, new Color(0.60f, 0.55f, 0.80f), "Inner drive and resilience - the fire that keeps them fighting back and bolting from punishment.");
            y = SubDomBar(view, y, sx.subDom);
            y = Bar(view, y, "Sex addiction", sx.sexAddiction, new Color(0.85f, 0.40f, 0.60f), "Compulsion and craving for sex. High addiction makes a pawn seek it out and easier to bribe with it.");
            y = Bar(view, y, "Trauma", sx.trauma, new Color(0.70f, 0.30f, 0.30f), "Accumulated sexual trauma - rises when raped, softened for masochists, fades slowly over time.");

            y += 6f;
            y = Section(view, y, "Social");
            DrawTextRow(view, ref y, "Reputation", HarassmentEngine.WorldReputationLabel(pawn),
                "This pawn's sexual reputation in the world. Mirrored from Karma & Reputation when installed, otherwise derived from colony notoriety and rumor.");

            _lastContentH = y + 4f;
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
        }

        /// <summary>Full attribute set in a compact, groupless style for Modern Bio Tab's RegisterSexualityStat
        /// hook. MBT gates this behind its stats toggle and hosts it in a scroll box, so we render EVERY row at
        /// natural height (no internal clipping) and report that exact height via MeasureCompact. Drawn at
        /// rect-absolute coords (no BeginGroup) so highlights/tooltips resolve inside MBT's scroll view.</summary>
        public static float MeasureCompact(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return 0f;
            if (GameComponent_Harassment.Instance?.GetProfile(pawn)?.SexAttr(pawn) == null) return 0f;
            int physical = 1; // alcohol (always shown)
            if (SexAttributes.HasMouth(pawn))  physical++;
            if (SexAttributes.HasVagina(pawn)) physical++;
            if (SexAttributes.HasPenis(pawn))  physical++;
            if (SexAttributes.HasAnus(pawn))   physical++;
            if (SexAttributes.Arousal(pawn) >= 0f) physical++;
            const int psychological = 6; // willpower, self-esteem, spirit, sub/dom, addiction, trauma
            const int social = 1;        // reputation
            return 3f * CHdrH + (physical + psychological + social) * CRowH + 2f;
        }

        public static void DrawCompact(Rect rect, Pawn pawn, SexAttributes sxOverride = null)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return;
            // sxOverride lets the market preview render a freshly-seeded attribute set without creating a profile.
            var sx = sxOverride ?? GameComponent_Harassment.Instance?.GetProfile(pawn)?.SexAttr(pawn);
            if (sx == null) return;

            var prevFont = Text.Font;
            var prevWrap = Text.WordWrap;
            Text.Font = GameFont.Tiny;
            Text.WordWrap = false;   // single-line labels: long ones (Vaginal wear / Self-esteem / Sex addiction) must not wrap to a 2nd line and spill into the next row
            float x = rect.x, w = rect.width, y = rect.y;

            CHeader(x, w, ref y, "Physical");
            if (SexAttributes.HasMouth(pawn))  CRow(x, w, ref y, "Oral wear",    sx.wearOral,    WearColor(sx.wearOral),    WearTip("mouth and throat", sx.wearOral));
            if (SexAttributes.HasVagina(pawn)) CRow(x, w, ref y, "Vaginal wear", sx.wearVaginal, WearColor(sx.wearVaginal), WearTip("vagina", sx.wearVaginal));
            if (SexAttributes.HasPenis(pawn))  CRow(x, w, ref y, "Penis wear",   sx.wearPenis,   WearColor(sx.wearPenis),   WearTip("penis", sx.wearPenis));
            if (SexAttributes.HasAnus(pawn))   CRow(x, w, ref y, "Anal wear",    sx.wearAnal,    WearColor(sx.wearAnal),    WearTip("anus", sx.wearAnal));
            float arous = SexAttributes.Arousal(pawn);
            if (arous >= 0f) CRow(x, w, ref y, "Arousal", arous, new Color(0.85f, 0.35f, 0.55f), ArousalTip);
            CRow(x, w, ref y, "Alcohol", SexAttributes.Alcohol(pawn), new Color(0.85f, 0.65f, 0.25f), AlcoholTip);

            CHeader(x, w, ref y, "Psychological");
            CRow(x, w, ref y, "Willpower",     sx.willpower,    new Color(0.40f, 0.65f, 0.85f), WillpowerTip);
            CRow(x, w, ref y, "Self-esteem",   sx.selfEsteem,   new Color(0.45f, 0.70f, 0.55f), EsteemTip);
            CRow(x, w, ref y, "Spirit",        sx.spirit,       new Color(0.60f, 0.55f, 0.80f), SpiritTip);
            CSubDomRow(x, w, ref y, sx.subDom);
            CRow(x, w, ref y, "Sex addiction", sx.sexAddiction, new Color(0.85f, 0.40f, 0.60f), AddictionTip);
            CRow(x, w, ref y, "Trauma",        sx.trauma,       new Color(0.70f, 0.30f, 0.30f), TraumaTip);

            CHeader(x, w, ref y, "Social");
            CTextRow(x, w, ref y, "Reputation", HarassmentEngine.WorldReputationLabel(pawn), RepTip);

            Text.Font = prevFont;
            Text.WordWrap = prevWrap;
            GUI.color = Color.white;
        }

        private static void CHeader(float x, float w, ref float y, string title)
        {
            GUI.color = HeaderCol;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(x, y + 1f, w, 16f), title);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawLineHorizontal(x, y + 18f, w);
            GUI.color = Color.white;
            y += CHdrH;
        }

        private static void CRow(float x, float w, ref float y, string label, float v, Color fill, string tip)
        {
            var r = new Rect(x, y, w, CRowH - 2f);
            if (!tip.NullOrEmpty() && Mouse.IsOver(r)) { Widgets.DrawHighlight(r); TooltipHandler.TipRegion(r, tip); }
            CompactBar(r, label, v, fill);
            y += CRowH;
        }

        private static void CTextRow(float x, float w, ref float y, string label, string val, string tip)
        {
            var r = new Rect(x, y, w, CRowH - 2f);
            if (!tip.NullOrEmpty() && Mouse.IsOver(r)) { Widgets.DrawHighlight(r); TooltipHandler.TipRegion(r, tip); }
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            Widgets.Label(new Rect(r.x, r.y, 70f, r.height), label);
            GUI.color = Color.white;
            Widgets.Label(new Rect(r.x + 74f, r.y, r.width - 74f, r.height), val);
            Text.Anchor = TextAnchor.UpperLeft;
            y += CRowH;
        }

        private static void CSubDomRow(float x, float w, ref float y, float v)
        {
            int pct = Mathf.RoundToInt(v);
            string cls = v < -8f ? "Submissive" : v > 8f ? "Dominant" : "Switch";
            string signedPct = (pct > 0 ? "+" : "") + pct + "%";
            var r = new Rect(x, y, w, CRowH - 2f);
            if (Mouse.IsOver(r))
            {
                Widgets.DrawHighlight(r);
                TooltipHandler.TipRegion(r, cls + " - " + signedPct + " on the submissive (-100%) to dominant (+100%) scale.\n\n" + SubDomTip);
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            Widgets.Label(new Rect(r.x, r.y, 70f, r.height), "Sub / Dom");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            var bar = new Rect(r.x + 74f, r.y + (r.height - 9f) * 0.5f, r.width - 118f, 9f);
            if (bar.width > 6f)
            {
                Widgets.DrawBoxSolid(bar, BarEmpty);
                float mid = bar.center.x;
                GUI.color = new Color(1f, 1f, 1f, 0.25f);
                Widgets.DrawLineVertical(mid, bar.y, bar.height);
                GUI.color = Color.white;
                float half = bar.width * 0.5f;
                float frac = Mathf.Clamp(v / 100f, -1f, 1f);
                if (frac >= 0f) Widgets.DrawBoxSolid(new Rect(mid, bar.y, Mathf.Max(1f, half * frac), bar.height), new Color(0.80f, 0.35f, 0.35f));
                else            Widgets.DrawBoxSolid(new Rect(mid + half * frac, bar.y, Mathf.Max(1f, -half * frac), bar.height), new Color(0.40f, 0.55f, 0.80f));
            }
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(bar.xMax + 2f, r.y, 40f, r.height), signedPct);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            y += CRowH;
        }

        private static void CompactBar(Rect r, string label, float v, Color fill)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            Widgets.Label(new Rect(r.x, r.y, 70f, r.height), label);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            float bh = Mathf.Min(9f, r.height);
            var bar = new Rect(r.x + 74f, r.y + (r.height - bh) * 0.5f, r.width - 108f, bh);
            Widgets.FillableBar(bar, Mathf.Clamp01(v / 100f), Tex(fill), Tex(BarEmpty), false);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(bar.xMax + 2f, r.y, 30f, r.height), Mathf.RoundToInt(v) + "%");
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static float Section(Rect view, float y, string title)
        {
            Text.Font = GameFont.Small;
            GUI.color = HeaderCol;
            Widgets.Label(new Rect(0f, y, view.width, 22f), title);
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            Widgets.DrawLineHorizontal(0f, y + 21f, view.width);
            GUI.color = Color.white;
            return y + 26f;
        }

        private static float Bar(Rect view, float y, string label, float pct0to100, Color fill, string tip)
        {
            var row = new Rect(0f, y, view.width, 22f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(row.x, row.y, 96f, row.height), label);
            Text.Anchor = TextAnchor.UpperLeft;
            var bar = new Rect(row.x + 100f, row.y + 3f, row.width - 100f, 16f);
            Widgets.FillableBar(bar, Mathf.Clamp01(pct0to100 / 100f), Tex(fill), Tex(BarEmpty), false);
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            Widgets.Label(bar, Mathf.RoundToInt(pct0to100) + "%");
            Text.Anchor = TextAnchor.UpperLeft;
            if (!tip.NullOrEmpty()) { Widgets.DrawHighlightIfMouseover(row); TooltipHandler.TipRegion(row, tip); }
            return y + 24f;
        }

        private static float SubDomBar(Rect view, float y, float v)
        {
            var row = new Rect(0f, y, view.width, 22f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(row.x, row.y, 96f, row.height), "Sub / Dom");
            Text.Anchor = TextAnchor.UpperLeft;
            var bar = new Rect(row.x + 100f, row.y + 3f, row.width - 100f, 16f);
            Widgets.DrawBoxSolid(bar, BarEmpty);
            float mid = bar.center.x;
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            Widgets.DrawLineVertical(mid, bar.y, bar.height);
            GUI.color = Color.white;
            float half = bar.width * 0.5f;
            float frac = Mathf.Clamp(v / 100f, -1f, 1f);
            if (frac >= 0f)
                Widgets.DrawBoxSolid(new Rect(mid, bar.y, Mathf.Max(1f, half * frac), bar.height), new Color(0.80f, 0.35f, 0.35f));
            else
                Widgets.DrawBoxSolid(new Rect(mid + half * frac, bar.y, Mathf.Max(1f, -half * frac), bar.height), new Color(0.40f, 0.55f, 0.80f));
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, v < -8f ? "submissive" : v > 8f ? "dominant" : "switch");
            Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(row)) { Widgets.DrawHighlightIfMouseover(row); TooltipHandler.TipRegion(row, "Submissive/Dominant orientation. Left is submissive (yields), right is dominant (takes charge). Seeded from traits/backstory/RimPsyche and shifted by domineering acts - beatings and rape push toward submissive, dominating others pushes toward dominant."); }
            return y + 24f;
        }

        private static void DrawTextRow(Rect view, ref float y, string label, string val, string tip)
        {
            var row = new Rect(0f, y, view.width, 22f);
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(row.x, row.y, 96f, row.height), label);
            GUI.color = Color.white;
            Widgets.Label(new Rect(row.x + 100f, row.y, row.width - 100f, row.height), val);
            Text.Anchor = TextAnchor.UpperLeft;
            if (!tip.NullOrEmpty()) { Widgets.DrawHighlightIfMouseover(row); TooltipHandler.TipRegion(row, tip); }
            y += 24f;
        }

        private static Color WearColor(float v) => v < 33f ? new Color(0.45f, 0.70f, 0.50f) : v < 66f ? new Color(0.80f, 0.70f, 0.30f) : new Color(0.80f, 0.40f, 0.35f);
        private static string WearTip(string which, float v) => "How worn and loosened this pawn's " + which + " is from use. Rises with each act on that part; recovers slowly over time.";

        private static readonly System.Collections.Generic.Dictionary<Color, Texture2D> _texCache = new System.Collections.Generic.Dictionary<Color, Texture2D>();
        private static Texture2D Tex(Color c)
        {
            if (!_texCache.TryGetValue(c, out var t)) { t = SolidColorMaterials.NewSolidColorTexture(c); _texCache[c] = t; }
            return t;
        }
    }
}
