using System.Collections.Generic;
using PokeMemories.Menu;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PokeMemories.Gameplay
{
    /// <summary>
    /// Runs a CourseSimulation and draws it: skater sprite + animation, track items,
    /// following camera, touch/keyboard input and a simple HUD.
    /// The simulation owns all physics; this class only reads its state.
    /// </summary>
    public class CourseGame : MonoBehaviour
    {
        [SerializeField] PlayMode mode = PlayMode.Course;
        [SerializeField] Animator skater;
        [SerializeField] Camera view;
        [Tooltip("Track item sprites and background sets. Built by PokeMemories > Build Track Art Set.")]
        [SerializeField] TrackArtSet art;
        [Tooltip("World units per simulation pixel. 0.0146 makes the 126px sim skater match her 1.84-unit sprite.")]
        [SerializeField] float unitsPerPixel = 0.0146f;
        [SerializeField] float groundY = -1.9f;
        [Tooltip("How far ahead of the camera centre the skater sits, in world units (negative = left of centre).")]
        [SerializeField] float skaterScreenOffset = -3f;
        [Tooltip("Autopilot: jumps obstacles, lands rails and throws tricks. Toggle with the Auto button or the A key.")]
        public bool autoPlay;

        // Jump clip: frame 31 starts the takeoff extension, 34+ is the airborne tuck.
        const float JumpTakeoffFrame = 31f, JumpClipFrames = 49f;
        // The player's footprint is [x-22, x+35] in sim pixels, so her visual centre is offset.
        const int GroundTiles = 24;
        const float GapDepth = 3f, RailBarThickness = 0.14f, RailPostWidth = 0.14f;
        const float SkaterCentreOffset = (CourseSimulation.PlayerRight - CourseSimulation.PlayerLeft) / 2;

        CourseSimulation sim;
        SpriteRenderer skaterSprite;
        GameFeel feel;
        VisualPolish polish;
        MenuFlow menu;
        RunReward? lastReward;
        bool recorded;
        Transform ground;
        SpriteRenderer groundSprite;
        ParallaxBackground background;
        readonly Dictionary<TrackItem, GameObject> itemViews = new();
        readonly HashSet<TrackItem> liveItems = new();
        string animState;
        bool jumpRequested;
        bool started;
        float endedAt;
        int seenBails;
        float bailUntil, landUntil;
        const float BailClipSeconds = 10f / 12f, LandClipSeconds = 6f / 12f;

        Rect[] trickButtons = new Rect[3];
        Rect autoButton, menuButton, bookButton;
        int autoTrick;
        static readonly Dictionary<TrickKind, string> TrickClips = new()
        {
            { TrickKind.Grab, "grab" },
            { TrickKind.Kick, "kick" },
            { TrickKind.Spin, "spin" },
        };
        static readonly (TrickKind kind, string label, Key key)[] TrickInputs =
        {
            (TrickKind.Grab, "Grab (J)", Key.J),
            (TrickKind.Kick, "Kick (K)", Key.K),
            (TrickKind.Spin, "Spin (L)", Key.L),
        };

        void Awake()
        {
            Application.targetFrameRate = 60;
#if UNITY_EDITOR
            // Keep Play mode running when the Editor isn't focused (e.g. testing from another window).
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            skaterSprite = skater.GetComponent<SpriteRenderer>();
            skaterSprite.sortingOrder = 10;

            // The ground is a tiled strip that jumps by whole tiles with the camera, so its
            // pattern stays fixed in the world while the camera scrolls over it.
            ground = new GameObject("Ground").transform;
            groundSprite = ground.gameObject.AddComponent<SpriteRenderer>();
            groundSprite.sprite = art.ground;
            groundSprite.drawMode = SpriteDrawMode.Tiled;
            groundSprite.tileMode = SpriteTileMode.Continuous;
            groundSprite.size = new Vector2(GroundTiles * art.ground.bounds.size.x, art.ground.bounds.size.y);
            groundSprite.sortingOrder = 0;

            background = new GameObject("Background").AddComponent<ParallaxBackground>();
            background.Init(art, view, groundY);

            feel = gameObject.AddComponent<GameFeel>();
            feel.SetFollowOffset(skaterScreenOffset);
            polish = gameObject.AddComponent<VisualPolish>();
            menu = gameObject.AddComponent<MenuFlow>();
            menu.Init(this);
            Restart();
        }

        void Restart()
        {
            foreach (var itemView in itemViews.Values) Destroy(itemView);
            itemViews.Clear();
            sim = new CourseSimulation(mode);
            sim.TookOff += () => jumpRequested = true;
            animState = null;
            seenBails = 0;
            bailUntil = 0;
            landUntil = 0;
            started = false;
            recorded = false;
            lastReward = null;
            feel.Bind(sim, view, groundY, skaterSprite.sharedMaterial);
            polish.Bind(sim, view, skaterSprite.sharedMaterial, skater.transform, groundY);
            skater.transform.rotation = Quaternion.identity;
            skater.transform.localScale = Vector3.one;
        }

        /// <summary>Starts a fresh run in the given mode (called by the menu).</summary>
        public void BeginRun(PlayMode playMode)
        {
            mode = playMode;
            endedAt = 0;
            Restart();
        }

        /// <summary>Puts the course back at its idle start, for the menu backdrop.</summary>
        public void ResetToStart()
        {
            endedAt = 0;
            Restart();
        }

        void Update()
        {
            // The menu and memory book leave the course idle behind them.
            if (menu.Screen == GameScreen.Playing)
            {
                ReadInput();
                if (started) sim.Advance(Time.deltaTime);
            }
            DrawSkater();
            DrawTrack();
        }

        void ReadInput()
        {
            var keyboard = Keyboard.current;
            bool pressed = false, held = false;

            if (keyboard != null)
            {
                pressed |= keyboard.spaceKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame;
                held |= keyboard.spaceKey.isPressed || keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed;
                foreach (var (kind, _, key) in TrickInputs)
                    if (keyboard[key].wasPressedThisFrame) sim.StartTrick(kind);
                if (keyboard.aKey.wasPressedThisFrame) autoPlay = !autoPlay;
                if (keyboard.escapeKey.wasPressedThisFrame) { menu.ShowMenu(); return; }
            }

            // Touch: any finger outside the trick buttons is the jump; fingers on buttons do tricks.
            if (Touchscreen.current != null)
            {
                foreach (var touch in Touchscreen.current.touches)
                {
                    if (!touch.press.isPressed && !touch.press.wasReleasedThisFrame) continue;
                    var uiButton = UiButtonAt(touch.position.ReadValue());
                    if (uiButton != null)
                    {
                        if (touch.press.wasPressedThisFrame) { PressUiButton(uiButton.Value); if (menu.Screen != GameScreen.Playing) return; }
                        continue;
                    }
                    var trick = TrickAt(touch.position.ReadValue());
                    if (trick != null)
                    {
                        if (touch.press.wasPressedThisFrame) sim.StartTrick(trick.Value);
                        continue;
                    }
                    pressed |= touch.press.wasPressedThisFrame;
                    held |= touch.press.isPressed;
                }
            }
            else if (Mouse.current != null)
            {
                var mouse = Mouse.current;
                var trick = TrickAt(mouse.position.ReadValue());
                var uiButton = UiButtonAt(mouse.position.ReadValue());
                if (uiButton != null)
                {
                    if (mouse.leftButton.wasPressedThisFrame) { PressUiButton(uiButton.Value); if (menu.Screen != GameScreen.Playing) return; }
                }
                else if (trick != null)
                {
                    if (mouse.leftButton.wasPressedThisFrame) sim.StartTrick(trick.Value);
                }
                else
                {
                    pressed |= mouse.leftButton.wasPressedThisFrame;
                    held |= mouse.leftButton.isPressed;
                }
            }

            if (autoPlay && !sim.Ended)
            {
                started = true;
                AutoPilot(ref pressed, ref held);
            }
            if (!started)
            {
                // The first tap only starts the run; it shouldn't also jump.
                if (pressed) started = true;
                return;
            }
            if (sim.Ended)
            {
                if (!recorded)
                {
                    // Autopilot runs are demos and testing: they don't change saved progress.
                    recorded = true;
                    if (!autoPlay) lastReward = SaveStore.RecordRun(mode, sim.Collected, sim.Completed);
                }
                // Short pause so the jump that ended the run doesn't instantly restart it.
                if (endedAt == 0) endedAt = Time.time;
                var waited = Time.time - endedAt;
                // Autopilot starts the next run by itself after showing the result.
                if ((pressed && waited > 0.8f) || (autoPlay && waited > 3f)) { endedAt = 0; Restart(); }
                return;
            }
            if (pressed) sim.PressJump();
            if (!held) sim.ReleaseJump();
        }

        // Looks a short way ahead and jumps anything in the way, aiming to come down on rails.
        void AutoPilot(ref bool pressed, ref bool held)
        {
            if (sim.Grounded && sim.Rail == null)
            {
                foreach (var item in sim.Items)
                {
                    if (item.Taken || item.Kind == ItemKind.Ball) continue;
                    var ahead = item.X - (sim.Distance + CourseSimulation.PlayerRight);
                    var trigger = item.Kind switch
                    {
                        ItemKind.Gap => 40f,
                        ItemKind.Rail or ItemKind.Stairs => 70f,
                        ItemKind.Kicker => -1f, // ride it; the kicker launches her
                        _ => 95f,
                    };
                    if (ahead > 0 && ahead < trigger * sim.Pace) { pressed = true; break; }
                }
            }
            else if (sim.Rail != null)
            {
                // Hop off near the end of a rail so she clears whatever comes after.
                var rail = sim.Items.Find(item => item.Id == sim.Rail);
                if (rail != null && rail.X + rail.Width - sim.Distance < 30 * sim.Pace)
                {
                    // Hop for the extra air time only if no obstacle waits where she would land;
                    // otherwise just roll off the end and jump the obstacle from the ground.
                    var railEnd = rail.X + rail.Width;
                    var blocked = sim.Items.Exists(item => !item.Taken && item.Kind != ItemKind.Ball && item != rail
                        && item.X > railEnd && item.X < railEnd + 380 * sim.Pace);
                    pressed = !blocked;
                }
            }
            // A held press cannot start a new jump: lift the finger for a frame first, like a real tap.
            if (pressed && sim.Held && sim.Grounded) { pressed = false; held = false; return; }
            held = pressed || (!sim.Grounded && sim.Velocity < 0);
            // One trick per jump, right after takeoff while there is time to finish it.
            if (!sim.Grounded && sim.Trick == null && sim.AirTime < 0.05f && sim.Velocity < -500)
            {
                var kind = TrickInputs[autoTrick++ % TrickInputs.Length].kind;
                if (kind != TrickKind.Spin || sim.Velocity < -800) sim.StartTrick(kind);
                else sim.StartTrick(TrickKind.Grab);
            }
        }

        enum UiButton { Auto, Menu, Book }

        UiButton? UiButtonAt(Vector2 screenPosition)
        {
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            if (autoButton.Contains(guiPoint)) return UiButton.Auto;
            if (menuButton.Contains(guiPoint)) return UiButton.Menu;
            if (bookButton.Contains(guiPoint)) return UiButton.Book;
            return null;
        }

        void PressUiButton(UiButton button)
        {
            switch (button)
            {
                case UiButton.Auto: autoPlay = !autoPlay; break;
                case UiButton.Menu: menu.ShowMenu(); break;
                case UiButton.Book: menu.OpenBook(lastReward?.Earned ?? 0); break;
            }
        }

        TrickKind? TrickAt(Vector2 screenPosition)
        {
            // Input System positions start bottom-left; GUI rects start top-left.
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            for (var i = 0; i < trickButtons.Length; i++)
                if (trickButtons[i].Contains(guiPoint)) return TrickInputs[i].kind;
            return null;
        }

        Vector3 SimToWorld(float x, float y) => new(x * unitsPerPixel, groundY - y * unitsPerPixel, 0);

        void DrawSkater()
        {
            var position = SimToWorld(sim.Distance + SkaterCentreOffset, sim.Foot);
            skater.transform.position = position;

            // GameFeel owns the camera (smoothing, shake, zoom); it only reads simulation state.
            feel.Tick(started, position, position.y);
            polish.Tick(started);
            var tile = art.ground.bounds.size;
            ground.position = new Vector3(Mathf.Floor(view.transform.position.x / tile.x) * tile.x, groundY - tile.y / 2, 0);
            background.Tick(sim.Distance, CourseSimulation.SectionLength, mode == PlayMode.Endless);
            groundSprite.color = background.GroundTint;

            if (sim.Bails > seenBails)
            {
                seenBails = sim.Bails;
                bailUntil = Time.time + BailClipSeconds;
                jumpRequested = false;
                SetAnim("bail", 1, true);
            }

            if (Time.time < bailUntil) { }
            else if (!sim.Grounded && sim.Trick is { } trick)
            {
                // Trick clips are scrubbed by the simulation's trick progress, so they finish
                // exactly when the trick does. The simulation is never told what is playing.
                var clip = TrickClips[trick];
                if (animState != clip) { animState = clip; skater.speed = 0; }
                skater.Play(clip, 0, Mathf.Min(0.999f, sim.TrickProgress));
            }
            else if (jumpRequested)
            {
                jumpRequested = false;
                skater.speed = 1.4f;
                skater.Play("jump", 0, JumpTakeoffFrame / JumpClipFrames);
                animState = "jump";
            }
            else if (!sim.Grounded)
            {
                // Trick finished before touchdown: go back to the airborne tuck.
                if (animState is "grab" or "kick" or "spin")
                {
                    skater.speed = 1;
                    skater.Play("jump", 0, 0.75f);
                    animState = "jump";
                }
            }
            else if (sim.Rail != null) SetAnim("grind", 1);
            else
            {
                if (animState is "jump" or "grab" or "kick" or "spin") { SetAnim("land", 2.5f); landUntil = Time.time + LandClipSeconds / 2.5f; }
                else if (Time.time >= landUntil)
                    SetAnim(!started || (sim.Ended && !sim.Completed) ? "idle" : "push", sim.Speed / CourseSimulation.BaseSpeed);
            }

            // Blink while protected after a hit.
            skaterSprite.color = sim.Invulnerable > 0 && (int)(sim.Invulnerable * 10) % 2 == 0
                ? new Color(1, 1, 1, 0.35f) : Color.white;
        }

        void SetAnim(string state, float speed, bool restart = false)
        {
            skater.speed = speed;
            if (animState == state && !restart) return;
            animState = state;
            skater.Play(state, 0, 0);
        }

        void DrawTrack()
        {
            liveItems.Clear();
            foreach (var item in sim.Items)
            {
                liveItems.Add(item);
                if (!itemViews.TryGetValue(item, out var go)) itemViews[item] = go = CreateItemView(item);
                if (item.Kind == ItemKind.Ball && item.Taken && go.activeSelf) go.SetActive(false);
            }
            var gone = new List<TrackItem>();
            foreach (var pair in itemViews)
                if (!liveItems.Contains(pair.Key)) { Destroy(pair.Value); gone.Add(pair.Key); }
            foreach (var item in gone) itemViews.Remove(item);
        }

        GameObject CreateItemView(TrackItem item)
        {
            var go = new GameObject($"{item.Kind} {item.Id}");
            var left = SimToWorld(item.X, 0);
            var width = item.Width * unitsPerPixel;
            var height = -item.Y * unitsPerPixel;

            // Stretches a sprite to a world-space box centred on `position`.
            SpriteRenderer Part(Sprite sprite, Vector3 position, float w, float h, float angle = 0, int order = 5)
            {
                var part = new GameObject("part").AddComponent<SpriteRenderer>();
                part.transform.SetParent(go.transform, false);
                part.sprite = sprite;
                part.sortingOrder = order;
                part.transform.position = position;
                part.transform.localScale = new Vector3(w / sprite.bounds.size.x, h / sprite.bounds.size.y, 1);
                part.transform.rotation = Quaternion.Euler(0, 0, angle);
                return part;
            }
            Vector3 Resting(float w, float h) => left + new Vector3(w / 2, h / 2, 0);

            switch (item.Kind)
            {
                case ItemKind.Ball:
                {
                    var centre = SimToWorld(item.X + item.Width / 2, item.Y);
                    var halo = Part(Look.GlowSprite, centre, width * 3.4f, width * 3.4f, 0, 4);
                    halo.color = new Color(1f, 0.92f, 0.62f, 1f);
                    var ball = Part(art.ball, centre, width, width);
                    var fx = go.AddComponent<BallFx>();
                    fx.baseScale = halo.transform.localScale.x;
                    fx.Init(ball.transform, halo.transform);
                    break;
                }
                case ItemKind.Cone:
                    Part(art.cone, Resting(width, height), width, height);
                    break;
                case ItemKind.Barrier:
                    Part(art.barrier, Resting(width, height), width, height);
                    break;
                case ItemKind.Kicker:
                    Part(art.kicker, Resting(width, height), width, height);
                    break;
                case ItemKind.Gap:
                    // Deeper than the ground strip so no ground shows through.
                    Part(art.gap, left + new Vector3(width / 2, -GapDepth / 2, 0), width, GapDepth, 0, 1);
                    break;
                case ItemKind.Rail:
                case ItemKind.Stairs:
                {
                    var start = SimToWorld(item.X, item.Y);
                    var end = SimToWorld(item.X + item.Width, item.Y + item.Rise);
                    var along = end - start;
                    var angle = Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
                    Part(art.railBar, (start + end) / 2, along.magnitude + RailBarThickness, RailBarThickness, angle, 6);
                    foreach (var top in new[] { start + along * 0.08f, end - along * 0.08f })
                        Part(art.railPost, new Vector3(top.x, (top.y + groundY) / 2, 0), RailPostWidth, top.y - groundY);
                    if (item.Kind == ItemKind.Stairs)
                    {
                        // Steps under the handrail, descending with it.
                        const int steps = 4;
                        for (var i = 0; i < steps; i++)
                        {
                            var stepTop = Mathf.Lerp(start.y, end.y, (i + 0.5f) / steps) - 0.45f;
                            var stepX = Mathf.Lerp(start.x, end.x, (i + 0.5f) / steps);
                            var stepHeight = Mathf.Max(0.02f, stepTop - groundY);
                            Part(art.step, new Vector3(stepX, groundY + stepHeight / 2, 0), along.x / steps, stepHeight, 0, 2);
                        }
                    }
                    break;
                }
            }
            return go;
        }

        // Cream drop shadow keeps the HUD readable over both the pale and the dark backgrounds.
        static void ShadowLabel(Rect rect, string text, float size, Color colour, Font font = null, TextAnchor anchor = TextAnchor.MiddleCenter) =>
            UIKit.ShadowLabel(rect, text, size, colour, UIKit.WithAlpha(UIKit.Hex("#5a0a22"), 0.55f), Mathf.Max(1.5f, size / 16f), anchor, false, FontStyle.Bold, font);

        // Big centre-screen messages: deep rose letters with a cream halo read on every backdrop.
        static void Message(Rect rect, string text, float size, Font font = null) =>
            UIKit.ShadowLabel(rect, text, size, UIKit.Hex("#be123c"), UIKit.WithAlpha(UIKit.Hex("#fff3ec"), 0.95f), Mathf.Max(2f, size / 12f), TextAnchor.MiddleCenter, false, FontStyle.Bold, font);

        static readonly Color Cream = new(1f, 0.96f, 0.93f);

        /// <summary>A frosted rose capsule that holds HUD readouts.</summary>
        static void Glass(Rect r, float alpha = 0.5f)
        {
            Look.Shadow(r, 8 * (r.height / 56f), 0.25f, new Vector2(0, 4));
            Look.Round(r, UIKit.WithAlpha(UIKit.Hex("#6b1233"), alpha), r.height / 2);
            Look.Round(new Rect(r.x + r.height * 0.25f, r.y + 2, r.width - r.height * 0.5f, r.height * 0.42f), UIKit.WithAlpha(Color.white, 0.12f), r.height * 0.21f);
            Look.RoundOutline(r, UIKit.WithAlpha(Color.white, 0.35f), r.height / 2, 1.5f);
        }

        /// <summary>A round glass button that dips when pressed; the rect doubles as its touch target.</summary>
        static void GlassButton(Rect r, string text, float size, bool on, string key)
        {
            var over = r.Contains(UIInput.Position);
            var down = over && UIInput.Pressed;
            var press = Look.Spring("g" + key, down ? 1 : 0, 30);
            var rect = new Rect(r.x + r.width * 0.02f * press, r.y + r.height * 0.02f * press, r.width * (1 - 0.04f * press), r.height * (1 - 0.04f * press));
            Look.Shadow(rect, 8, 0.25f, new Vector2(0, 4 - press * 2));
            Look.Round(rect, UIKit.WithAlpha(on ? UIKit.Hex("#f43f5e") : UIKit.Hex("#6b1233"), on ? 0.85f : 0.5f + press * 0.2f), rect.height * 0.5f);
            Look.Round(new Rect(rect.x + rect.height * 0.2f, rect.y + 2, rect.width - rect.height * 0.4f, rect.height * 0.42f), UIKit.WithAlpha(Color.white, 0.14f), rect.height * 0.21f);
            Look.RoundOutline(rect, UIKit.WithAlpha(Color.white, 0.4f), rect.height * 0.5f, 1.5f);
            UIKit.Label(rect, text, size, Cream);
        }

        float resultTime;

        void OnGUI()
        {
            if (menu.Screen != GameScreen.Playing) return;
            Look.Ensure();
            if (!sim.Ended) resultTime = 0;
            if (Event.current.type != EventType.Repaint) return;
            var scale = Screen.height / 720f;
            var t = Time.unscaledTime;

            // Top-left readouts: hearts, pokeballs, trick score, and the course progress bar.
            var hud = new Rect(16 * scale, 14 * scale, (mode == PlayMode.Course ? 400 : 330) * scale, 56 * scale);
            Glass(hud);
            for (var i = 0; i < 3; i++)
            {
                var full = i < sim.Hearts;
                var c = new Vector2(hud.x + (28 + i * 34) * scale, hud.center.y);
                if (full) Look.Tex(new Rect(c.x - 18 * scale, c.y - 15 * scale, 36 * scale, 36 * scale), Look.Glow, UIKit.WithAlpha(UIKit.Hex("#ff6b8f"), 0.35f));
                Look.HeartAt(c, 26 * scale, full ? UIKit.Hex("#ff5c82") : UIKit.WithAlpha(Cream, 0.25f));
            }
            var ball = new Rect(hud.x + 126 * scale, hud.y + 13 * scale, 30 * scale, 30 * scale);
            Look.Ball(ball);
            UIKit.Label(new Rect(ball.xMax + 6 * scale, hud.y, 56 * scale, hud.height), $"{sim.Collected}", 28 * scale, Cream, TextAnchor.MiddleLeft);
            var star = new Rect(hud.x + 232 * scale, hud.y + 14 * scale, 28 * scale, 28 * scale);
            Look.Tex(star, Look.Sparkle, UIKit.Hex("#ffd36b"));
            UIKit.Label(new Rect(star.xMax + 6 * scale, hud.y, 80 * scale, hud.height), $"{Mathf.RoundToInt(sim.TrickScore)}", 28 * scale, Cream, TextAnchor.MiddleLeft);
            if (mode == PlayMode.Course)
            {
                var pct = Mathf.Clamp01(sim.Distance / CourseSimulation.CourseLength);
                var bar = new Rect(hud.x + 20 * scale, hud.yMax + 10 * scale, hud.width - 40 * scale, 12 * scale);
                Look.Shadow(bar, 4 * scale, 0.25f, new Vector2(0, 2));
                Look.Round(bar, UIKit.WithAlpha(UIKit.Hex("#6b1233"), 0.55f), bar.height / 2);
                if (pct > 0.01f)
                {
                    var fill = new Rect(bar.x, bar.y, Mathf.Max(bar.height, bar.width * pct), bar.height);
                    Look.Round(fill, UIKit.Hex("#ff7f9c"), bar.height / 2);
                    Look.Round(new Rect(fill.x + 3, fill.y + 1.5f, fill.width - 6, bar.height * 0.35f), UIKit.WithAlpha(Color.white, 0.4f), bar.height * 0.17f);
                }
                Look.HeartAt(new Vector2(bar.x + Mathf.Max(bar.height / 2, bar.width * pct), bar.center.y), 22 * scale, Cream);
                UIKit.Label(new Rect(bar.xMax - 60 * scale, bar.yMax + 2 * scale, 60 * scale, 22 * scale), $"{Mathf.RoundToInt(pct * 100)}%", 16 * scale, Cream, TextAnchor.MiddleRight);
            }

            var title = Screen.height * 0.2f;
            if (!started)
            {
                var bob = Mathf.Sin(t * 2.4f) * 4 * scale;
                                Message(new Rect(0, title + bob, Screen.width, 70 * scale), "Tap to start skating", 48 * scale, Look.Title);
                Message(new Rect(0, title + 66 * scale, Screen.width, 44 * scale),
                    "Tap = jump (hold for higher) · land on rails to grind · tricks in the air", 22 * scale);
            }
            else if (sim.Ended)
            {
                DrawResult(scale, t);
            }
            else if (sim.FeedbackTime > 0)
            {
                var pop = Look.EaseOutBack(1 - Mathf.Clamp01(sim.FeedbackTime / 1.0f) + 0.35f);
                var m = GUI.matrix;
                GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(pop)), new Vector2(Screen.width / 2f, Screen.height * 0.18f + 30 * scale));
                Message(new Rect(0, Screen.height * 0.18f, Screen.width, 64 * scale), sim.Feedback, 44 * scale, Look.Title);
                GUI.matrix = m;
            }

            autoButton = new Rect(Screen.width - 170 * scale, 14 * scale, 150 * scale, 56 * scale);
            menuButton = new Rect(Screen.width - 340 * scale, 14 * scale, 150 * scale, 56 * scale);
            GlassButton(autoButton, autoPlay ? "Auto: ON (A)" : "Auto: off (A)", 20 * scale, autoPlay, "auto");
            GlassButton(menuButton, "Menu (Esc)", 20 * scale, false, "menu");
            if (!sim.Ended || lastReward is not { Earned: > 0 }) bookButton = Rect.zero;

            float w = 150 * scale, h = 90 * scale, gap = 14 * scale;
            for (var i = 0; i < TrickInputs.Length; i++)
            {
                trickButtons[i] = new Rect(Screen.width - (w + gap) * (TrickInputs.Length - i), Screen.height - h - gap, w, h);
                GlassButton(trickButtons[i], TrickInputs[i].label, 24 * scale, false, "t" + i);
            }
        }

        // The run-end card: a taped sheet of paper with the result, rewards and a way into the book.
        void DrawResult(float scale, float t)
        {
            var enter = Look.EaseOutBack(resultTime / 0.5f);
            resultTime += Time.unscaledDeltaTime;
            var w = Mathf.Min(Screen.width * 0.9f, 640 * scale);
            var h = (lastReward is { Earned: > 0 } ? 320 : 200) * scale;
            var card = new Rect((Screen.width - w) / 2, Screen.height * 0.25f - 14 * scale + (1 - enter) * 40 * scale, w, h);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01(resultTime / 0.2f));
            var m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one * (0.94f + 0.06f * Mathf.Clamp01(enter)), card.center);
            UIKit.Rotated(-0.8f, card, () =>
            {
                Look.Shadow(card, 26 * scale, 0.4f, new Vector2(0, 14 * scale));
                Look.Round(card, UIKit.Hex("#fffaf4"), 18 * scale);
                GUI.BeginClip(card);
                Look.Tiled(new Rect(0, 0, card.width, card.height), Look.Paper, 400 * scale, UIKit.Hex("#fff3ec"));
                Look.FadeDown(new Rect(0, 0, card.width, 90 * scale), UIKit.WithAlpha(UIKit.Hex("#fda4af"), 0.25f));
                GUI.EndClip();
                Look.RoundOutline(new Rect(card.x + 10 * scale, card.y + 10 * scale, card.width - 20 * scale, card.height - 20 * scale), UIKit.WithAlpha(UIKit.Rose300, 0.55f), 12 * scale, 1.5f * scale);
                UIKit.DrawTape(new Rect(card.center.x - 38 * scale, card.y + 10 * scale, 76 * scale, 1), sim.Completed ? 1 : 4, scale * 1.3f);
                UIKit.Label(new Rect(card.x, card.y + 24 * scale, card.width, 80 * scale), sim.Completed ? "Sunset Course cleared!" : "Out of hearts", 44 * scale, UIKit.Rose600, TextAnchor.MiddleCenter, false, FontStyle.Normal, Look.Title);
                if (sim.Completed) Look.HeartAt(new Vector2(card.xMax - 56 * scale, card.y + 52 * scale + Mathf.Sin(t * 2.5f) * 3 * scale), 28 * scale, UIKit.Rose400);
                UIKit.Label(new Rect(card.x, card.y + 100 * scale, card.width, 40 * scale), "Tap to skate again", 26 * scale, UIKit.Rose500);
                var small = 24 * scale;
                if (lastReward is { Earned: > 0 } reward)
                {
                    UIKit.Label(new Rect(card.x, card.y + 148 * scale, card.width, 40 * scale),
                        $"You unlocked {reward.Earned} new {(reward.Earned == 1 ? "memory" : "memories")} ♡", small, UIKit.Rose600);
                    bookButton = new Rect((Screen.width - 300 * scale) / 2, card.y + 206 * scale, 300 * scale, 64 * scale);
                }
                else if (lastReward != null && !SaveStore.AllUnlocked)
                    UIKit.Label(new Rect(card.x, card.y + 148 * scale, card.width, 40 * scale),
                        $"{SaveStore.BallsToNextMemory} more Pokeballs to your next memory", small, UIKit.Rose400);
            });
            if (lastReward is { Earned: > 0 }) UIKit.Button(bookButton, "Open Memory Book ♡", UIKit.Rose500, Color.white, 24 * scale);
            GUI.matrix = m;
            GUI.color = old;
        }
    }
}
