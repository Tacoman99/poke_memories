using System.Text;
using PokeMemories.Menu;
using UnityEngine;

namespace PokeMemories.Gameplay
{
    // Immediate-mode HUD, intro card and result card for the bowl, drawn with the same paper, washi
    // tape, script fonts and frosted-rose capsules as the rest of the game.
    public partial class BowlGame
    {
        static readonly Color Cream = new(1f, 0.96f, 0.93f);
        static readonly Color[] MedalColours = { new(0.8f, 0.5f, 0.2f), new(0.78f, 0.82f, 0.88f), new(0.96f, 0.77f, 0.26f) };
        float shownScore;

        // Web copy carries emoji the bundled fonts don't have; keep letters, digits and basic punctuation.
        static string Clean(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (ch == '…') sb.Append("...");
                else if (ch < 0x250 || ch == '·' || ch == '×') sb.Append(ch);
            }
            return sb.ToString().Trim();
        }

        static void Message(Rect rect, string text, float size, Font font = null) =>
            UIKit.ShadowLabel(rect, text, size, UIKit.Hex("#be123c"), UIKit.WithAlpha(UIKit.Hex("#fff3ec"), 0.95f), Mathf.Max(2f, size / 12f), TextAnchor.MiddleCenter, false, FontStyle.Bold, font);

        static void Glass(Rect r, float alpha = 0.5f)
        {
            Look.Shadow(r, 8 * (r.height / 56f), 0.25f, new Vector2(0, 4));
            Look.Round(r, UIKit.WithAlpha(UIKit.Hex("#6b1233"), alpha), r.height / 2);
            Look.Round(new Rect(r.x + r.height * 0.25f, r.y + 2, r.width - r.height * 0.5f, r.height * 0.42f), UIKit.WithAlpha(Color.white, 0.12f), r.height * 0.21f);
            Look.RoundOutline(r, UIKit.WithAlpha(Color.white, 0.35f), r.height / 2, 1.5f);
        }

        static void GlassButton(Rect r, string text, float size, Color tint, bool on, string key, string sub = null)
        {
            var over = r.Contains(UIInput.Position);
            var down = over && UIInput.Pressed;
            var press = Look.Spring("bg" + key, down ? 1 : 0, 30);
            var rect = new Rect(r.x + r.width * 0.02f * press, r.y + r.height * 0.02f * press, r.width * (1 - 0.04f * press), r.height * (1 - 0.04f * press));
            var fill = on ? UIKit.WithAlpha(tint, 0.9f) : UIKit.WithAlpha(Color.Lerp(UIKit.Hex("#6b1233"), tint, 0.25f), 0.55f + press * 0.2f);
            Look.Shadow(rect, 8, 0.25f, new Vector2(0, 4 - press * 2));
            Look.Round(rect, fill, rect.height * 0.5f);
            Look.Round(new Rect(rect.x + rect.height * 0.2f, rect.y + 2, rect.width - rect.height * 0.4f, rect.height * 0.42f), UIKit.WithAlpha(Color.white, 0.14f), rect.height * 0.21f);
            Look.RoundOutline(rect, UIKit.WithAlpha(Color.white, 0.4f), rect.height * 0.5f, 1.5f);
            if (sub == null) UIKit.Label(rect, text, size, Cream);
            else
            {
                UIKit.Label(new Rect(rect.x, rect.y + rect.height * 0.1f, rect.width, rect.height * 0.55f), text, size, Cream);
                UIKit.Label(new Rect(rect.x, rect.y + rect.height * 0.55f, rect.width, rect.height * 0.35f), sub, size * 0.62f, UIKit.WithAlpha(Cream, 0.8f));
            }
        }

        static void Chip(Rect r, string text, float size, Color fill)
        {
            Look.Shadow(r, 5, 0.22f, new Vector2(0, 3));
            Look.Round(r, UIKit.WithAlpha(fill, 0.92f), r.height / 2);
            Look.Round(new Rect(r.x + r.height * 0.2f, r.y + 1.5f, r.width - r.height * 0.4f, r.height * 0.4f), UIKit.WithAlpha(Color.white, 0.2f), r.height * 0.2f);
            UIKit.Label(r, text, size, Color.white);
        }

