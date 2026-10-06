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

        public void Init(CourseGame owner)
        {
            game = owner;
            book = gameObject.AddComponent<MemoryBook>();
            book.OnBack = ShowMenu;
        }

        public void ShowMenu()
        {
            Screen = GameScreen.Menu;
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

            var s = UIInput.Scale;
            var panel = Panel(s);
            var save = SaveStore.Data;
            UIKit.Fill(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), UIKit.WithAlpha(UIKit.Rose50, 0.35f));
            UIKit.Fill(new Rect(panel.x + 4 * s, panel.y + 8 * s, panel.width, panel.height), UIKit.WithAlpha(Color.black, 0.12f));
            UIKit.Fill(panel, UIKit.WithAlpha(Color.white, 0.92f));
            UIKit.Fill(new Rect(panel.x, panel.y, panel.width, 6 * s), UIKit.Rose400);

            UIKit.Label(new Rect(panel.x, panel.y + 28 * s, panel.width, 30 * s), "A LITTLE SKATE DATE", 16 * s, UIKit.Rose400);
            UIKit.Label(new Rect(panel.x, panel.y + 56 * s, panel.width, 70 * s), "Poke-Memories", 54 * s, UIKit.Rose600);
            UIKit.Label(new Rect(panel.x + 24 * s, panel.y + 126 * s, panel.width - 48 * s, 56 * s),
                "Jump the obstacles. Land on rails.\nBring home memories.", 20 * s, UIKit.Rose500, TextAnchor.MiddleCenter, true, FontStyle.Normal);

            UIKit.Button(Row(panel, s, 196, 68), "Skate the Sunset Course", UIKit.Rose500, Color.white, 28 * s);
            UIKit.Button(Row(panel, s, 280, 58), "Endless skate", UIKit.Violet, Color.white, 24 * s);
            UIKit.Button(Row(panel, s, 352, 58), $"My Memory Book ({save.unlockedMemoryIds.Count} / {MemoryPool.All.Count})", UIKit.Pink100, UIKit.Pink600, 22 * s);

            var hint = SaveStore.AllUnlocked
                ? "Every memory is in your book ♡"
                : $"{SaveStore.BallsToNextMemory} more Pokeballs to your next memory";
            if (!save.courseCompleted) hint += "\nFinish the course once for a bonus memory";
            UIKit.Label(new Rect(panel.x + 24 * s, panel.y + 424 * s, panel.width - 48 * s, 56 * s), hint, 18 * s, UIKit.Rose400, TextAnchor.MiddleCenter, true, FontStyle.Normal);
            if (save.highScore > 0)
                UIKit.Label(new Rect(panel.x, panel.y + 490 * s, panel.width, 30 * s),
                    $"Best haul: {save.highScore}   ({save.totalCollected} Pokeballs collected)", 18 * s, UIKit.Rose500);

            UIKit.Label(new Rect(panel.x + 24 * s, panel.y + 530 * s, panel.width - 48 * s, 50 * s),
                "Tap or press Space to jump · hold for higher · J / K / L for tricks", 15 * s, UIKit.Rose300, TextAnchor.MiddleCenter, true, FontStyle.Normal);
        }
    }
}
