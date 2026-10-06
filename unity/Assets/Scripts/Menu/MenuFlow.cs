using PokeMemories.Gameplay;
using PlayMode = PokeMemories.Gameplay.PlayMode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PokeMemories.Menu
{
    public enum GameScreen { Menu, Playing, Book }

    /// <summary>
    /// The screen state machine: main menu, a run in progress, and the memory book.
    /// While the menu or book is up the course sits idle behind it; CourseGame only
    /// simulates and reads input while the screen is Playing.
    /// </summary>
    public class MenuFlow : MonoBehaviour
    {
        CourseGame game;
        MemoryBook book;

        public GameScreen Screen { get; private set; } = GameScreen.Menu;
        float menuTime;

        public void Init(CourseGame owner)
        {
            game = owner;
            book = gameObject.AddComponent<MemoryBook>();
            book.OnBack = ShowMenu;
        }

        public void ShowMenu()
        {
            Screen = GameScreen.Menu;
            menuTime = 0;
            game.ResetToStart();
        }

        public void StartRun(PlayMode mode)
        {
            Screen = GameScreen.Playing;
            game.BeginRun(mode);
        }

        public void OpenBook(int newMemories = 0)
        {
            Screen = GameScreen.Book;
            book.Open(newMemories);
        }

        // ───────────── Main menu layout (in UIInput.Scale units) ─────────────

        static Rect Panel(float s)
        {
            var w = Mathf.Min(UnityEngine.Screen.width * 0.92f, 600 * s);
            var h = 600 * s;
            return new Rect((UnityEngine.Screen.width - w) / 2, (UnityEngine.Screen.height - h) / 2, w, h);
        }

        static Rect Row(Rect panel, float s, float y, float height) =>
            new(panel.x + 36 * s, panel.y + y * s, panel.width - 72 * s, height * s);

        void Update()
        {
            UIInput.Update();
            menuTime += Time.unscaledDeltaTime;
            if (Screen == GameScreen.Book)
            {
                book.Tick();
                return;
            }
            if (Screen != GameScreen.Menu) return;

            var s = UIInput.Scale;
            var panel = Panel(s);
            if (UIInput.TapIn(Row(panel, s, 196, 68))) StartRun(PlayMode.Course);
            else if (UIInput.TapIn(Row(panel, s, 280, 58))) StartRun(PlayMode.Endless);
            else if (UIInput.TapIn(Row(panel, s, 352, 58))) OpenBook();
        }

        void OnGUI()
        {
            if (Screen == GameScreen.Book)
            {
                book.Draw();
                return;
            }
            if (Screen != GameScreen.Menu || Event.current.type != EventType.Repaint) return;
            Look.Ensure();

            var s = UIInput.Scale;
            var panel = Panel(s);
            var save = SaveStore.Data;
            var W = UnityEngine.Screen.width;
            var H = UnityEngine.Screen.height;

            // Rose wash over the course, a warm vignette, and a few drifting sparkles.
            UIKit.Fill(new Rect(0, 0, W, H), UIKit.WithAlpha(UIKit.Hex("#fda4af"), 0.22f));
            Look.Tex(new Rect(-W * 0.2f, -H * 0.3f, W * 1.4f, H * 1.6f), Look.Vignette, UIKit.WithAlpha(UIKit.Hex("#9f1239"), 0.0f));
            Look.FadeUp(new Rect(0, H * 0.55f, W, H * 0.45f), UIKit.WithAlpha(UIKit.Hex("#881337"), 0.35f));
            for (var i = 0; i < 14; i++)
            {
                var seed = i * 7.31f;
                var x = (Mathf.Repeat(seed * 53f + Time.unscaledTime * (6 + i % 4 * 3), W + 80) - 40);
                var y = H * (0.1f + 0.75f * Mathf.Repeat(seed * 0.173f, 1)) + Mathf.Sin(Time.unscaledTime * 0.8f + seed) * 14 * s;
                var size = (14 + i % 3 * 8) * s * (0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 2 + seed));
                Look.Tex(new Rect(x - size / 2, y - size / 2, size, size), Look.Sparkle, UIKit.WithAlpha(Color.white, 0.5f));
            }

            // The card settles in with a little overshoot.
            var enter = Look.EaseOutBack(menuTime / 0.55f);
            var fade = Mathf.Clamp01(menuTime / 0.25f);
            var card = new Rect(panel.x, panel.y + (1 - enter) * 60 * s, panel.width, panel.height);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, fade);
            var m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one * (0.94f + 0.06f * enter), card.center);

            Look.Shadow(card, 28 * s, 0.34f, new Vector2(0, 16 * s));
            Look.Round(card, UIKit.Hex("#fffaf4"), 22 * s);
            GUI.BeginClip(card);
            var inner = new Rect(0, 0, card.width, card.height);
            Look.Tiled(inner, Look.Paper, 512 * s * 0.8f, UIKit.Hex("#fff3ec"));
            Look.FadeDown(new Rect(0, 0, card.width, 120 * s), UIKit.WithAlpha(UIKit.Hex("#fda4af"), 0.25f));
            GUI.EndClip();
            Look.RoundOutline(card, UIKit.WithAlpha(Color.white, 0.8f), 22 * s, 2);
            Look.RoundOutline(new Rect(card.x + 12 * s, card.y + 12 * s, card.width - 24 * s, card.height - 24 * s), UIKit.WithAlpha(UIKit.Rose300, 0.55f), 14 * s, 1.5f * s);

            // Washi tape pinning the card to the wall.
            var tapeW = 110 * s;
            foreach (var side in new[] { -1, 1 })
            {
                var t = new Rect(card.center.x + side * (card.width / 2 - 40 * s) - tapeW / 2, card.y - 10 * s, tapeW, 34 * s);
                UIKit.Rotated(-side * 34f, t, () =>
                {
                    Look.Shadow(t, 3 * s, 0.18f, new Vector2(0, 2 * s));
                    Look.Tex(t, Look.Washi[side < 0 ? 1 : 4], Color.white);
                });
            }

            UIKit.Label(new Rect(card.x, card.y + 34 * s, card.width, 30 * s), "~ a little skate date ~", 20 * s, UIKit.Rose400, TextAnchor.MiddleCenter, true, FontStyle.Normal);
            UIKit.TitleLabel(new Rect(card.x, card.y + 62 * s, card.width, 82 * s), "Poke-Memories", 60 * s, UIKit.Rose600, UIKit.WithAlpha(UIKit.Rose700, 0.22f), 3 * s);
            var bob = Mathf.Sin(Time.unscaledTime * 2.2f) * 3 * s;
            Look.HeartAt(new Vector2(card.xMax - 70 * s, card.y + 72 * s + bob), 26 * s, UIKit.Rose400);
            Look.HeartAt(new Vector2(card.x + 62 * s, card.y + 118 * s - bob), 18 * s, UIKit.Rose300);
            UIKit.Label(new Rect(card.x + 24 * s, card.y + 138 * s, card.width - 48 * s, 56 * s),
                "Jump the obstacles. Land on rails.\nBring home memories.", 20 * s, UIKit.Rose500, TextAnchor.MiddleCenter, true, FontStyle.Normal);

            Stagger(0, () => UIKit.Button(Row(card, s, 196, 68), "Skate the Sunset Course", UIKit.Rose500, Color.white, 28 * s), s);
            Stagger(1, () => UIKit.Button(Row(card, s, 280, 58), "Endless skate", UIKit.Violet, Color.white, 24 * s), s);
            Stagger(2, () => UIKit.Button(Row(card, s, 352, 58), $"My Memory Book ({save.unlockedMemoryIds.Count} / {MemoryPool.All.Count})", UIKit.Pink100, UIKit.Pink600, 22 * s), s);

            var hint = SaveStore.AllUnlocked
                ? "Every memory is in your book ♡"
                : $"{SaveStore.BallsToNextMemory} more Pokeballs to your next memory";
            if (!save.courseCompleted) hint += "\nFinish the course once for a bonus memory";
            UIKit.Label(new Rect(card.x + 24 * s, card.y + 424 * s, card.width - 48 * s, 56 * s), hint, 19 * s, UIKit.Rose400, TextAnchor.MiddleCenter, true, FontStyle.Normal);
            if (save.highScore > 0)
                UIKit.Label(new Rect(card.x, card.y + 490 * s, card.width, 30 * s),
                    $"Best haul: {save.highScore}   ({save.totalCollected} Pokeballs collected)", 19 * s, UIKit.Rose500);

            UIKit.Label(new Rect(card.x + 24 * s, card.y + 530 * s, card.width - 48 * s, 50 * s),
                "Tap or press Space to jump · hold for higher · J / K / L for tricks", 16 * s, UIKit.Rose300, TextAnchor.MiddleCenter, true, FontStyle.Normal);

            GUI.matrix = m;
            GUI.color = old;
        }

        // Buttons rise into place one after another.
        void Stagger(int index, System.Action draw, float s)
        {
            var t = Look.EaseOutBack((menuTime - 0.18f - index * 0.09f) / 0.45f);
            var a = Mathf.Clamp01((menuTime - 0.18f - index * 0.09f) / 0.2f);
            var m = GUI.matrix;
            var oldColor = GUI.color;
            GUI.color = new Color(oldColor.r, oldColor.g, oldColor.b, oldColor.a * a);
            GUI.matrix = m * Matrix4x4.Translate(new Vector3(0, (1 - t) * 24 * s, 0));
            draw();
            GUI.matrix = m;
            GUI.color = oldColor;
        }
    }
}
