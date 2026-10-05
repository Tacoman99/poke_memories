using System;
using System.Collections.Generic;

namespace PokeMemories.Gameplay
{
    // Port of components/gameplay.ts. Keep the two in step: same constants, same fixed
    // 120Hz steps, same collision rules. Units are the web game's logical pixels; y grows
    // downward and the ground is at 0, so a foot above the ground is negative.
    public enum PlayMode { Course, Endless }
    public enum TrickKind { Grab, Kick, Spin }
    public enum ItemKind { Ball, Cone, Barrier, Rail, Stairs, Kicker, Gap }

    public class TrackItem
    {
        public string Id;
        public ItemKind Kind;
        public float X, Width, Y, Rise;
        public bool Taken;
        public bool Grindable => Kind == ItemKind.Rail || Kind == ItemKind.Stairs;
    }

    public class CourseSimulation
    {
        public const float Step = 1f / 120f;
        public const float CourseLength = 10800;
        public const float BaseSpeed = 360;
        public const float MaxSpeed = 460;
        public const float SectionLength = 3600;
        public const float SkaterHeight = 126;
        public const float PlayerLeft = 22;
        public const float PlayerRight = 35;
        public const float Gravity = 1800;
        public const float JumpSpeed = 650;
        public const float MinJumpSpeed = 580;
        public const float KickSpeed = 860;
        const float Coyote = 0.1f;
        const float Buffer = 0.14f;
        const float ComboWindow = 0.6f;

        public static readonly Dictionary<TrickKind, (string name, float points, float duration)> Tricks = new()
        {
            { TrickKind.Grab, ("Pink Grab", 120, 0.4f) },
            { TrickKind.Kick, ("Heart Kick", 150, 0.35f) },
            { TrickKind.Spin, ("360 Twirl", 220, 0.5f) },
        };

        public PlayMode Mode;
        public float Distance, Elapsed, Foot, Velocity;
        public string Rail;
        public bool Grounded = true, Held, Completed, Ended;
        public float CoyoteTime = Coyote, BufferTime, Invulnerable;
        public int Hearts = 3, Collected;
        public float TrickScore;
        public float Accumulator;
        public int NextPattern;
        public readonly List<TrackItem> Items = new();
        public string Feedback = "Rose Walk · Let's roll!";
        public float FeedbackTime = 2;
        public int Landings;
        public float LandingPulse, AirBlend, GrindBlend, RollTime, AirTime, GrindTime;
        public TrickKind? Trick;
        public float TrickTime, PendingScore;
        public int PendingTricks, Combo, TricksLanded, BestCombo;
        public float ComboScore, ComboIdle;

        /// <summary>Raised on each takeoff (jump or kicker) so the view can restart the jump animation.</summary>
        public event Action TookOff;

        public CourseSimulation(PlayMode mode) => Mode = mode;

        public float Speed => Mode == PlayMode.Course ? BaseSpeed : Math.Min(MaxSpeed, BaseSpeed + Distance / 180);

        public float TrickProgress => Trick is { } t ? Math.Min(1, TrickTime / Tricks[t].duration) : 0;