        // A little rosette: coloured disc, ring, sparkle, and two ribbon tails.
        static void Medal(Vector2 centre, float size, int tier, float spin = 0)
        {
            var colour = MedalColours[Mathf.Clamp(tier - 1, 0, 2)];
            foreach (var side in new[] { -1, 1 })
            {
                var tail = new Rect(centre.x + side * size * 0.2f - size * 0.12f, centre.y + size * 0.25f, size * 0.24f, size * 0.55f);
                UIKit.Rotated(side * 14, tail, () => Look.Round(tail, Color.Lerp(UIKit.Hex("#fb7185"), Color.white, side > 0 ? 0 : 0.15f), size * 0.04f));
            }
            var disc = new Rect(centre.x - size / 2, centre.y - size / 2, size, size);
            Look.Shadow(disc, size * 0.12f, 0.35f, new Vector2(0, size * 0.06f));
            Look.Round(disc, Color.Lerp(colour, Color.black, 0.3f), size);
            var face = new Rect(disc.x + size * 0.05f, disc.y + size * 0.03f, size * 0.9f, size * 0.9f);
            Look.Round(face, colour, size);
            Look.Round(new Rect(face.x + size * 0.14f, face.y + size * 0.1f, size * 0.62f, size * 0.62f), Color.Lerp(colour, Color.white, 0.25f), size);
            Look.RoundOutline(disc, UIKit.WithAlpha(Color.white, 0.6f), size, 2);
            var s = size * 0.5f * (1 + 0.06f * Mathf.Sin(Time.unscaledTime * 3 + spin));
            Look.Tex(new Rect(centre.x - s / 2, centre.y - s / 2, s, s), Look.Sparkle, Color.white);
        }

        Rect ResultCard(float s)
        {
            var w = Mathf.Min(Screen.width * 0.92f, 700 * s);
            var h = Mathf.Min(Screen.height * 0.9f, 470 * s);
            return new Rect((Screen.width - w) / 2, Mathf.Max(8 * s, (Screen.height - h) / 2 - 10 * s), w, h);
        }

        Rect IntroCard(float s)
        {
            var w = Mathf.Min(Screen.width * 0.92f, 700 * s);
            var h = Mathf.Min(Screen.height * 0.78f, 430 * s);
            return new Rect((Screen.width - w) / 2, Mathf.Max(8 * s, (Screen.height - h) / 2 - 24 * s), w, h);
        }

        void LayoutResult(float s)
        {
            var card = ResultCard(s);
            var gap = 12 * s;
            var half = (card.width - 80 * s - gap) / 2;
            againButton = new Rect(card.x + 40 * s, card.yMax - 78 * s, half, 58 * s);
            resultMenuButton = new Rect(againButton.xMax + gap, againButton.y, half, 58 * s);
            bookButton = lastReward is { Earned: > 0 }
                ? new Rect(card.x + 40 * s, againButton.y - 70 * s, card.width - 80 * s, 58 * s)
                : Rect.zero;
        }

