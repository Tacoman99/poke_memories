using System;

namespace PokeMemories.Gameplay
{
    // Port of components/bowlplay.ts. Keep the two in step: same constants, same fixed
    // 120Hz steps, same rules. The bowl is a flat bottom with a circular transition on
    // each side; position is arc length along that profile, so she rolls back and forth
    // like a pendulum and launches straight up past the coping when she has enough speed.
    public enum BowlTrick { Grab, Kick, Spin, Flip }
    public enum TrickGrade { None, Good, Perfect }
    /// <summary>Grade of an armed lip handstand; Miss means she is committed to a slam.</summary>
    public enum LipState { None, Good, Perfect, Miss }

    public class BowlSimulation
    {
        public const float Step = 1f / 120f;
        public const float Gravity = 1800;
        public const float Flat = 150;
        public const float Radius = 175;
        public const float MaxAngle = 1.45f;
        public const float Duration = 60;
        const float PumpAccel = 420;
        const float Drag = 0.1f;
        const float RollDecel = 18;
        const float MaxLaunch = 980;
        const float LandingKeep = 0.97f;
        const float BailKeep = 0.45f;
        const float FallTime = 0.9f;
        const float ChainBonus = 100;
        const float LipCommit = 1;
        const float ComboWindow = 2.2f;
        const float WallArc = Radius * MaxAngle;
        public const float Half = Flat + WallArc;

        public static readonly (string name, float points, float duration)[] Tricks =
        {
            ("Pink Grab", 120, 0.4f),
            ("Heart Kick", 150, 0.35f),
            ("360 Twirl", 220, 0.5f),
            ("Backflip", 400, 0.5f),
        };
        public static (string name, float points, float duration) TrickInfo(BowlTrick kind) => Tricks[(int)kind];

        // Air tricks started this soon after takeoff count as perfect.
        public const float PerfectAirWindow = 0.15f;
        const float PerfectMultiplier = 1.5f;
        const float StreakBonus = 50;
        // Lip handstand: press as she reaches the coping. Windows are seconds before she hits it.
        public const string LipName = "Lip Handstand";
        public const float LipPerfectPoints = 450, LipGoodPoints = 180, LipPerfectWindow = 0.1f, LipGoodWindow = 0.3f,
            LipMinSpeed = 350, LipStall = 0.6f, LipExitKeep = 0.95f;

        public static readonly (int tier, string name, int score)[] Goals =
        {
            (1, "Bronze", 2000), (2, "Silver", 6000), (3, "Gold", 12000),
        };

        // ---- State (read by the view, written only by the simulation) ----
        public float S, V;
        public bool Airborne;
        public int Side = -1;
        public float Air, AirV, AirPeak;
        public bool Pumping;
        public float TimeLeft = Duration, Elapsed;
        public bool Ended;
        public float Score;
        public BowlTrick? Trick;
        public float TrickTime;
        public float PendingScore;
        public int PendingTricks;
        public int Combo;
        public float ComboIdle;
        public int BestCombo, TricksLanded, Airs, Bails;
        public float PumpPulse, LandingPulse;
        public string Feedback = "Drop in! Hold Pump to build speed ✨";
        public float FeedbackTime = 2.5f;
        public float AirTime;
        public TrickGrade TrickGrade;
        public int PendingCombo, PendingPerfects, PendingGoods;
        public LipState LipArmed;
        public BowlTrick? QueuedTrick;
        public int AirChain;
        public float Fallen;
        public int Bruises;
        public float Stall, StallSpeed;
        public int Perfects, Streak, BestStreak;
        public LipState GradeFlash;
        public float GradeFlashTime;
        /// <summary>Counts each time a trick (or the lip handstand) lands for the view's effects.</summary>
        public int Launches;

        float accumulator;

        // ---- Geometry ----

        /// <summary>Wall angle at an arc position: 0 on the flat, rising to MaxAngle at the coping.</summary>
        public static float AngleAt(float s)
        {
            var d = Math.Abs(s) - Flat;
            return d <= 0 ? 0 : Math.Min(MaxAngle, d / Radius);
        }

