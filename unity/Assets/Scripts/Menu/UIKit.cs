using UnityEngine;
using UnityEngine.InputSystem;

namespace PokeMemories.Menu
{
    /// <summary>
    /// Pointer input for the menu screens, polled once per frame. Positions are in GUI
    /// space (origin top-left). A tap is a press that was released without dragging.
    /// </summary>
    public static class UIInput
    {
        public static Vector2 Position;
        public static bool Tap, Dragging, DragReleased, Pressed;
        public static Vector2 TapPosition, DragDelta, DragTotal;
        public static float Wheel;

        static bool tracking;
        static Vector2 last, start;
        static float travelled;

        /// <summary>UI scale: 1 at 720 tall in landscape, limited by width so portrait phones fit.</summary>
        public static float Scale => Mathf.Min(Screen.height / 720f, Screen.width / 540f);

        public static void Update()
        {
            Tap = false;
            DragReleased = false;
            Dragging = false;
            DragDelta = Vector2.zero;
            Wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0;

            var pointer = Pointer.current;
            Pressed = pointer != null && pointer.press.isPressed;
            if (pointer == null) return;
            var raw = pointer.position.ReadValue();
            Position = new Vector2(raw.x, Screen.height - raw.y);

            if (pointer.press.wasPressedThisFrame)
            {
                tracking = true;
                travelled = 0;
                last = Position;
                start = Position;
            }
            else if (tracking && pointer.press.isPressed)
            {
                DragDelta = Position - last;
                travelled += DragDelta.magnitude;
                last = Position;
                Dragging = travelled > 14 * Scale;
                if (!Dragging) DragDelta = Vector2.zero;
            }
            if (tracking && pointer.press.wasReleasedThisFrame)
            {
                tracking = false;
                if (travelled <= 14 * Scale) { Tap = true; TapPosition = Position; }
                else { DragReleased = true; DragTotal = Position - start; }
            }
        }

        public static bool TapIn(Rect rect) => Tap && rect.Contains(TapPosition);
    }

    /// <summary>Small immediate-mode drawing helpers shared by the menu and the memory book.</summary>
    public static class UIKit
    {
        public static readonly Color Paper = Hex("#fef7f0"), Rose50 = Hex("#fff1f2"), Rose300 = Hex("#fda4af"),
            Rose400 = Hex("#fb7185"), Rose500 = Hex("#f43f5e"), Rose600 = Hex("#e11d48"), Rose700 = Hex("#be123c"),
            Violet = Hex("#8b5cf6"), Pink100 = Hex("#fce7f3"), Pink600 = Hex("#db2777"), Ink = Hex("#881337");
        public static readonly Color[] Tape = { Hex("#f59e0b"), Hex("#ec4899"), Hex("#3b82f6"), Hex("#34d399"), Hex("#a78bfa") };

        static GUIStyle labelStyle;

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var colour);
            return colour;
        }

        public static Color WithAlpha(Color colour, float alpha) => new(colour.r, colour.g, colour.b, alpha);

        public static void Fill(Rect rect, Color colour)
        {
            var old = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static void Label(Rect rect, string text, float size, Color colour,
            TextAnchor anchor = TextAnchor.MiddleCenter, bool wrap = true, FontStyle style = FontStyle.Bold, Font font = null)
        {
            Look.Ensure();
            labelStyle ??= new GUIStyle(GUI.skin.label);
            labelStyle.font = font != null ? font : style == FontStyle.Bold ? Look.HandBold : Look.Hand;
            labelStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(size));
            labelStyle.fontStyle = FontStyle.Normal;
            labelStyle.alignment = anchor;
            labelStyle.wordWrap = wrap;
            labelStyle.clipping = TextClipping.Clip;
            labelStyle.normal.textColor = colour;
            GUI.Label(rect, text, labelStyle);
        }

        /// <summary>Label with a soft offset shadow, for text that sits on busy backgrounds.</summary>
        public static void ShadowLabel(Rect rect, string text, float size, Color colour, Color shadow, float offset,
            TextAnchor anchor = TextAnchor.MiddleCenter, bool wrap = true, FontStyle style = FontStyle.Bold, Font font = null)
        {
            Label(new Rect(rect.x + offset, rect.y + offset * 1.4f, rect.width, rect.height), text, size, shadow, anchor, wrap, style, font);
            Label(rect, text, size, colour, anchor, wrap, style, font);
        }

        /// <summary>The script face used for titles, with a drop shadow.</summary>
        public static void TitleLabel(Rect rect, string text, float size, Color colour, Color shadow, float offset)
        {
            Look.Ensure();
            ShadowLabel(rect, text, size, colour, shadow, offset, TextAnchor.MiddleCenter, false, FontStyle.Normal, Look.Title);
        }

        /// <summary>A rounded pill button with a soft shadow, gloss and a springy hover/press response.</summary>
        public static void Button(Rect rect, string text, Color background, Color foreground, float size)
        {
            Look.Ensure();
            var hovered = rect.Contains(UIInput.Position);
            var down = hovered && UIInput.Pressed;
            var lift = Look.Spring("hov" + text, hovered ? 1 : 0, 16);
            var press = Look.Spring("prs" + text, down ? 1 : 0, 30);
            var grow = 1 + lift * 0.025f - press * 0.04f;
            var r = new Rect(rect.center.x - rect.width * grow / 2, rect.center.y - rect.height * grow / 2 + press * 2, rect.width * grow, rect.height * grow);
            var radius = r.height * 0.5f;

            Look.Shadow(r, 10 * (1 - press * 0.4f), 0.28f - press * 0.1f, new Vector2(0, 6 - press * 3));
            Look.Round(new Rect(r.x, r.y + 3, r.width, r.height), Color.Lerp(background, Color.black, 0.32f), radius);
            var face = new Rect(r.x, r.y, r.width, r.height - 3 + press * 2);
            Look.Round(face, Color.Lerp(background, Color.white, lift * 0.08f), radius);
            var gloss = new Rect(r.x + r.height * 0.3f, r.y + 2, r.width - r.height * 0.6f, face.height * 0.46f);
            Look.Round(gloss, WithAlpha(Color.white, 0.2f), gloss.height / 2);
            Look.RoundOutline(face, WithAlpha(Color.white, 0.25f), radius, 1.5f);
            var textRect = new Rect(face.x, face.y - 1, face.width, face.height);
            Label(new Rect(textRect.x, textRect.y + 1.5f, textRect.width, textRect.height), text, size, WithAlpha(Color.black, 0.18f));
            Label(textRect, text, size, foreground);
        }

        /// <summary>Runs `draw` with the GUI rotated about the centre of `around`.</summary>
        public static void Rotated(float degrees, Rect around, System.Action draw)
        {
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(degrees, around.center);
            draw();
            GUI.matrix = matrix;
        }

        /// <summary>A strip of washi tape across the top edge of `polaroid`.</summary>
        public static void DrawTape(Rect polaroid, int index, float scale, float alpha = 0.6f)
        {
            Look.Ensure();
            var w = 78 * scale;
            var tape = new Rect(polaroid.center.x - w / 2, polaroid.y - 13 * scale, w, 26 * scale);
            var tex = Look.Washi[index % Look.Washi.Length];
            Rotated((index % 5 - 2) * 2.4f, tape, () =>
            {
                Look.Shadow(tape, 3 * scale, 0.16f, new Vector2(0, 1.5f * scale));
                Look.Tex(tape, tex, Color.white);
            });
        }

        static Color Tapes(int index) => Tape[index % Tape.Length];
    }
}
