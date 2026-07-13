using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>Centre-screen popout listing every known scandalous photo of a pawn. Styled to match the Modern
    /// Suite (flat ModernStyle panel, no vanilla frame, icon buttons): a large icon, the photo's lore, who
    /// currently controls it, and jump/destroy icons.</summary>
    public class Window_PhotoGallery : Window
    {
        private readonly Pawn _subject;
        private List<HarassmentEngine.PhotoRecord> _photos;
        private Vector2 _scroll;

        public Window_PhotoGallery(Pawn subject)
        {
            _subject = subject;
            forcePause = false;
            draggable = true;
            doCloseX = false;              // we draw our own close icon
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            doWindowBackground = false;    // draw our own flat Modern panel instead of the vanilla frame
            Refresh();
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(580f, 640f);

        private void Refresh() => _photos = HarassmentEngine.GatherPhotosOf(_subject);

        public override void DoWindowContents(Rect inRect)
        {
            // Flat panel background + border.
            Widgets.DrawBoxSolid(inRect, ModernStyle.BGD);
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(inRect, 1); GUI.color = Color.white;

            var pad = inRect.ContractedBy(14f);

            // Header: photo icon + title, close icon on the right.
            var head = new Rect(pad.x, pad.y, pad.width, 32f);
            var pdef = RJWSH_ThingDefOf.RJWSH_ScandalousPhoto;
            var hIcon = new Rect(head.x, head.y + 3f, 26f, 26f);
            if (pdef?.uiIcon != null) GUI.DrawTexture(hIcon, pdef.uiIcon);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(hIcon.xMax + 8f, head.y, head.width - 90f, 32f), "Photos of " + _subject.LabelShortCap);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            var closeR = new Rect(head.xMax - 26f, head.y + 2f, 24f, 24f);
            GUI.color = ModernStyle.TextDim;
            if (Widgets.ButtonText(closeR, "\u2715", drawBackground: false)) Close();
            GUI.color = Color.white;
            TooltipHandler.TipRegion(closeR, "Close");

            GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(pad.x, head.yMax, pad.width, 20f),
                _photos.Count == 0 ? "No known photos." : _photos.Count + " known photo(s).");
            GUI.color = Color.white;

            var listArea = new Rect(pad.x, head.yMax + 24f, pad.width, pad.yMax - head.yMax - 24f);
            const float rowH = 150f;
            var view = new Rect(0f, 0f, listArea.width - 16f, Mathf.Max(listArea.height, _photos.Count * (rowH + 8f)));
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(listArea, ref _scroll, view);
            float y = 0f;
            for (int i = 0; i < _photos.Count; i++)
            {
                DrawRow(new Rect(0f, y, view.width, rowH), _photos[i]);
                y += rowH + 8f;
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
        }

        private void DrawRow(Rect r, HarassmentEngine.PhotoRecord rec)
        {
            ModernStyle.DrawCard(r);
            bool circulating = rec.photo == null; // world record, no physical copy on the map

            // Large photo icon on the left (dimmed for world-circulating copies).
            var icon = new Rect(r.x + 10f, r.y + 10f, 128f, 128f);
            var pdef = RJWSH_ThingDefOf.RJWSH_ScandalousPhoto;
            var tex = (rec.photo != null && rec.photo.def.uiIcon != null) ? rec.photo.def.uiIcon : pdef?.uiIcon;
            if (tex != null)
            {
                GUI.color = circulating ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
                GUI.DrawTexture(icon, tex);
                GUI.color = Color.white;
            }
            else Widgets.DrawBoxSolid(icon, ModernStyle.PanelBG);

            float tx = icon.xMax + 12f;
            float tw = r.width - icon.width - 30f;
            GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(tx, r.y + 10f, tw, r.height - 48f), rec.comp?.loreDesc ?? rec.loreOverride ?? "A scandalous photo.");
            GUI.color = Color.white;

            // Holder line (bottom-left) + icon buttons (bottom-right) - reserve space so they never overlap.
            var target = rec.holderPawn ?? (rec.photo != null && rec.photo.Spawned ? (Thing)rec.photo : null);
            bool canBurn = rec.photo != null && !rec.photo.Destroyed;
            int nBtns = (target != null ? 1 : 0) + (canBurn ? 1 : 0);
            GUI.color = ModernStyle.Accent;
            string holder = rec.holder + ((rec.comp != null && rec.comp.distributed) ? "  -  copies in circulation" : "");
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(tx, r.yMax - 30f, Mathf.Max(40f, tw - nBtns * 28f - 8f), 24f), holder);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            float bx = r.xMax - 30f;
            if (canBurn)
            {
                var dr = new Rect(bx, r.yMax - 30f, 24f, 24f);
                if (Widgets.ButtonImage(dr, HarassmentTextures.BurnPhoto ?? BaseContent.WhiteTex))
                {
                    rec.photo.Destroy(DestroyMode.Vanish);
                    Refresh();
                }
                TooltipHandler.TipRegion(dr, "Burn this photo");
                bx -= 28f;
            }
            if (target != null)
            {
                var jr = new Rect(bx, r.yMax - 30f, 24f, 24f);
                if (Widgets.ButtonImage(jr, HarassmentTextures.GoTo ?? BaseContent.WhiteTex)) CameraJumper.TryJumpAndSelect(target);
                TooltipHandler.TipRegion(jr, "Jump to");
            }
        }
    }
}
