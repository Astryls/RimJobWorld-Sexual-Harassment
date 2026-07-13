using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// A self-contained speech-bubble overlay for text the real bubble mods (Jaxe Bubbles / SpeakUp) refuse to
    /// draw - specifically a CARRIED victim's pleas. A carried pawn is despawned, and those mods gate every frame
    /// on initiator.Spawned, so they can never show it. We draw our own bubble anchored over the carrier (who IS
    /// spawned, and is where the carried victim is rendered). Styling mirrors Jaxe's settings when that mod is
    /// installed so it blends in, with clean defaults otherwise. Only the latest plea per carrier is kept.
    /// </summary>
    public static class DragBubbleOverlay
    {
        private struct Entry { public Pawn anchor; public string text; public int startTick; }

        private static readonly List<Entry> Active = new List<Entry>();
        private const int LifeTicks = 220;
        private const int FadeTicks = 70;

        public static void Push(Pawn anchor, string text)
        {
            if (anchor == null || string.IsNullOrEmpty(text)) return;
            Active.RemoveAll(e => e.anchor == anchor); // one plea per carrier at a time
            Active.Add(new Entry { anchor = anchor, text = text, startTick = Find.TickManager.TicksGame });
        }

        public static void ClearFor(Pawn anchor)
        {
            if (anchor != null) Active.RemoveAll(e => e.anchor == anchor);
        }

        public static void OnGUIFor(Map map)
        {
            if (Active.Count == 0 || map == null) return;
            if (Event.current.type != EventType.Repaint) return;
            ProbeStyle();
            int now = Find.TickManager.TicksGame;
            Color prevColor = GUI.color;
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var e = Active[i];
                if (e.anchor == null || e.anchor.Dead || now - e.startTick > LifeTicks) { Active.RemoveAt(i); continue; }
                if (!e.anchor.Spawned || e.anchor.Map != map) continue;
                DrawOne(e, now);
            }
            GUI.color = prevColor;
            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
        }

        private static void DrawOne(Entry e, int now)
        {
            float age = now - e.startTick;
            float alpha = age <= LifeTicks - FadeTicks ? 1f : Mathf.Clamp01((LifeTicks - age) / (float)FadeTicks);
            if (alpha <= 0.01f) return;

            GUIStyle style = Style();
            var content = new GUIContent(e.text);
            float w = Mathf.Min(style.CalcSize(content).x + 16f, 220f);
            float h = style.CalcHeight(content, w) + 10f;
            Vector2 p = GenMapUI.LabelDrawPosFor(e.anchor, -0.6f);
            var rect = new Rect(p.x - w / 2f, p.y - h - 4f, w, h);

            Widgets.DrawBoxSolid(rect, new Color(_bg.r, _bg.g, _bg.b, _bg.a * alpha));
            GUI.color = new Color(_fg.r, _fg.g, _fg.b, 0.45f * alpha);
            Widgets.DrawBox(rect, 1);
            GUI.color = new Color(_fg.r, _fg.g, _fg.b, alpha);
            GUI.Label(rect, e.text, style);
        }

        // ── style (mirrors Jaxe Bubbles when installed) ──
        private static bool _probed;
        private static int _fontSize = 22;
        private static Color _bg = new Color(0.06f, 0.06f, 0.07f, 0.82f);
        private static Color _fg = Color.white;
        private static GUIStyle _style;

        private static GUIStyle Style()
        {
            if (_style == null || _style.fontSize != _fontSize)
                _style = new GUIStyle(Text.CurFontStyle) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = _fontSize };
            return _style;
        }

        private static void ProbeStyle()
        {
            if (_probed) return;
            _probed = true;
            try
            {
                var t = GenTypes.GetTypeInAnyAssembly("Bubbles.Settings");
                if (t == null) return;
                if (TryReadSetting<int>(t, "FontSize", out var fs) && fs > 0) _fontSize = fs;
                if (TryReadSetting<Color>(t, "Background", out var bg)) _bg = bg;
                if (TryReadSetting<Color>(t, "Foreground", out var fg)) _fg = fg;
            }
            catch { }
        }

        private static bool TryReadSetting<T>(Type settingsType, string fieldName, out T result)
        {
            result = default;
            try
            {
                var f = settingsType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                object setting = f?.GetValue(null);
                if (setting == null) return false;
                Type st = setting.GetType();
                object val = st.GetProperty("Value")?.GetValue(setting) ?? st.GetField("Value")?.GetValue(setting);
                if (val is T tv) { result = tv; return true; }
            }
            catch { }
            return false;
        }
    }
}