        void OnGUI()
        {
            if (!active || menu.Screen != GameScreen.Bowl) return;
            if (Event.current.type != EventType.Repaint) return;
            Look.Ensure();
            Layout();
            var s = UIInput.Scale;
            float W = Screen.width, H = Screen.height;
            var t = Time.unscaledTime;
            shownScore = Mathf.MoveTowards(shownScore, sim.Score, Mathf.Max(60f, Mathf.Abs(sim.Score - shownScore) * 6f) * Time.unscaledDeltaTime);
            if (sim.Elapsed < 0.01f) shownScore = 0;

            DrawScoreboard(s, t);

            // Centre messages: grade flash, trick banner, then the plain feedback line.
            if (started && !sim.Ended && !paused)
            {
                if (sim.GradeFlashTime > 0 && sim.GradeFlash != LipState.None)
                {
                    var pop = Look.EaseOutBack(1 - Mathf.Clamp01(sim.GradeFlashTime / 0.9f) + 0.3f);
                    var label = sim.GradeFlash == LipState.Perfect ? "PERFECT!" : sim.GradeFlash == LipState.Good ? "Good!" : "Miss...";
                    var colour = sim.GradeFlash == LipState.Perfect ? UIKit.Hex("#10b981") : sim.GradeFlash == LipState.Good ? UIKit.Hex("#d97706") : UIKit.Hex("#be123c");
                    var centre = new Vector2(W / 2, H * 0.145f);
                    var m = GUI.matrix;
                    GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.Lerp(0.8f, 1.05f, Mathf.Clamp01(pop)), centre);
                    UIKit.ShadowLabel(new Rect(0, centre.y - 40 * s, W, 80 * s), label, 64 * s, colour, UIKit.WithAlpha(UIKit.Hex("#fff3ec"), 0.97f), 4 * s, TextAnchor.MiddleCenter, false, FontStyle.Normal, Look.Title);
                    GUI.matrix = m;
                }
                if (sim.Trick is { } trick)
                {
                    var text = BowlSimulation.TrickInfo(trick).name + "!";
                    if (sim.QueuedTrick is { } queued) text += "  >  " + BowlSimulation.TrickInfo(queued).name;
                    if (sim.AirChain > 0) text += $"   Chain x{sim.AirChain + 1}";
                    var width = Mathf.Min(W * 0.9f, (text.Length * 15 + 80) * s);
                    var pill = new Rect((W - width) / 2, H * 0.235f, width, 46 * s);
                    Chip(pill, text, 24 * s, UIKit.Hex("#e11d48"));
                }
                if (sim.FeedbackTime > 0)
                {
                    var text = Clean(sim.Feedback);
                    if (text.Length > 0) Message(new Rect(0, H * 0.30f, W, 50 * s), text, 30 * s);
                }
            }

            // Buttons.
            GlassButton(autoButton, autoPlay ? "Auto: ON (A)" : "Auto: off (A)", 20 * s, UIKit.Hex("#f43f5e"), autoPlay, "auto");
            GlassButton(menuButton, "Menu (Esc)", 20 * s, UIKit.Hex("#6b1233"), false, "menu");
            if (started && !sim.Ended)
            {
                var air = sim.Airborne;
                for (var i = 0; i < 4; i++)
                    GlassButton(trickButtons[i], TrickInputs[i].label, 24 * s, UIKit.Hex("#e11d48"), air, "t" + i, TrickInputs[i].key.ToString());
                var armed = sim.LipArmed is LipState.Good or LipState.Perfect;
                var near = sim.TimeToCoping() <= BowlSimulation.LipGoodWindow + 0.2f;
                GlassButton(lipButton, "Lip Handstand (U)", 22 * s, UIKit.Hex("#10b981"), armed || near, "lip");

                // Pump hint, bottom-left.
                var hint = new Rect(16 * s, H - 14 * s - 54 * s, 300 * s, 54 * s);
                Glass(hint, sim.Pumping ? 0.75f : 0.5f);
                if (sim.Pumping) Look.Round(hint, UIKit.WithAlpha(UIKit.Hex("#f43f5e"), 0.35f), hint.height / 2);
                UIKit.Label(hint, sim.Pumping ? "Pumping! build speed" : (sim.Elapsed < 12 ? "Hold anywhere to pump" : "Hold to pump"), 22 * s, Cream);
            }