        /// <summary>Horizontal offset from the bowl centre and height above the bottom.</summary>
        public static (float x, float h) PointAt(float s)
        {
            var sign = s < 0 ? -1 : 1;
            var d = Math.Abs(s) - Flat;
            if (d <= 0) return (s, 0);
            var theta = Math.Min(MaxAngle, d / Radius);
            return (sign * (Flat + Radius * (float)Math.Sin(theta)), Radius * (1 - (float)Math.Cos(theta)));
        }

        public static readonly (float x, float h) Coping = PointAt(Half);

        public readonly struct Pose
        {
            public readonly float X, H, Angle;
            public readonly int Facing;
            public Pose(float x, float h, float angle, int facing) { X = x; H = h; Angle = angle; Facing = facing; }
        }

        /// <summary>Screen-agnostic pose: tilt that keeps her feet on the wall, and facing direction.</summary>
        public Pose CurrentPose()
        {
            if (Stall > 0) return new Pose(Side * Coping.x, Coping.h, 0, -Side);
            if (Airborne)
                return new Pose(Side * Coping.x, Coping.h + Air, -Side * MaxAngle, AirV > 0 ? Side : -Side);
            var point = PointAt(S);
            var sign = S < 0 ? -1 : 1;
            return new Pose(point.x, point.h, -sign * AngleAt(S), V >= 0 ? 1 : -1);
        }

        public static int GoalTier(float score)
        {
            var tier = 0;
            foreach (var goal in Goals) if (score >= goal.score) tier = goal.tier;
            return tier;
        }

        public static (int tier, string name, int score)? NextGoal(float score)
        {
            foreach (var goal in Goals) if (score < goal.score) return goal;
            return null;
        }

        public BowlSimulation()
        {
            S = -Half + 0.001f;
        }

        void Say(string text, float time = 1.3f)
        {
            Feedback = text;
            FeedbackTime = time;
        }

        public void SetPump(bool held) => Pumping = held && !Ended;

        void Flash(LipState grade)
        {
            GradeFlash = grade;
            GradeFlashTime = 0.9f;
        }

        void BreakStreak() => Streak = 0;

        // Knocked down after a bad landing or a slammed lip trick.
        void Crash(string text)
        {
            Bails++;
            Bruises++;
            Fallen = FallTime;
            Trick = null;
            TrickGrade = TrickGrade.None;
            TrickTime = 0;
            QueuedTrick = null;
            AirChain = 0;
            LipArmed = LipState.None;
            PendingScore = 0;
            PendingTricks = 0;
            PendingCombo = 0;
            PendingPerfects = 0;
            PendingGoods = 0;
            Combo = 0;
            BreakStreak();
            Flash(LipState.Miss);
            Say(text, 1.6f);
        }

        /// <summary>While a trick is running, one more press queues the next trick in the chain.</summary>
        public bool StartTrick(BowlTrick kind)
        {
            if (Ended || !Airborne || Fallen > 0) return false;
            if (Trick != null)
            {
                if (QueuedTrick != null) return false;
                QueuedTrick = kind;
                return true;
            }
            Trick = kind;
            TrickTime = 0;
            TrickGrade = AirTime <= PerfectAirWindow ? TrickGrade.Perfect : TrickGrade.Good;
            return true;
        }

        /// <summary>Seconds until she reaches the coping on her current line, or Infinity if she isn't heading there.</summary>
        public float TimeToCoping()
        {
            if (Airborne || Stall > 0) return float.PositiveInfinity;
            var sign = S < 0 ? -1 : 1;
            var outward = V * sign;
            if (outward <= 0) return float.PositiveInfinity;
            return (Half - Math.Abs(S)) / outward;
        }

        /// <summary>Handstand on the coping. Press it right as she reaches the lip.</summary>
        public bool ArmLipHandstand()
        {
            if (Ended || Airborne || Stall > 0 || LipArmed != LipState.None || Fallen > 0) return false;
            var sign = S < 0 ? -1 : 1;
            var speed = V * sign;
            var eta = TimeToCoping();
            if (eta > LipCommit)
            {
                Say("Ride up toward the lip first", 0.8f);
                return false;
            }
            if (eta > LipGoodWindow || speed < LipMinSpeed)
            {
                // She commits anyway and is going to slam on the coping.
                LipArmed = LipState.Miss;
                BreakStreak();
                Flash(LipState.Miss);
                Say(speed < LipMinSpeed ? "Not enough speed! Uh oh…" : "Too early! Uh oh…", 1);
                return false;
            }
            LipArmed = eta <= LipPerfectWindow ? LipState.Perfect : LipState.Good;
            return true;
        }