        public static List<TrackItem> Pattern(int index)
        {
            var baseX = index * SectionLength;
            var variant = index % 3;
            var items = new List<TrackItem>();
            void Add(ItemKind kind, float x, float y, float width, float rise = 0) =>
                items.Add(new TrackItem { Id = $"{index}:{items.Count}", Kind = kind, X = baseX + x, Y = y, Width = width, Rise = rise });
            void Trail(float y, params float[] xs) { foreach (var x in xs) Add(ItemKind.Ball, x, y, 28); }
            void Grind(float x, float width, float y, float rise = 0)
            {
                Add(ItemKind.Rail, x, y, width, rise);
                for (float offset = 90; offset < width - 20; offset += 100)
                    Add(ItemKind.Ball, x + offset, y + rise * offset / width - 45, 28);
            }
            void Stairs(float x, float width, float y, float rise)
            {
                Add(ItemKind.Stairs, x, y, width, rise);
                for (float offset = 70; offset < width - 20; offset += 90)
                    Add(ItemKind.Ball, x + offset, y + rise * offset / width - 45, 28);
            }

            if (variant == 0)
            {
                Trail(-48, 300, 390);
                Stairs(620, 300, -76, 28);
                Grind(1330, 440, -58);
                Add(ItemKind.Cone, 2230, -36, 32);
                Trail(-105, 2220, 2310);
                Add(ItemKind.Barrier, 2820, -52, 70);
                Trail(-112, 2810, 2900);
                Add(ItemKind.Kicker, 3150, -34, 60);
                Trail(-230, 3300, 3390);
            }
            else if (variant == 1)
            {
                Grind(520, 520, -78, -16);
                Add(ItemKind.Cone, 1450, -36, 32);
                Trail(-100, 1440, 1530);
                Add(ItemKind.Cone, 2020, -36, 32);
                Add(ItemKind.Cone, 2080, -36, 32);
                Trail(-110, 2010, 2100, 2190);
                Add(ItemKind.Barrier, 2740, -52, 70);
                Trail(-112, 2730, 2820);
                Add(ItemKind.Gap, 3150, 0, 120);
                Trail(-120, 3150, 3240);
            }
            else
            {
                Grind(470, 380, -66, 12);
                Add(ItemKind.Cone, 1260, -36, 32);
                Trail(-105, 1250, 1340);
                Grind(1870, 460, -88, -10);
                Add(ItemKind.Barrier, 2750, -52, 70);
                Trail(-112, 2740, 2830);
                Add(ItemKind.Cone, 3310, -36, 32);
                Trail(-100, 3300, 3390);
            }
            return items;
        }

        public void PressJump()
        {
            if (Ended || Held) return;
            Held = true;
            BufferTime = Buffer;
            if (Grounded || CoyoteTime > 0) Jump();
        }

        public void ReleaseJump()
        {
            if (!Held) return;
            Held = false;
            if (Velocity < -MinJumpSpeed) Velocity = -MinJumpSpeed;
        }

        // Tricks only start in the air and only bank on landing, so a trick still in
        // progress at touchdown is a bail.
        public bool StartTrick(TrickKind kind)
        {
            if (Ended || Grounded || Trick != null) return false;
            Trick = kind;
            TrickTime = 0;
            return true;
        }

        public static bool Overlaps(float x, TrackItem item) =>
            x + PlayerRight >= item.X && x - PlayerLeft <= item.X + item.Width;

        public static float RailHeight(TrackItem rail, float x)
        {
            var contact = Math.Max(rail.X, Math.Min(x, rail.X + rail.Width));
            return rail.Y + rail.Rise * (contact - rail.X) / rail.Width;
        }

        void Jump()
        {
            Velocity = Held ? -JumpSpeed : -MinJumpSpeed;
            Rail = null;
            Grounded = false;
            CoyoteTime = 0;
            BufferTime = 0;
            LandingPulse = 0;
            AirTime = 0;
            TookOff?.Invoke();
        }

        void Hurt(string text)
        {
            Hearts--;
            Invulnerable = 1.8f;
            SetFeedback(Hearts > 0 ? text : "Out of hearts", 1.8f);
            if (Hearts == 0) Ended = true;
        }

        // Returns false when the skater bailed a trick on touchdown.
        bool Land()
        {
            if (Trick != null)
            {
                Trick = null;
                PendingScore = 0;
                PendingTricks = 0;
                Combo = 0;
                ComboScore = 0;
                if (Invulnerable == 0) Hurt("Bailed it! Finish the trick before landing");
                else SetFeedback("Bailed it!");
                return false;
            }
            if (PendingTricks > 0)
            {
                Combo += PendingTricks;
                var banked = PendingScore * Math.Min(5, Combo);
                TrickScore += banked;
                ComboScore += banked;
                TricksLanded += PendingTricks;
                BestCombo = Math.Max(BestCombo, Combo);
                SetFeedback($"Landed! +{Math.Round(banked)} · combo x{Math.Min(5, Combo)} ✨", 1.4f);
                PendingScore = 0;
                PendingTricks = 0;
            }
            ComboIdle = 0;
            return true;
        }