            if (!started && !sim.Ended) DrawIntro(s, t);
            if (paused) DrawPaused(s);
            if (sim.Ended && resultTime > 0.8f) DrawResult(s, t);
            else if (sim.Ended)
                Message(new Rect(0, H * 0.28f, W, 90 * s), "Time!", 80 * s, Look.Title);
        }

        void DrawScoreboard(float s, float t)
        {
            var hud = new Rect(16 * s, 14 * s, 340 * s, 56 * s);
            Glass(hud);
            var seconds = Mathf.CeilToInt(sim.TimeLeft);
            var low = started && seconds <= 10 && !sim.Ended;
            var pulse = low ? 0.5f + 0.5f * Mathf.Sin(t * 8f) : 0;
            var clock = new Rect(hud.x + 14 * s, hud.y + 12 * s, 32 * s, 32 * s);
            Look.Round(clock, Color.Lerp(Cream, UIKit.Hex("#ff6b8f"), pulse), clock.height);
            Look.Round(new Rect(clock.x + 3 * s, clock.y + 3 * s, clock.width - 6 * s, clock.height - 6 * s), UIKit.WithAlpha(UIKit.Hex("#6b1233"), 0.85f), clock.height);
            UIKit.Rotated(sim.Elapsed * 6f, new Rect(clock.center.x - 1, clock.center.y - 1, 2, 2), () => UIKit.Fill(new Rect(clock.center.x - 1.5f * s, clock.center.y - 10 * s, 3 * s, 10 * s), Cream));
            UIKit.Label(new Rect(clock.xMax + 6 * s, hud.y, 62 * s, hud.height), $"{seconds / 60}:{seconds % 60:00}", 28 * s, low ? Color.Lerp(Cream, UIKit.Hex("#ffb4c4"), pulse) : Cream, TextAnchor.MiddleLeft);
            var star = new Rect(hud.x + 150 * s, hud.y + 14 * s, 28 * s, 28 * s);
            Look.Tex(star, Look.Sparkle, UIKit.Hex("#ffd36b"));
            UIKit.Label(new Rect(star.xMax + 6 * s, hud.y, 150 * s, hud.height), $"{Mathf.RoundToInt(shownScore)}", 30 * s, Cream, TextAnchor.MiddleLeft);

            // Medal bar: three equal stretches, one per medal, with the medal at the end of each.
            var bar = new Rect(hud.x + 18 * s, hud.yMax + 14 * s, hud.width - 56 * s, 14 * s);
            Look.Shadow(bar, 4 * s, 0.25f, new Vector2(0, 2));
            Look.Round(bar, UIKit.WithAlpha(UIKit.Hex("#6b1233"), 0.55f), bar.height / 2);
            var fraction = MedalFraction(sim.Score);
            if (fraction > 0.005f)
            {
                var fill = new Rect(bar.x, bar.y, Mathf.Max(bar.height, bar.width * fraction), bar.height);
                Look.Round(fill, UIKit.Hex("#ff7f9c"), bar.height / 2);
                Look.Round(new Rect(fill.x + 3, fill.y + 1.5f, fill.width - 6, bar.height * 0.35f), UIKit.WithAlpha(Color.white, 0.4f), bar.height * 0.17f);
            }
            for (var i = 0; i < BowlSimulation.Goals.Length; i++)
            {
                var got = sim.Score >= BowlSimulation.Goals[i].score;
                var c = new Vector2(bar.x + bar.width * (i + 1) / 3f, bar.center.y);
                Look.Round(new Rect(c.x - 15 * s, c.y - 15 * s, 30 * s, 30 * s), UIKit.WithAlpha(UIKit.Hex("#6b1233"), 0.9f), 30 * s);
                var m = MedalColours[i];
                Look.Round(new Rect(c.x - 12 * s, c.y - 12 * s, 24 * s, 24 * s), got ? m : UIKit.WithAlpha(m, 0.35f), 24 * s);
                if (got) Look.Tex(new Rect(c.x - 9 * s, c.y - 9 * s, 18 * s, 18 * s), Look.Sparkle, Color.white);
                UIKit.Label(new Rect(c.x - 40 * s, c.y + 16 * s, 80 * s, 20 * s), BowlSimulation.Goals[i].score.ToString(), 15 * s, UIKit.WithAlpha(Cream, got ? 1f : 0.7f));
            }

            // Chips: combo, perfect streak, bruises.
            var chipY = bar.yMax + 44 * s;
            var x = hud.x;
            if (sim.Combo > 0) { Chip(new Rect(x, chipY, 150 * s, 36 * s), $"Combo x{Mathf.Min(5, sim.Combo)}", 22 * s, UIKit.Hex("#e11d48")); x += 160 * s; }
            if (sim.Streak > 0) { Chip(new Rect(x, chipY, 190 * s, 36 * s), $"Perfect streak x{sim.Streak}", 20 * s, UIKit.Hex("#059669")); x += 200 * s; }
            if (sim.Bruises > 0) Chip(new Rect(x, chipY, 130 * s, 36 * s), $"Bruises {sim.Bruises}", 20 * s, UIKit.Hex("#7e22ce"));
        }

        static float MedalFraction(float score)
        {
            var goals = BowlSimulation.Goals;
            var floor = 0f;
            for (var i = 0; i < goals.Length; i++)
            {
                if (score < goals[i].score) return (i + Mathf.Clamp01((score - floor) / (goals[i].score - floor))) / goals.Length;
                floor = goals[i].score;
            }
            return 1f;
        }

        // A taped sheet of paper, same construction as the run-end card on the course.
        static void Paper(Rect card, float s, float tilt, int tapeIndex)
        {
            UIKit.Rotated(tilt, card, () =>
            {
                Look.Shadow(card, 26 * s, 0.4f, new Vector2(0, 14 * s));
                Look.Round(card, UIKit.Hex("#fffaf4"), 18 * s);
                GUI.BeginClip(card);
                Look.Tiled(new Rect(0, 0, card.width, card.height), Look.Paper, 400 * s, UIKit.Hex("#fff3ec"));
                Look.FadeDown(new Rect(0, 0, card.width, 90 * s), UIKit.WithAlpha(UIKit.Hex("#fda4af"), 0.25f));
                GUI.EndClip();
                Look.RoundOutline(new Rect(card.x + 10 * s, card.y + 10 * s, card.width - 20 * s, card.height - 20 * s), UIKit.WithAlpha(UIKit.Rose300, 0.55f), 12 * s, 1.5f * s);
                UIKit.DrawTape(new Rect(card.center.x - 38 * s, card.y + 10 * s, 76 * s, 1), tapeIndex, s * 1.3f);
            });
        }

        void DrawIntro(float s, float t)
        {
            var enter = Look.EaseOutBack(Mathf.Clamp01((t - introStart) / 0.5f));
            var card = IntroCard(s);
            card.y += (1 - enter) * 40 * s;
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01((t - introStart) / 0.2f));
            Paper(card, s, -0.8f, 1);
            UIKit.Rotated(-0.8f, card, () =>
            {
                UIKit.Label(new Rect(card.x, card.y + 22 * s, card.width, 80 * s), "Rose Bowl", 56 * s, UIKit.Rose600, TextAnchor.MiddleCenter, false, FontStyle.Normal, Look.Title);
                UIKit.Label(new Rect(card.x + 30 * s, card.y + 98 * s, card.width - 60 * s, 34 * s), "60 seconds to carve, air out and land your tricks", 22 * s, UIKit.Rose500);
                var lines = new (string head, string body)[]
                {
                    ("Pump", "hold anywhere (or Space) to build speed until she airs out of the coping"),
                    ("Tricks", "in the air tap Grab, Kick, Spin or Flip (J K L I). Chain a few, then land clean"),
                    ("Handstand", "press U just as she reaches the lip. Mistime it, or land mid trick, and she falls"),
                };
                for (var i = 0; i < lines.Length; i++)
                {
                    var y = card.y + 142 * s + i * 62 * s;
                    UIKit.Label(new Rect(card.x + 36 * s, y, 130 * s, 54 * s), lines[i].head, 24 * s, UIKit.Rose600, TextAnchor.MiddleLeft);
                    UIKit.Label(new Rect(card.x + 166 * s, y, card.width - 202 * s, 54 * s), lines[i].body, 19 * s, UIKit.Ink, TextAnchor.MiddleLeft, true, FontStyle.Normal);
                }
                // Medal targets.
                for (var i = 0; i < 3; i++)
                {
                    var c = new Vector2(card.x + card.width * (0.22f + i * 0.28f), card.yMax - 78 * s);
                    Medal(c, 34 * s, i + 1, i);
                    UIKit.Label(new Rect(c.x + 22 * s, c.y - 16 * s, 110 * s, 32 * s), BowlSimulation.Goals[i].score.ToString(), 20 * s, UIKit.Rose600, TextAnchor.MiddleLeft);
                }
                var bob = Mathf.Sin(t * 2.4f) * 3 * s;
                UIKit.Label(new Rect(card.x, card.yMax - 44 * s + bob, card.width, 34 * s), "Hold to drop in", 28 * s, UIKit.Rose500);
            });
            GUI.color = old;
        }

        float introStart;

        void DrawPaused(float s)
        {
            Message(new Rect(0, Screen.height * 0.4f, Screen.width, 80 * s), "Paused", 64 * s, Look.Title);
            Message(new Rect(0, Screen.height * 0.4f + 70 * s, Screen.width, 40 * s), "Tap or hold to keep skating", 26 * s);
        }

        void DrawResult(float s, float t)
        {
            var local = resultTime - 0.8f;
            var enter = Look.EaseOutBack(local / 0.5f);
            var card = ResultCard(s);
            card.y += (1 - enter) * 40 * s;
            var tier = BowlSimulation.GoalTier(sim.Score);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01(local / 0.2f));
            var m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one * (0.94f + 0.06f * Mathf.Clamp01(enter)), card.center);
            Paper(card, s, -0.8f, tier > 0 ? 1 : 4);
            UIKit.Rotated(-0.8f, card, () =>
            {
                UIKit.Label(new Rect(card.x, card.y + 22 * s, card.width, 70 * s), tier > 0 ? $"{BowlSimulation.Goals[tier - 1].name} medal!" : "Nice session", 48 * s, UIKit.Rose600, TextAnchor.MiddleCenter, false, FontStyle.Normal, Look.Title);
                if (tier > 0) Medal(new Vector2(card.xMax - 84 * s, card.y + 62 * s), 56 * s, tier);
                if (tier > 0) Look.HeartAt(new Vector2(card.x + 70 * s, card.y + 56 * s + Mathf.Sin(t * 2.5f) * 3 * s), 30 * s, UIKit.Rose400);
                UIKit.Label(new Rect(card.x, card.y + 94 * s, card.width, 70 * s), $"{Mathf.FloorToInt(sim.Score)}", 58 * s, UIKit.Rose500);
                var best = lastReward is { NewBest: true } ? "New best score!" : $"Best {SaveStore.Data.bowlBest}";
                UIKit.Label(new Rect(card.x, card.y + 158 * s, card.width, 30 * s), best, 22 * s, lastReward is { NewBest: true } ? UIKit.Hex("#059669") : UIKit.Rose400);
                UIKit.Label(new Rect(card.x + 20 * s, card.y + 192 * s, card.width - 40 * s, 30 * s),
                    $"Airs {sim.Airs} · Tricks {sim.TricksLanded} · Perfects {sim.Perfects} · Best combo x{sim.BestCombo}", 20 * s, UIKit.Rose400, TextAnchor.MiddleCenter, true, FontStyle.Normal);
                if (lastReward is { Earned: > 0 } reward)
                    UIKit.Label(new Rect(card.x, card.y + 226 * s, card.width, 34 * s), $"New medal! You unlocked {reward.Earned} {(reward.Earned == 1 ? "memory" : "memories")}", 24 * s, UIKit.Rose600);
                else if (BowlSimulation.NextGoal(sim.Score) is { } next)
                    UIKit.Label(new Rect(card.x, card.y + 226 * s, card.width, 34 * s), $"Next: {next.name} at {next.score} points", 22 * s, UIKit.Rose400);
                else UIKit.Label(new Rect(card.x, card.y + 226 * s, card.width, 34 * s), "Every medal earned", 22 * s, UIKit.Rose400);
            });
            if (bookButton.width > 0) UIKit.Button(bookButton, "Open Memory Book", UIKit.Rose500, Color.white, 24 * s);
            UIKit.Button(againButton, "Drop in again", UIKit.Hex("#ec4899"), Color.white, 22 * s);
            UIKit.Button(resultMenuButton, "Menu", UIKit.Violet, Color.white, 22 * s);
            GUI.matrix = m;
            GUI.color = old;
        }
    }
}