        public float TrickProgress => Trick is { } kind ? Math.Min(1, TrickTime / Tricks[(int)kind].duration) : 0;

        void StartStall(int side)
        {
            var perfect = LipArmed == LipState.Perfect;
            LipArmed = LipState.None;
            Side = side;
            StallSpeed = Math.Abs(V);
            Stall = LipStall;
            V = 0;
            Combo += perfect ? 2 : 1;
            TricksLanded++;
            float points = perfect ? LipPerfectPoints : LipGoodPoints;
            points *= Math.Min(3, Combo);
            if (perfect)
            {
                Perfects++;
                Streak++;
                points += StreakBonus * Streak;
            }
            else BreakStreak();
            Score += points;
            BestCombo = Math.Max(BestCombo, Combo);
            BestStreak = Math.Max(BestStreak, Streak);
            ComboIdle = 0;
            Flash(perfect ? LipState.Perfect : LipState.Good);
            Say($"{(perfect ? "PERFECT" : "Good")} {LipName}! +{points} ✨");
        }

        void Land()
        {
            Airborne = false;
            LandingPulse = 1;
            S = Side * (Half - 0.001f);
            V = -Side * Math.Abs(AirV) * LandingKeep;
            Air = 0;
            AirChain = 0;
            if (Trick != null)
            {
                V *= BailKeep / LandingKeep;
                Crash("Bail! She slammed and got a bruise 🤕 Finish tricks before landing");
                return;
            }
            QueuedTrick = null;
            var heightBonus = (float)Math.Floor(AirPeak / 10);
            if (PendingTricks > 0)
            {
                Combo += PendingCombo;
                TricksLanded += PendingTricks;
                var banked = PendingScore * Math.Min(3, Combo) + heightBonus;
                var clean = PendingGoods == 0;
                if (clean)
                {
                    Perfects += PendingPerfects;
                    Streak += PendingPerfects;
                    banked += StreakBonus * Streak;
                }
                else
                {
                    Perfects += PendingPerfects;
                    BreakStreak();
                }
                Score += banked;
                BestCombo = Math.Max(BestCombo, Combo);
                BestStreak = Math.Max(BestStreak, Streak);
                ComboIdle = 0;
                Flash(clean ? LipState.Perfect : LipState.Good);
                Say($"{(clean ? "PERFECT! " : "")}Landed +{banked} · combo x{Math.Min(5, Combo)} ✨");
            }
            else
            {
                Score += heightBonus;
                if (heightBonus > 0) Say($"Air +{heightBonus}", 0.8f);
            }
            PendingScore = 0;
            PendingTricks = 0;
            PendingCombo = 0;
            PendingPerfects = 0;
            PendingGoods = 0;
        }