        TrackItem OverGap()
        {
            var center = Distance + (PlayerRight - PlayerLeft) / 2;
            return Items.Find(item => item.Kind == ItemKind.Gap && center > item.X && center < item.X + item.Width);
        }

        void SetFeedback(string text, float seconds = 1.3f)
        {
            Feedback = text;
            FeedbackTime = seconds;
        }

        void FillTrack(float lookAhead)
        {
            while (NextPattern * SectionLength < Distance + lookAhead + SectionLength &&
                   (Mode == PlayMode.Endless || NextPattern < 3))
                Items.AddRange(Pattern(NextPattern++));
        }

        public void StepSimulation(float dt = Step)
        {
            if (Ended) return;
            var oldX = Distance;
            var oldFoot = Foot;
            var wasGrounded = Grounded;
            Elapsed += dt;
            Distance += Speed * dt;
            Invulnerable = Math.Max(0, Invulnerable - dt);
            FeedbackTime = Math.Max(0, FeedbackTime - dt);
            LandingPulse = Math.Max(0, LandingPulse - dt * 7);
            CoyoteTime = Math.Max(0, CoyoteTime - dt);
            BufferTime = Math.Max(0, BufferTime - dt);
            if (Trick is { } current)
            {
                TrickTime += dt;
                var trick = Tricks[current];
                if (TrickTime >= trick.duration)
                {
                    PendingScore += trick.points;
                    PendingTricks++;
                    SetFeedback($"{trick.name}! hold the landing", 0.9f);
                    Trick = null;
                    TrickTime = 0;
                }
            }
            if (Grounded || Rail != null) CoyoteTime = Coyote;
            if (BufferTime > 0 && CoyoteTime > 0) Jump();

            var supportingRail = Rail == null ? null : Items.Find(item => item.Id == Rail);
            if (supportingRail != null && Overlaps(Distance, supportingRail))
            {
                Foot = RailHeight(supportingRail, Distance);
                Velocity = 0;
                Grounded = true;
                TrickScore += 60 * dt;
            }
            else
            {
                Rail = null;
                Grounded = false;
                Velocity = Math.Min(850, Velocity + Gravity * dt);
                Foot += Velocity * dt;
                if (Velocity >= 0)
                {
                    // Solve the swept foot/top crossing, then check the same footprint at
                    // that instant. This catches rail edges without snapping onto their sides.
                    TrackItem landingRail = null;
                    float landingFraction = 0, landingHeight = 0;
                    foreach (var rail in Items)
                    {
                        if (!rail.Grindable) continue;
                        var before = oldFoot - RailHeight(rail, oldX);
                        var after = Foot - RailHeight(rail, Distance);
                        if (before > 0.001f || after < 0 || after <= before) continue;
                        var fraction = Math.Max(0, Math.Min(1, -before / (after - before)));
                        var crossingX = oldX + (Distance - oldX) * fraction;
                        if (!Overlaps(crossingX, rail)) continue;
                        if (landingRail == null || fraction < landingFraction)
                        {
                            landingRail = rail;
                            landingFraction = fraction;
                            landingHeight = RailHeight(rail, Distance);
                        }
                    }
                    if (landingRail != null && Overlaps(Distance, landingRail))
                    {
                        Foot = landingHeight;
                        Velocity = 0;
                        Rail = landingRail.Id;
                        GrindTime = 0;
                        Grounded = true;
                        Landings++;
                        LandingPulse = 1;
                        var hadTricks = PendingTricks > 0;
                        if (Land() && !hadTricks) SetFeedback(landingRail.Kind == ItemKind.Stairs ? "Handrail grind! ✨" : "Clean landing! ✨");
                    }
                }
                var gap = Foot >= 0 && Invulnerable == 0 ? OverGap() : null;
                if (gap != null)
                {
                    Trick = null;
                    Hurt("Fell in the gap! Hop out");
                    if (!Ended)
                    {
                        Foot = 0;
                        Velocity = -MinJumpSpeed;
                        Grounded = false;
                        AirTime = 0;
                    }
                }
                else if (Foot >= 0)
                {
                    if (!wasGrounded && Velocity > 200) LandingPulse = 1;
                    Foot = 0;
                    Velocity = 0;
                    Grounded = true;
                    if (!wasGrounded) Land();
                }
            }
            if (Grounded && Rail == null && !Ended)
            {
                var kicker = Items.Find(item => item.Kind == ItemKind.Kicker && !item.Taken && Overlaps(Distance, item));
                if (kicker != null && Foot >= -1)
                {
                    kicker.Taken = true;
                    Velocity = -KickSpeed;
                    Grounded = false;
                    CoyoteTime = 0;
                    BufferTime = 0;
                    AirTime = 0;
                    SetFeedback("Kicker launch! Throw a trick ✨", 1.1f);
                    TookOff?.Invoke();
                }
            }
            if (Grounded && Rail == null && Combo > 0)
            {
                ComboIdle += dt;
                if (ComboIdle > ComboWindow)
                {
                    Combo = 0;
                    ComboScore = 0;
                }
            }
            if (Grounded)
            {
                CoyoteTime = Coyote;
                if (BufferTime > 0) Jump();
            }
            var blend = 1 - (float)Math.Exp(-14 * dt);
            AirBlend += ((Grounded ? 0 : 1) - AirBlend) * blend;
            GrindBlend += ((Rail != null ? 1 : 0) - GrindBlend) * blend;
            if (Grounded && Rail == null) RollTime += dt * Speed / BaseSpeed;
            if (!Grounded) AirTime = (wasGrounded ? 0 : AirTime) + dt;
            if (Rail != null) GrindTime += dt;

            foreach (var item in Items)
            {
                if (item.Taken || item.Kind == ItemKind.Rail || item.Kind == ItemKind.Kicker || item.Kind == ItemKind.Gap ||
                    item.Id == Rail || !Overlaps(Distance, item)) continue;
                if (item.Kind == ItemKind.Ball)
                {
                    if (item.Y + 14 >= Foot - SkaterHeight && item.Y - 14 <= Foot)
                    {
                        item.Taken = true;
                        Collected++;
                        SetFeedback("+1 Pokéball ♡", 0.8f);
                    }
                }
                else if (Foot > (item.Kind == ItemKind.Stairs ? RailHeight(item, Distance) : item.Y) + 4 && Invulnerable == 0)
                {
                    item.Taken = true;
                    Hurt("Ouch! Keep rolling · briefly protected");
                }
            }
            Items.RemoveAll(item => item.X + item.Width < Distance - 180);
            if (!Ended && Mode == PlayMode.Course && Distance >= CourseLength)
            {
                Distance = CourseLength;
                Completed = true;
                Ended = true;
                SetFeedback("Sunset Course cleared! ♡", 10);
            }
        }

        // Fixed steps keep collision, timers, and scoring identical across refresh
        // rates. A suspended app never advances more than 100ms on its return.
        public void Advance(float seconds, float lookAhead = 1200)
        {
            FillTrack(lookAhead);
            Accumulator += Math.Max(0, Math.Min(0.1f, float.IsFinite(seconds) ? seconds : 0));
            while (Accumulator + 1e-10f >= Step && !Ended)
            {
                StepSimulation();
                Accumulator = Math.Max(0, Accumulator - Step);
            }
        }
    }
}
