using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
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
