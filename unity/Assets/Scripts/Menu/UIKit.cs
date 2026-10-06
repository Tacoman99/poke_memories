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
        public static bool Tap, Dragging, DragReleased;
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
            TextAnchor anchor = TextAnchor.MiddleCenter, bool wrap = true, FontStyle style = FontStyle.Bold)
        {
            labelStyle ??= new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(size));
            labelStyle.fontStyle = style;
            labelStyle.alignment = anchor;
            labelStyle.wordWrap = wrap;
            labelStyle.clipping = TextClipping.Clip;
            labelStyle.normal.textColor = colour;
            GUI.Label(rect, text, labelStyle);
        }

        public static void Button(Rect rect, string text, Color background, Color foreground, float size)
        {
            var edge = new Rect(rect.x, rect.yMax - rect.height * 0.08f, rect.width, rect.height * 0.08f);
            Fill(new Rect(rect.x, rect.y + 3, rect.width, rect.height), WithAlpha(Color.black, 0.18f));
            Fill(rect, background);
            Fill(edge, WithAlpha(Color.black, 0.12f));
            Label(rect, text, size, foreground);
        }

        /// <summary>Runs `draw` with the GUI rotated about the centre of `around`.</summary>
        public static void Rotated(float degrees, Rect around, System.Action draw)
        {
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(degrees, around.center);
            draw();
            GUI.matrix = matrix;
        }

        public static void DrawTape(Rect polaroid, int index, float scale, float alpha = 0.6f)
        {
            var w = 64 * scale;
            var tape = new Rect(polaroid.center.x - w / 2, polaroid.y - 12 * scale, w, 24 * scale);
            Rotated((index % 3 - 1) * 2f, tape, () => Fill(tape, WithAlpha(Tapes(index), alpha)));
        }

        static Color Tapes(int index) => Tape[index % Tape.Length];
    }
}
