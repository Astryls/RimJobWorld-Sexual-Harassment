using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// The stylist: a Character-Editor-style appearance editor for a pet - hair style + color, beard, head type,
    /// body type, skin tone, and (with Ideology) face/body tattoos. Changes apply live to a shoulders-up paperdoll
    /// preview; Apply keeps them, Cancel restores the pawn exactly as it was. Modern Suite chrome throughout.
    /// </summary>
    public class Dialog_Stylist : Window
    {
        private readonly Pawn pawn;
        private Vector2 scroll;
        private bool applied;

        // Snapshot for Cancel.
        private readonly HairDef oHair;
        private readonly Color oHairColor;
        private readonly HeadTypeDef oHead;
        private readonly BodyTypeDef oBody;
        private readonly Color? oSkin;
        private readonly BeardDef oBeard;
        private readonly TattooDef oFaceTat;
        private readonly TattooDef oBodyTat;

        private static readonly Color[] HairColors =
        {
            ModernStyle.FromHex(0x1b1b1b), ModernStyle.FromHex(0x3a2417), ModernStyle.FromHex(0x5a3a22),
            ModernStyle.FromHex(0x8a5a34), ModernStyle.FromHex(0xc8a465), ModernStyle.FromHex(0xe6d8a8),
            ModernStyle.FromHex(0x7a3320), ModernStyle.FromHex(0xa83b22), ModernStyle.FromHex(0x9a9a9a),
            ModernStyle.FromHex(0xe8e8e8), ModernStyle.FromHex(0x6a3a8a), ModernStyle.FromHex(0x2a7a7a),
            ModernStyle.FromHex(0xc86a9a),
        };
        private static readonly Color[] SkinColors =
        {
            ModernStyle.FromHex(0xf5d6b8), ModernStyle.FromHex(0xe8c19a), ModernStyle.FromHex(0xd9a878),
            ModernStyle.FromHex(0xb07a4a), ModernStyle.FromHex(0x8a5a34), ModernStyle.FromHex(0x5f3a20),
            ModernStyle.FromHex(0x3a2416),
        };

        public override Vector2 InitialSize => new Vector2(760f, 600f);
        protected override float Margin => 0f;

        public Dialog_Stylist(Pawn pawn)
        {
            this.pawn = pawn;
            forcePause = true;
            draggable = true;
            doCloseX = true;
            doWindowBackground = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;

            oHair = pawn.story?.hairDef;
            oHairColor = pawn.story != null ? pawn.story.HairColor : Color.white;
            oHead = pawn.story?.headType;
            oBody = pawn.story?.bodyType;
            oSkin = pawn.story?.skinColorOverride;
            oBeard = pawn.style?.beardDef;
            oFaceTat = pawn.style?.FaceTattoo;
            oBodyTat = pawn.style?.BodyTattoo;
        }

        private void Refresh()
        {
            try { pawn.Drawer?.renderer?.SetAllGraphicsDirty(); } catch { }
            PortraitsCache.SetDirty(pawn);
        }

        private static bool GrayBtn(Rect r, string label, bool enabled = true)
        {
            Color fill = !enabled ? ModernStyle.PanelBG : Mouse.IsOver(r) ? Color.Lerp(ModernStyle.BGL, ModernStyle.Accent, 0.14f) : ModernStyle.BGL;
            Widgets.DrawBoxSolid(r, fill);
            GUI.color = new Color(0f, 0f, 0f, 0.28f); Widgets.DrawBox(r, 1); GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Tiny;
            GUI.color = enabled ? new Color(0.9f, 0.9f, 0.9f) : ModernStyle.TextDim;
            Widgets.Label(r, (label ?? "").Truncate(r.width - 6f));
            GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
            return enabled && Widgets.ButtonInvisible(r);
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, ModernStyle.BGD);
            GUI.color = ModernStyle.BGL; Widgets.DrawBox(inRect, 1); GUI.color = Color.white;
            var content = inRect.ContractedBy(16f);

            Text.Font = GameFont.Medium; GUI.color = ModernStyle.Body;
            Widgets.Label(new Rect(content.x, content.y, content.width - 30f, 34f), "Stylist - " + pawn.LabelShortCap);
            Text.Font = GameFont.Small; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(content.x, content.y + 32f, content.width, 22f),
                "Restyle hair, face and body. Changes preview live; Apply to keep, Cancel to revert.");
            GUI.color = Color.white;

            const float bottom = 44f, dollW = 240f;
            var bodyRect = new Rect(content.x, content.y + 62f, content.width, content.height - 62f - bottom);
            var listRect = new Rect(bodyRect.x, bodyRect.y, bodyRect.width - dollW - 12f, bodyRect.height);
            DrawList(listRect);
            DrawDoll(new Rect(listRect.xMax + 12f, bodyRect.y, dollW, bodyRect.height));

            float by = content.yMax - 32f;
            if (GrayBtn(new Rect(content.x, by, 150f, 32f), "Randomize look")) Randomize();
            if (GrayBtn(new Rect(content.xMax - 290f, by, 140f, 32f), "Apply")) { applied = true; Refresh(); Close(); }
            if (GrayBtn(new Rect(content.xMax - 140f, by, 140f, 32f), "Cancel")) Close();
        }

        public void DrawList(Rect rect)
        {
            ModernStyle.DrawCard(rect);
            var inner = rect.ContractedBy(8f);
            bool ideo = ModsConfig.IdeologyActive;
            int rows = 6 + (pawn.style != null ? 1 : 0) + (ideo && pawn.style != null ? 2 : 0);
            var view = new Rect(0f, 0f, inner.width - 16f, rows * 34f + 6f);
            ModernStyle.PushScroll();
            Widgets.BeginScrollView(inner, ref scroll, view);
            var col = new Rect(0f, 0f, view.width, view.height);
            float y = 2f;

            if (pawn.story != null)
            {
                y = DefRow(col, y, "Hair", DefDatabase<HairDef>.AllDefsListForReading, pawn.story.hairDef, d => pawn.story.hairDef = d);
                y = ColorRow(col, y, "Hair color", pawn.story.HairColor, HairColors, c => pawn.story.HairColor = c);
                if (pawn.style != null)
                    y = DefRow(col, y, "Beard", DefDatabase<BeardDef>.AllDefsListForReading, pawn.style.beardDef, d => pawn.style.beardDef = d);
                y = DefRow(col, y, "Head", DefDatabase<HeadTypeDef>.AllDefsListForReading.Where(h => h.gender == Gender.None || h.gender == pawn.gender).ToList(), pawn.story.headType, d => pawn.story.headType = d);
                y = DefRow(col, y, "Body", DefDatabase<BodyTypeDef>.AllDefsListForReading.Where(b => b.defName != "Baby" && b.defName != "Child").ToList(), pawn.story.bodyType, d => pawn.story.bodyType = d);
                y = ColorRow(col, y, "Skin", pawn.story.SkinColor, SkinColors, c => pawn.story.skinColorOverride = c);
                if (ideo && pawn.style != null)
                {
                    y = DefRow(col, y, "Face tattoo", DefDatabase<TattooDef>.AllDefsListForReading.Where(t => t.tattooType == TattooType.Face).ToList(), pawn.style.FaceTattoo, d => pawn.style.FaceTattoo = d);
                    y = DefRow(col, y, "Body tattoo", DefDatabase<TattooDef>.AllDefsListForReading.Where(t => t.tattooType == TattooType.Body).ToList(), pawn.style.BodyTattoo, d => pawn.style.BodyTattoo = d);
                }
            }
            Widgets.EndScrollView();
            ModernStyle.PopScroll();
        }

        // A cycle row: label + [<] [current, opens a full picker] [>].
        private float DefRow<T>(Rect inner, float y, string label, List<T> list, T cur, Action<T> set) where T : Def
        {
            var r = new Rect(inner.x, y, inner.width, 30f);
            if (Mouse.IsOver(r)) Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.04f));
            Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(r.x + 2f, r.y, 92f, 30f), label);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            var lb = new Rect(r.x + 96f, r.y + 3f, 24f, 24f);
            var rb = new Rect(r.xMax - 26f, r.y + 3f, 24f, 24f);
            var mid = new Rect(lb.xMax + 3f, r.y + 3f, rb.x - lb.xMax - 6f, 24f);
            if (GrayBtn(lb, "\u25c0")) Step(list, cur, -1, set);
            if (GrayBtn(rb, "\u25b6")) Step(list, cur, 1, set);
            if (GrayBtn(mid, cur != null ? cur.LabelCap.ToString() : "None")) OpenPicker(list, set);
            return y + 34f;
        }

        private void Step<T>(List<T> list, T cur, int dir, Action<T> set) where T : Def
        {
            if (list == null || list.Count == 0) return;
            int i = list.IndexOf(cur);
            i = (i + dir + list.Count) % list.Count;
            if (i < 0) i = 0;
            set(list[i]); Refresh();
        }

        private void OpenPicker<T>(List<T> list, Action<T> set) where T : Def
        {
            if (list == null || list.Count == 0) return;
            var opts = new List<FloatMenuOption>();
            foreach (var d in list)
            {
                var dd = d;
                opts.Add(new FloatMenuOption(d != null ? d.LabelCap.ToString() : "None", () => { set(dd); Refresh(); }));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private float ColorRow(Rect inner, float y, string label, Color cur, Color[] presets, Action<Color> set)
        {
            var r = new Rect(inner.x, y, inner.width, 30f);
            Text.Anchor = TextAnchor.MiddleLeft; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(r.x + 2f, r.y, 92f, 30f), label);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            float sx = r.x + 96f;
            float sw = Mathf.Min(24f, (r.xMax - sx) / presets.Length - 2f);
            for (int i = 0; i < presets.Length; i++)
            {
                var sr = new Rect(sx + i * (sw + 2f), r.y + 4f, sw, 22f);
                Widgets.DrawBoxSolid(sr, presets[i]);
                bool selc = Mathf.Abs(presets[i].r - cur.r) + Mathf.Abs(presets[i].g - cur.g) + Mathf.Abs(presets[i].b - cur.b) < 0.06f;
                GUI.color = selc ? ModernStyle.Accent : new Color(0f, 0f, 0f, 0.4f);
                Widgets.DrawBox(sr, selc ? 2 : 1);
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(sr)) { set(presets[i]); Refresh(); }
            }
            return y + 34f;
        }

        private Portraits.Sizer _dollSizer;
        private void DrawDoll(Rect rect)
        {
            ModernStyle.DrawCard(rect);
            var inner = rect.ContractedBy(8f);
            float s = Mathf.Min(inner.width, inner.height - 24f);
            var portraitRect = new Rect(inner.center.x - s / 2f, inner.y, s, s);
            Widgets.DrawBoxSolid(portraitRect, new Color(0.04f, 0.045f, 0.06f));
            // `s` is derived from the host rect, so it moves whenever the window (or the Command deck this is
            // drawn inline in) is resized - route it through the sizer or every pixel of drag mints a
            // RenderTexture that is never freed. Shoulders-up framing for face/hair editing.
            if (pawn != null && !pawn.Destroyed)
                Portraits.Body(portraitRect, pawn, _dollSizer.Request(new Vector2(s, s)), 1.7f, 0.45f);
            Text.Anchor = TextAnchor.MiddleCenter; GUI.color = ModernStyle.TextDim;
            Widgets.Label(new Rect(inner.x, portraitRect.yMax + 2f, inner.width, 22f), "Preview");
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
        }

        public void Randomize()
        {
            if (pawn.story == null) return;
            var hairs = DefDatabase<HairDef>.AllDefsListForReading;
            if (hairs.Count > 0) pawn.story.hairDef = hairs.RandomElement();
            pawn.story.HairColor = HairColors.RandomElement();
            if (pawn.style != null)
            {
                var beards = DefDatabase<BeardDef>.AllDefsListForReading;
                if (beards.Count > 0) pawn.style.beardDef = beards.RandomElement();
                if (ModsConfig.IdeologyActive)
                {
                    var ft = DefDatabase<TattooDef>.AllDefsListForReading.Where(t => t.tattooType == TattooType.Face).ToList();
                    if (ft.Count > 0) pawn.style.FaceTattoo = ft.RandomElement();
                }
            }
            Refresh();
        }

        public void Commit() { applied = true; }

        public void Revert()
        {
            if (pawn.story != null)
            {
                pawn.story.hairDef = oHair; pawn.story.HairColor = oHairColor;
                pawn.story.headType = oHead; pawn.story.bodyType = oBody;
                pawn.story.skinColorOverride = oSkin;
            }
            if (pawn.style != null)
            {
                pawn.style.beardDef = oBeard; pawn.style.FaceTattoo = oFaceTat; pawn.style.BodyTattoo = oBodyTat;
            }
            Refresh();
        }

        public override void PreClose()
        {
            base.PreClose();
            if (!applied) Revert();
        }
    }
}
