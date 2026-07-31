using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Shared portrait drawing. Every portrait in this mod goes through here.
    ///
    /// WHY THIS EXISTS: PortraitsCache keys its RenderTexture cache on
    /// (size x cameraOffset x cameraZoom x rotation x flags), then per pawn inside that bucket, and it
    /// allocates `new RenderTexture(w, h, 24)` at requestedSize * 1.25 (supersample) * Prefs.UIScale on every
    /// miss. Expired entries are parked in a pool that is ONLY ever emptied by PortraitsCache.Clear()
    /// (game load / quit) - nothing trims it during play. So every distinct size we ever ask for mints a
    /// RenderTexture that stays resident for the rest of the session. Handing it a size derived from a
    /// RESIZEABLE window rect therefore strands roughly 1.5 MB per pixel of drag.
    ///
    /// Two rules follow, and both are enforced here rather than at the call sites:
    ///   1. Never request an unquantised, resize-derived size (see PortraitSizer).
    ///   2. Fixed-size headshots snap to a short ladder so 8 call sites do not mint 8 RenderTexture
    ///      families - and 8 full PawnCacheRenderer passes - per pawn (see PortraitBucket).
    /// Both quantise UP, and PortraitsCache supersamples 1.25x on top, so the texture always covers the rect
    /// we draw into: we are always downscaling, never upscaling. ScaleToFit keeps the pawn unstretched.
    /// </summary>
    public static class Portraits
    {
        /// <summary>Round up to a multiple of `step`.</summary>
        public static float SnapUp(float v, float step) => Mathf.Ceil(v / step) * step;

        // Chosen so every existing headshot call site (30/31/34/36/42/44/56/64 px) lands on a bucket whose
        // 1.25x render is at least as supersampled as it was before - i.e. this is non-degrading, and for
        // most sizes it is a slight quality gain. Collapses 8 buckets to 3.
        private static readonly float[] Ladder = { 32f, 48f, 64f, 96f, 128f };

        /// <summary>Smallest ladder bucket that still comfortably covers `size` px once supersampled.</summary>
        public static float PortraitBucket(float size)
        {
            float need = size * 0.9f;
            for (int i = 0; i < Ladder.Length; i++)
                if (Ladder[i] >= need) return Ladder[i];
            return SnapUp(size, 64f);
        }

        /// <summary>
        /// Picks the size to REQUEST for a portrait whose rect can change (anything inside a resizeable
        /// window). While the rect is still moving we ask for a coarsely-snapped size, so a whole resize drag
        /// costs a handful of RenderTextures instead of one per pixel. Once it holds still for a few frames we
        /// ask for the exact size again - so the settled image, which is the one the player actually looks at,
        /// is pixel-identical to an unquantised request. Hold one of these per portrait as a plain field.
        /// </summary>
        public struct Sizer
        {
            private Vector2 _last;
            private int _stableSince;

            public Vector2 Request(Vector2 exact, float snapStep = 32f, int settleFrames = 6)
            {
                int f = Time.frameCount;
                if (exact != _last) { _last = exact; _stableSince = f; }
                if (f - _stableSince >= settleFrames) return exact;   // settled: exact size, one texture
                return new Vector2(SnapUp(exact.x, snapStep), SnapUp(exact.y, snapStep));
            }
        }

        /// <summary>Fixed-size headshot. Repaint-gated and ladder-bucketed.</summary>
        public static void Head(Rect rect, Pawn p, float zoom = 1.2f)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (p == null) { GUI.DrawTexture(rect, BaseContent.GreyTex); return; }
            try
            {
                float b = PortraitBucket(Mathf.Max(rect.width, rect.height));
                var tex = PortraitsCache.Get(p, new Vector2(b, b), Rot4.South, default, zoom);
                if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
            }
            catch { Widgets.ThingIcon(rect, p); }
        }

        /// <summary>
        /// Full-body / free-size doll. `request` comes from a Sizer so a resize drag cannot mint a texture per
        /// pixel. ScaleToFit means a snapped request is letterboxed rather than stretched, and when the request
        /// equals the rect (the settled case) ScaleToFit is identical to a plain stretch-to-fill.
        /// </summary>
        public static void Body(Rect frame, Pawn p, Vector2 request, float zoom, float offZ)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (p == null) return;
            try
            {
                var tex = PortraitsCache.Get(p, request, Rot4.South, new Vector3(0f, 0f, offZ), zoom,
                                             healthStateOverride: PawnHealthState.Mobile);
                if (tex != null) GUI.DrawTexture(frame, tex, ScaleMode.ScaleToFit);
            }
            catch { Widgets.ThingIcon(frame, p); }
        }
    }

    /// <summary>
    /// Shared dark "Modern Suite" styling, copied verbatim from the user's Modern UI mods
    /// (ModernFactionMenu.Palette / ModernStyle) so this window matches Modern Needs Tab etc.
    /// Copied, not referenced, so there is no hard dependency on those mods.
    /// </summary>
    public static class ModernStyle
    {
        public static readonly Color BG = FromHex(1382685);    // dark panel base
        public static readonly Color BGL = FromHex(3093303);   // lighter - borders / dividers
        public static readonly Color BGD = FromHex(921619);    // darkest - window/panel background (near black)
        public static readonly Color TextDim = new Color(0.62f, 0.65f, 0.7f);
        public static readonly Color Accent = new Color(0.45f, 0.75f, 1f);
        public static readonly Color PanelBG = Color.Lerp(BG, BGL, 0.22f);
        public static readonly Color Body = new Color(0.76f, 0.79f, 0.84f);

        public static Color FromHex(int hex) =>
            new Color(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);

        public static void DrawCard(Rect r)
        {
            Widgets.DrawBoxSolid(r, PanelBG);
            GUI.color = BGL;
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;
        }

        // ── Flat gray Modern-Suite scrollbar ─────────────────────────────────
        private static bool _scrollInit;
        private static GUIStyle _flatBar, _flatThumb, _flatBtn;
        private static GUIStyle _savedBar, _savedThumb, _savedUp, _savedDown;

        private static void InitScroll()
        {
            if (_scrollInit) return;
            _scrollInit = true;
            var track = SolidColorMaterials.NewSolidColorTexture(new Color(1f, 1f, 1f, 0.04f));
            var thumb = SolidColorMaterials.NewSolidColorTexture(new Color(0.55f, 0.58f, 0.62f, 0.55f));
            var thumbHover = SolidColorMaterials.NewSolidColorTexture(new Color(0.72f, 0.75f, 0.80f, 0.75f));
            _flatBar = new GUIStyle { fixedWidth = 8f };
            _flatBar.normal.background = track;
            _flatThumb = new GUIStyle { fixedWidth = 8f, border = new RectOffset(0, 0, 0, 0) };
            _flatThumb.normal.background = thumb;
            _flatThumb.hover.background = thumbHover;
            _flatThumb.active.background = thumbHover;
            _flatBtn = new GUIStyle(); // no up/down arrows
        }

        /// <summary>Swap the vertical scrollbar to a flat gray Modern style. Pair every call with PopScroll
        /// around a single BeginScrollView/EndScrollView block.</summary>
        public static void PushScroll()
        {
            InitScroll();
            _savedBar = GUI.skin.verticalScrollbar;
            _savedThumb = GUI.skin.verticalScrollbarThumb;
            _savedUp = GUI.skin.verticalScrollbarUpButton;
            _savedDown = GUI.skin.verticalScrollbarDownButton;
            GUI.skin.verticalScrollbar = _flatBar;
            GUI.skin.verticalScrollbarThumb = _flatThumb;
            GUI.skin.verticalScrollbarUpButton = _flatBtn;
            GUI.skin.verticalScrollbarDownButton = _flatBtn;
        }

        public static void PopScroll()
        {
            if (!_scrollInit) return;
            GUI.skin.verticalScrollbar = _savedBar;
            GUI.skin.verticalScrollbarThumb = _savedThumb;
            GUI.skin.verticalScrollbarUpButton = _savedUp;
            GUI.skin.verticalScrollbarDownButton = _savedDown;
        }
    }
}