        void StepOnce()
        {
            const float dt = Step;
            Elapsed += dt;
            TimeLeft = Math.Max(0, TimeLeft - dt);
            FeedbackTime = Math.Max(0, FeedbackTime - dt);
            LandingPulse = Math.Max(0, LandingPulse - dt * 4);
            PumpPulse = Math.Max(0, PumpPulse - dt * 3);
            GradeFlashTime = Math.Max(0, GradeFlashTime - dt);
            Fallen = Math.Max(0, Fallen - dt);

            if (Stall > 0)
            {
                Stall -= dt;
                if (Stall <= 0)
                {
                    // Drop back in off the coping with most of her speed.
                    Stall = 0;
                    S = Side * (Half - 0.001f);
                    V = -Side * StallSpeed * LipExitKeep;
                    LandingPulse = 1;
                }
            }
            else if (Airborne)
            {
                AirTime += dt;
                AirV -= Gravity * dt;
                Air += AirV * dt;
                AirPeak = Math.Max(AirPeak, Air);
                if (Trick is { } kind)
                {
                    TrickTime += dt;
                    var trick = Tricks[(int)kind];
                    if (TrickTime >= trick.duration)
                    {
                        var perfect = TrickGrade == TrickGrade.Perfect;
                        PendingScore += (float)Math.Round(trick.points * (perfect ? PerfectMultiplier : 1), MidpointRounding.AwayFromZero);
                        PendingTricks++;
                        PendingCombo += perfect ? 2 : 1;
                        if (perfect) PendingPerfects++;
                        else PendingGoods++;
                        AirChain++;
                        if (AirChain >= 2)
                        {
                            PendingScore += ChainBonus * (AirChain - 1);
                            Say($"{trick.name}! Chain x{AirChain} 🔥", 0.8f);
                        }
                        else Say($"{(perfect ? "Perfect " : "")}{trick.name}!", 0.7f);
                        Trick = null;
                        TrickGrade = TrickGrade.None;
                        TrickTime = 0;
                        if (QueuedTrick is { } next)
                        {
                            // A trick linked straight from the last one keeps the timing grade.
                            Trick = next;
                            TrickGrade = perfect ? TrickGrade.Perfect : TrickGrade.Good;
                            QueuedTrick = null;
                        }
                    }
                }
                if (Air <= 0 && AirV < 0) Land();
            }
            else
            {
                var sign = S < 0 ? -1 : 1;
                var theta = AngleAt(S);
                // Gravity along the wall pulls her back toward the centre.
                V += -sign * Gravity * (float)Math.Sin(theta) * dt;
                if (Pumping && Fallen <= 0)
                {
                    // Holding pump always adds speed along her line; when nearly stopped it pushes her toward the centre.
                    var onFlat = Math.Abs(S) < Flat;
                    var dir = Math.Abs(V) > 20 || (onFlat && V != 0) ? Math.Sign(V) : -sign;
                    V += dir * PumpAccel * dt;
                    PumpPulse = 1;
                }
                var loss = (Drag * Math.Abs(V) + RollDecel) * dt;
                V = Math.Sign(V) * Math.Max(0, Math.Abs(V) - loss);
                S += V * dt;
                if (LipArmed != LipState.None && V * sign <= 0)
                {
                    if (LipArmed == LipState.Miss) Crash("Bail! She fell off the handstand 🤕");
                    else
                    {
                        LipArmed = LipState.None;
                        BreakStreak();
                        Flash(LipState.Miss);
                        Say("Missed the lip!", 1);
                    }
                }
                if (Math.Abs(S) >= Half)
                {
                    var side = S < 0 ? -1 : 1;
                    if (V * side > 0 && LipArmed == LipState.Miss)
                    {
                        // Slammed on the coping: she tumbles back down the wall.
                        V = -side * Math.Abs(V) * 0.3f;
                        Crash("Bail! Handstand slam, ouch 🤕");
                    }
                    else if (V * side > 0 && LipArmed != LipState.None) StartStall(side);
                    else if (V * side > 0)
                    {
                        // Past the coping: launch straight up out of the wall.
                        Side = side;
                        Airborne = true;
                        AirV = Math.Min(MaxLaunch, Math.Abs(V));
                        Air = 0;
                        AirPeak = 0;
                        AirTime = 0;
                        AirChain = 0;
                        QueuedTrick = null;
                        Airs++;
                        Launches++;
                        V = 0;
                    }
                    S = side * (Half - 0.001f);
                }
                ComboIdle += dt;
                if (Combo > 0 && ComboIdle > ComboWindow) Combo = 0;
            }

            if (TimeLeft <= 0 && !Airborne && Stall <= 0)
            {
                Ended = true;
                Pumping = false;
                Say($"Time! {Score} points", 3);
            }
        }

        public void Advance(float seconds)
        {
            accumulator += Math.Max(0, Math.Min(0.1f, float.IsFinite(seconds) ? seconds : 0));
            while (accumulator + 1e-10 >= Step && !Ended)
            {
                StepOnce();
                accumulator = Math.Max(0, accumulator - Step);
            }
        }
    }
}
