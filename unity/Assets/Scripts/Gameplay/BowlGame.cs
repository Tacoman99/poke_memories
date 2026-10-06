using PokeMemories.Menu;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PokeMemories.Gameplay
{
    /// <summary>
    /// Plays a BowlSimulation: the procedural bowl scene, the skater (borrowed from CourseGame),
    /// camera, input, effects and HUD. The simulation owns all physics and scoring; this class
    /// only reads its state. The bowl is built far from the course so the two never overlap, and
    /// CourseGame is switched off while the bowl is up.
    /// </summary>
    public partial class BowlGame : MonoBehaviour
    {
        // World units per simulation pixel, matching CourseGame so the skater keeps her size.
        const float U = 0.0146f;
        const float FloorY = -3.15f;
        const float CameraBase = 0.25f;
        const float BodyCentre = 0.9f;               // world units from her wheels to her middle
        const float JumpTakeoffFrame = 31f, JumpClipFrames = 49f;
        const float BailClipSeconds = 10f / 12f, LandClipSeconds = 6f / 12f;
        static readonly Vector2 Origin = new(2000, 0);

        static readonly (BowlTrick kind, string label, Key key)[] TrickInputs =
        {
            (BowlTrick.Grab, "Grab", Key.J), (BowlTrick.Kick, "Kick", Key.K),
            (BowlTrick.Spin, "Spin", Key.L), (BowlTrick.Flip, "Flip", Key.I),
        };

        CourseGame course;
        MenuFlow menu;
        Animator skater;
        SpriteRenderer skaterSprite;
        Camera view;
        float savedOrtho;
        Vector3 savedCamera;

        BowlSimulation sim = new();
        bool active, started, paused, recorded;
        public bool Active => active;
        public bool autoPlay;
        float endedAt, resultTime;
        BowlReward? lastReward;

        // Skater animation bookkeeping (mirrors CourseGame's clip selection).
        string animState;
        bool jumpClipStarted;
        int seenBails;
        float bailUntil, landUntil;
        Vector3 skaterRestPosition;

        // Camera feel.
        float shake, camVelocityY, orthoVelocity;
        float baseOrtho = 5f;

        public void Init(CourseGame owner, MenuFlow flow)
        {
            course = owner;
            menu = flow;
            skater = owner.SkaterAnimator;
            skaterSprite = skater.GetComponent<SpriteRenderer>();
            view = owner.View;
            BowlArt.BeginBuildingScene();
        }

        /// <summary>Called by the menu: take over the screen and start a fresh run.</summary>
        public void Begin()
        {
            if (!active)
            {
                active = true;
                savedOrtho = view.orthographicSize;
                savedCamera = view.transform.position;
                skaterRestPosition = skater.transform.position;
                course.enabled = false;
                BuildWorld();
            }
            world.SetActive(true);
            Restart();
        }

        /// <summary>Called by the menu when leaving: hand the skater and camera back to the course.</summary>
        public void End()
        {
            if (!active) return;
            active = false;
            world.SetActive(false);
            SilenceAudio();
            view.orthographicSize = savedOrtho;
            view.transform.position = savedCamera;
            skater.transform.position = skaterRestPosition;
            skater.transform.rotation = Quaternion.identity;
            skater.transform.localScale = Vector3.one;
            skaterSprite.color = Color.white;
            ClearTrail();
            course.enabled = true;
        }

        void Restart()
        {
            sim = new BowlSimulation();
            started = false;
            paused = false;
            recorded = false;
            lastReward = null;
            endedAt = 0;
            resultTime = 0;
            animState = null;
            introStart = Time.unscaledTime;
            jumpClipStarted = false;
            seenBails = 0;
            bailUntil = landUntil = 0;
            shake = 0;
            ResetEffects();
            baseOrtho = FitOrtho();
            view.orthographicSize = baseOrtho;
            orthoVelocity = 0;
            view.transform.position = new Vector3(Origin.x, CameraBase, view.transform.position.z);
            Place(sim.CurrentPose());
            ClearTrail();
            SetAnim("idle", 1, true);
        }

        void ClearTrail()
        {
            foreach (var trail in skater.GetComponentsInChildren<TrailRenderer>())
            {
                trail.emitting = false;
                trail.Clear();
            }
        }

        // The whole bowl, plus margin, must fit; tall phones zoom out to make room.
        float FitOrtho() => Mathf.Max(5f, (BowlSimulation.Coping.x * U + 1.5f) / Mathf.Max(0.5f, view.aspect));

        // ───────────── Frame loop ─────────────

        void Update()
        {
            if (!active || menu.Screen == GameScreen.Menu) return;
            if (menu.Screen != GameScreen.Bowl)
            {
                // The memory book is up over the bowl; keep the scene still.
                return;
            }
            ReadInput(out var pumpHeld);
            if (started && !paused && !sim.Ended)
            {
                sim.SetPump(pumpHeld);
                sim.Advance(Time.deltaTime);
            }
            if (sim.Ended && !recorded)
            {
                recorded = true;
                endedAt = Time.unscaledTime;
                // Autopilot runs are demos: they don't change saved progress.
                if (!autoPlay) lastReward = SaveStore.RecordBowl(Mathf.FloorToInt(sim.Score));
            }
            if (sim.Ended) resultTime += Time.unscaledDeltaTime;
            DrawSkater();
            UpdateWorld();
            UpdateCamera();
            UpdateEffects();
        }

        // ───────────── Input ─────────────

        Rect menuButton, autoButton, lipButton;
        readonly Rect[] trickButtons = new Rect[4];
        Rect againButton, bookButton, resultMenuButton;

        static bool Narrow => Screen.width < Screen.height * 1.2f;

        void Layout()
        {
            var s = UIInput.Scale;
            float W = Screen.width, H = Screen.height;
            var top = 14 * s;
            menuButton = Narrow ? new Rect(W - 124 * s, top, 108 * s, 52 * s) : new Rect(W - 166 * s, top, 150 * s, 52 * s);
            autoButton = Narrow ? Rect.zero : new Rect(W - 330 * s, top, 150 * s, 52 * s);

            float bw = 132 * s, bh = 80 * s, gap = 12 * s;
            var cols = Narrow ? 2 : Mathf.Clamp(Mathf.FloorToInt((W - 24 * s) / (bw + gap)), 1, 4);
            var rows = Mathf.CeilToInt(4f / cols);
            for (var i = 0; i < 4; i++)
            {
                var row = i / cols;
                var inRow = Mathf.Min(cols, 4 - row * cols);
                var col = i % cols;
                var x = W - 16 * s - (inRow - col) * (bw + gap) + gap;
                var y = H - 14 * s - (rows - row) * (bh + gap) + gap;
                trickButtons[i] = new Rect(x, y, bw, bh);
            }
            var lipWidth = Mathf.Min(2 * bw + gap, W - 32 * s);
            lipButton = new Rect(W - 16 * s - lipWidth, trickButtons[0].y - 66 * s - gap, lipWidth, 66 * s);
            if (sim.Ended) LayoutResult(s);
            else againButton = bookButton = resultMenuButton = Rect.zero;
        }

        bool OverUi(Vector2 guiPoint)
        {
            if (sim.Ended) return true;                       // the result card handles its own taps
            if (menuButton.Contains(guiPoint) || autoButton.Contains(guiPoint)) return true;
            if (!started) return false;
            if (lipButton.Contains(guiPoint)) return true;
            foreach (var r in trickButtons) if (r.Contains(guiPoint)) return true;
            return false;
        }

        // A press that lands on a button triggers it on touch-down, like the course's trick buttons.
        void PressAt(Vector2 guiPoint)
        {
            if (menuButton.Contains(guiPoint)) { menu.ShowMenu(); return; }
            if (autoButton.Contains(guiPoint)) { autoPlay = !autoPlay; return; }
            if (!started || sim.Ended) return;
            if (lipButton.Contains(guiPoint)) { sim.ArmLipHandstand(); return; }
            for (var i = 0; i < 4; i++)
                if (trickButtons[i].Contains(guiPoint)) { sim.StartTrick(TrickInputs[i].kind); return; }
        }

        void ReadInput(out bool pumpHeld)
        {
            Layout();
            pumpHeld = false;
            var keyboard = Keyboard.current;
            var startPressed = false;
            var tapped = false;
            var anyDown = false;
            var tapPoint = Vector2.zero;

            if (keyboard != null)
            {
                startPressed |= keyboard.spaceKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame;
                pumpHeld |= keyboard.spaceKey.isPressed || keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed;
                if (started && !sim.Ended && !paused)
                {
                    foreach (var (kind, _, key) in TrickInputs)
                        if (keyboard[key].wasPressedThisFrame) sim.StartTrick(kind);
                    if (keyboard.uKey.wasPressedThisFrame) sim.ArmLipHandstand();
                }
                if (keyboard.aKey.wasPressedThisFrame) autoPlay = !autoPlay;
                if (keyboard.escapeKey.wasPressedThisFrame) { menu.ShowMenu(); return; }
                if (sim.Ended && resultTime > 1.2f && keyboard.spaceKey.wasPressedThisFrame) { Restart(); return; }
            }

            if (Touchscreen.current != null)
            {
                foreach (var touch in Touchscreen.current.touches)
                {
                    var pos = touch.position.ReadValue();
                    var gui = new Vector2(pos.x, Screen.height - pos.y);
                    anyDown |= touch.press.isPressed;
                    if (touch.press.wasPressedThisFrame) { tapped = true; tapPoint = gui; PressAt(gui); if (menu.Screen != GameScreen.Bowl) return; }
                    if (touch.press.isPressed && !OverUi(gui)) pumpHeld = true;
                    startPressed |= touch.press.wasPressedThisFrame && !OverUi(gui);
                }
            }
            else if (Mouse.current != null)
            {
                var mouse = Mouse.current;
                var pos = mouse.position.ReadValue();
                var gui = new Vector2(pos.x, Screen.height - pos.y);
                anyDown |= mouse.leftButton.isPressed;
                if (mouse.leftButton.wasPressedThisFrame) { tapped = true; tapPoint = gui; PressAt(gui); if (menu.Screen != GameScreen.Bowl) return; }
                if (mouse.leftButton.isPressed && !OverUi(gui)) pumpHeld = true;
                startPressed |= mouse.leftButton.wasPressedThisFrame && !OverUi(gui);
            }

            if (waitRelease)
            {
                if (!anyDown) waitRelease = false;
                pumpHeld = false;
                startPressed = false;
            }

            if (sim.Ended)
            {
                if (tapped && resultTime > 0.9f)
                {
                    if (bookButton.Contains(tapPoint) && lastReward is { Earned: > 0 }) { menu.OpenBook(lastReward.Value.Earned); return; }
                    if (resultMenuButton.Contains(tapPoint)) { menu.ShowMenu(); return; }
                    if (againButton.Contains(tapPoint)) { Restart(); waitRelease = true; return; }
                }
                if (autoPlay && Time.unscaledTime - endedAt > 3f) Restart();
                return;
            }

            if (paused)
            {
                if (startPressed || pumpHeld) paused = false;
                pumpHeld = false;
                return;
            }

            if (autoPlay)
            {
                started = true;
                pumpHeld = AutoPilot();
                return;
            }
            // The first press only drops her in; it also starts pumping so there is no dead moment.
            if (!started && (startPressed || pumpHeld)) started = true;
            if (!started) pumpHeld = false;
        }

        bool waitRelease;
        int autoLip;
        bool autoWasOutward;

        // Pumps on the way down, chains tricks that fit in the air, and times the odd lip handstand.
        bool AutoPilot()
        {
            if (sim.Airborne)
            {
                if (sim.Trick == null && sim.AirTime < 0.06f && sim.AirV > 560)
                {
                    var remaining = 2 * sim.AirV / BowlSimulation.Gravity - 0.12f;
                    var order = new[] { BowlTrick.Flip, BowlTrick.Spin, BowlTrick.Kick, BowlTrick.Grab };
                    var offset = sim.Airs % 4;
                    var used = 0f;
                    for (var i = 0; i < 4 && sim.QueuedTrick == null; i++)
                    {
                        var kind = order[(i + offset) % 4];
                        var duration = BowlSimulation.TrickInfo(kind).duration;
                        if (used + duration > remaining) continue;
                        if (sim.StartTrick(kind)) used += duration;
                        if (used > remaining - 0.3f) break;
                    }
                }
                return false;
            }
            var sign = sim.S < 0 ? -1 : 1;
            var eta = sim.TimeToCoping();
            var outward = sim.V * sign > 0;
            if (outward && !autoWasOutward) autoLip++;
            autoWasOutward = outward;
            if (sim.Stall <= 0 && sim.LipArmed == LipState.None && eta is < 0.09f and > 0.03f && sim.V * sign >= 380 && sim.Airs >= 2 && autoLip % 4 == 0)
                sim.ArmLipHandstand();
            return sim.V * sign < 0 || Mathf.Abs(sim.V) < 20;
        }

        void OnApplicationFocus(bool focus)
        {
#if !UNITY_EDITOR
            if (!focus && active && started && !sim.Ended && !autoPlay) paused = true;
#endif
        }

        // ───────────── Skater ─────────────

        // World position of a bowl-local point (sim pixels; height above the floor).
        static Vector3 World(float x, float h) => new(Origin.x + x * U, FloorY + h * U, 0);

        void SetAnim(string state, float speed, bool restart = false)
        {
            skater.speed = speed;
            if (animState == state && !restart) return;
            animState = state;
            skater.Play(state, 0, 0);
        }

        // Pivot is at her wheels. Rotating about her middle (flips, handstand) keeps the turn natural.
        void Place(BowlSimulation.Pose pose, float extraDegrees = 0, float tiltOverride = float.NaN)
        {
            var tilt = float.IsNaN(tiltOverride) ? -pose.Angle * Mathf.Rad2Deg : tiltOverride;
            var foot = World(pose.X, pose.H);
            var upBase = Quaternion.Euler(0, 0, tilt) * Vector3.up;
            var centre = foot + upBase * BodyCentre;
            var rotation = Quaternion.Euler(0, 0, tilt + extraDegrees);
            var t = skater.transform;
            t.rotation = rotation;
            t.position = centre - rotation * Vector3.up * BodyCentre;
            t.localScale = new Vector3(pose.Facing, 1, 1);
        }

        void DrawSkater()
        {
            var pose = sim.CurrentPose();

            if (sim.Bails > seenBails)
            {
                seenBails = sim.Bails;
                bailUntil = Time.time + BailClipSeconds;
                shake = 0.32f;
                SetAnim("bail", 1, true);
            }

            var extra = 0f;
            var tilt = float.NaN;
            var clipLocked = Time.time < bailUntil;

            if (sim.Stall > 0)
            {
                // Lip handstand: upside down on the coping with a little wobble.
                var wobble = Mathf.Sin(sim.Elapsed * 22f) * 5f * (sim.Stall / BowlSimulation.LipStall);
                extra = 180f + wobble;
                tilt = 0;
                if (!clipLocked) SetAnim("idle", 0.0f);
            }
            else if (sim.Airborne)
            {
                // Stand up out of the wall quickly, lean back in as she drops toward the lip again.
                var wall = -pose.Angle * Mathf.Rad2Deg;
                var lean = sim.Side * 8f;
                tilt = Mathf.Lerp(wall, lean, Mathf.SmoothStep(0, 1, sim.Air / 90f));
                if (sim.Trick == BowlTrick.Flip)
                    extra = sim.Side * 0f + pose.Facing * 360f * Mathf.SmoothStep(0, 1, sim.TrickProgress);
                if (!clipLocked) AirClip();
            }
            else
            {
                jumpClipStarted = false;
                if (clipLocked) { }
                else if (animState is "jump" or "grab" or "kick" or "spin")
                {
                    SetAnim("land", 2.5f);
                    landUntil = Time.time + LandClipSeconds / 2.5f;
                }
                else if (Time.time >= landUntil)
                {
                    var rolling = Mathf.Abs(sim.V) > 25;
                    SetAnim(rolling ? "push" : "idle", Mathf.Clamp(Mathf.Abs(sim.V) / 330f, 0.5f, 2f) * (sim.Pumping ? 1.25f : 1f));
                }
            }

            Place(pose, extra, tilt);
            skaterSprite.color = Color.white;
        }

        void AirClip()
        {
            if (sim.Trick is { } trick && trick != BowlTrick.Flip)
            {
                var clip = trick == BowlTrick.Grab ? "grab" : trick == BowlTrick.Kick ? "kick" : "spin";
                if (animState != clip) { animState = clip; skater.speed = 0; }
                skater.Play(clip, 0, Mathf.Min(0.999f, sim.TrickProgress));
                jumpClipStarted = true;
            }
            else if (sim.Trick == BowlTrick.Flip)
            {
                // Tucked for the flip.
                if (animState != "jump-tuck") { animState = "jump-tuck"; skater.speed = 0; skater.Play("jump", 0, 0.78f); }
            }
            else if (!jumpClipStarted)
            {
                jumpClipStarted = true;
                skater.speed = 1.4f;
                skater.Play("jump", 0, JumpTakeoffFrame / JumpClipFrames);
                animState = "jump";
            }
            else if (animState is "grab" or "kick" or "spin" or "jump-tuck")
            {
                // Trick finished before touchdown: back to the airborne tuck.
                skater.speed = 1;
                skater.Play("jump", 0, 0.75f);
                animState = "jump";
            }
        }

        // ───────────── Camera ─────────────

        void UpdateCamera()
        {
            baseOrtho = FitOrtho();
            var h = sim.Airborne ? sim.Air : 0;
            var zoomTarget = baseOrtho * Mathf.Lerp(1f, 1.2f, Mathf.Clamp01(h / 260f));
            view.orthographicSize = Mathf.SmoothDamp(view.orthographicSize, zoomTarget, ref orthoVelocity, 0.3f);
            var targetY = CameraBase + Mathf.Clamp01(h / 260f) * 0.9f;
            var camPos = view.transform.position;
            var y = Mathf.SmoothDamp(camPos.y, targetY, ref camVelocityY, 0.2f);
            var offset = Vector3.zero;
            if (shake > 0)
            {
                shake -= Time.deltaTime;
                var strength = 0.22f * Mathf.Clamp01(shake / 0.32f);
                offset = new Vector3(Mathf.PerlinNoise(Time.time * 60f, 0) - 0.5f, Mathf.PerlinNoise(0, Time.time * 60f) - 0.5f, 0) * 2f * strength;
            }
            view.transform.position = new Vector3(Origin.x, y, camPos.z) + offset;
        }

        // ───────────── Cleanup ─────────────

        void OnDestroy() => BowlArt.Release();
    }
}
