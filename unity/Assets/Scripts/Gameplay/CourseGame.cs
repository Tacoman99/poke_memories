using System.Collections.Generic;
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
        const float SkaterCentreOffset = (CourseSimulation.PlayerRight - CourseSimulation.PlayerLeft) / 2;

        CourseSimulation sim;
        SpriteRenderer skaterSprite;
        Transform ground;
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
        Rect autoButton;
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

            ground = new GameObject("Ground").transform;
            var groundSprite = ground.gameObject.AddComponent<SpriteRenderer>();
            groundSprite.sprite = ShapeSprites.Square;
            groundSprite.color = new Color(0.98f, 0.8f, 0.86f);
            groundSprite.sortingOrder = 0;
            ground.localScale = new Vector3(60, 6, 1);

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
            skater.transform.rotation = Quaternion.identity;
            skater.transform.localScale = Vector3.one;
        }

        void Update()
        {
            ReadInput();
            if (started) sim.Advance(Time.deltaTime);
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
            }

            // Touch: any finger outside the trick buttons is the jump; fingers on buttons do tricks.
            if (Touchscreen.current != null)
            {
                foreach (var touch in Touchscreen.current.touches)
                {
                    if (!touch.press.isPressed && !touch.press.wasReleasedThisFrame) continue;
                    if (touch.press.wasPressedThisFrame && OnAutoButton(touch.position.ReadValue())) { autoPlay = !autoPlay; continue; }
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
                if (OnAutoButton(mouse.position.ReadValue()))
                {
                    if (mouse.leftButton.wasPressedThisFrame) autoPlay = !autoPlay;
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
                    if (ahead > 0 && ahead < trigger) { pressed = true; break; }
                }
            }
            else if (sim.Rail != null)
            {
                // Hop off near the end of a rail so she clears whatever comes after.
                var rail = sim.Items.Find(item => item.Id == sim.Rail);
                if (rail != null && rail.X + rail.Width - sim.Distance < 30) pressed = true;
            }
            held = pressed || (!sim.Grounded && sim.Velocity < 0);
            // One trick per jump, right after takeoff while there is time to finish it.
            if (!sim.Grounded && sim.Trick == null && sim.AirTime < 0.05f && sim.Velocity < -500)
            {
                var kind = TrickInputs[autoTrick++ % TrickInputs.Length].kind;
                if (kind != TrickKind.Spin || sim.Velocity < -800) sim.StartTrick(kind);
                else sim.StartTrick(TrickKind.Grab);
            }
        }

        bool OnAutoButton(Vector2 screenPosition) =>
            autoButton.Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y));

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

            var camPos = view.transform.position;
            view.transform.position = new Vector3(position.x - skaterScreenOffset, camPos.y, camPos.z);
            // The ground block is 6 units tall with a centred pivot; its top edge sits on groundY.
            ground.position = new Vector3(view.transform.position.x, groundY - 3f, 0);

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

            SpriteRenderer Part(Sprite sprite, Color color, Vector3 position, Vector3 scale, float angle = 0)
            {
                var part = new GameObject("part").AddComponent<SpriteRenderer>();
                part.transform.SetParent(go.transform, false);
                part.sprite = sprite;
                part.color = color;
                part.sortingOrder = 5;
                part.transform.position = position;
                part.transform.localScale = scale;
                part.transform.rotation = Quaternion.Euler(0, 0, angle);
                return part;
            }

            switch (item.Kind)
            {
                case ItemKind.Ball:
                    Part(ShapeSprites.Ball, Color.white, SimToWorld(item.X + item.Width / 2, item.Y), Vector3.one * width);
                    break;
                case ItemKind.Cone:
                    Part(ShapeSprites.Cone, Color.white, left + Vector3.right * width / 2, new Vector3(width, height, 1));
                    break;
                case ItemKind.Barrier:
                    Part(ShapeSprites.Barrier, Color.white, left + Vector3.right * width / 2, new Vector3(width, height, 1));
                    break;
                case ItemKind.Kicker:
                    Part(ShapeSprites.Kicker, Color.white, left + Vector3.right * width / 2, new Vector3(width, height, 1));
                    break;
                case ItemKind.Gap:
                    Part(ShapeSprites.Square, new Color(0.35f, 0.22f, 0.3f), left + new Vector3(width / 2, -1.5f, 0), new Vector3(width, 3, 1)).sortingOrder = 1;
                    break;
                case ItemKind.Rail:
                case ItemKind.Stairs:
                {
                    var start = SimToWorld(item.X, item.Y);
                    var end = SimToWorld(item.X + item.Width, item.Y + item.Rise);
                    var along = end - start;
                    var angle = Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
                    var steel = new Color(0.36f, 0.33f, 0.4f);
                    Part(ShapeSprites.Square, steel, (start + end) / 2, new Vector3(along.magnitude, 0.07f, 1), angle);
                    foreach (var top in new[] { start + along * 0.08f, end - along * 0.08f })
                        Part(ShapeSprites.Square, steel, new Vector3(top.x, (top.y + groundY) / 2, 0), new Vector3(0.05f, top.y - groundY, 1));
                    if (item.Kind == ItemKind.Stairs)
                    {
                        // Steps under the handrail, descending with it.
                        const int steps = 4;
                        for (var i = 0; i < steps; i++)
                        {
                            var stepTop = Mathf.Lerp(start.y, end.y, (i + 0.5f) / steps) - 0.45f;
                            var stepX = Mathf.Lerp(start.x, end.x, (i + 0.5f) / steps);
                            Part(ShapeSprites.Square, new Color(0.85f, 0.72f, 0.78f), new Vector3(stepX, (stepTop + groundY) / 2, 0),
                                new Vector3(along.x / steps, Mathf.Max(0.02f, stepTop - groundY), 1)).sortingOrder = 2;
                        }
                    }
                    break;
                }
            }
            return go;
        }

        void OnGUI()
        {
            var scale = Screen.height / 720f;
            var label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(30 * scale), fontStyle = FontStyle.Bold };
            label.normal.textColor = new Color(0.75f, 0.1f, 0.4f);

            var hearts = new string('♥', Mathf.Max(0, sim.Hearts)) + new string('♡', 3 - Mathf.Max(0, sim.Hearts));
            var progress = mode == PlayMode.Course ? $"   {Mathf.RoundToInt(sim.Distance / CourseSimulation.CourseLength * 100)}%" : "";
            GUI.Label(new Rect(20 * scale, 14 * scale, Screen.width, 50 * scale),
                $"{hearts}   ◓ {sim.Collected}   ★ {Mathf.RoundToInt(sim.TrickScore)}{progress}", label);

            var center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(34 * scale) };
            if (!started)
            {
                GUI.Label(new Rect(0, Screen.height * 0.22f, Screen.width, 60 * scale), "Tap to start skating ♡", center);
                GUI.Label(new Rect(0, Screen.height * 0.22f + 55 * scale, Screen.width, 50 * scale),
                    "Tap = jump (hold for higher) · land on rails to grind · tricks in the air", new GUIStyle(center) { fontSize = Mathf.RoundToInt(22 * scale) });
            }
            else if (sim.Ended)
            {
                GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 60 * scale),
                    sim.Completed ? "Sunset Course cleared! ♡" : "Out of hearts", center);
                GUI.Label(new Rect(0, Screen.height * 0.25f + 60 * scale, Screen.width, 50 * scale), "Tap to skate again", center);
            }
            else if (sim.FeedbackTime > 0)
                GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, 60 * scale), sim.Feedback, center);

            var autoStyle = new GUIStyle(GUI.skin.box) { fontSize = Mathf.RoundToInt(22 * scale), alignment = TextAnchor.MiddleCenter };
            autoButton = new Rect(Screen.width - 170 * scale, 14 * scale, 150 * scale, 56 * scale);
            GUI.Box(autoButton, autoPlay ? "Auto: ON (A)" : "Auto: off (A)", autoStyle);

            var button = new GUIStyle(GUI.skin.box) { fontSize = Mathf.RoundToInt(24 * scale), alignment = TextAnchor.MiddleCenter };
            float w = 150 * scale, h = 90 * scale, gap = 14 * scale;
            for (var i = 0; i < TrickInputs.Length; i++)
            {
                trickButtons[i] = new Rect(Screen.width - (w + gap) * (TrickInputs.Length - i), Screen.height - h - gap, w, h);
                GUI.Box(trickButtons[i], TrickInputs[i].label, button);
            }
        }
    }
}
