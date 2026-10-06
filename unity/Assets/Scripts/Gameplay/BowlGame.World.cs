using System.Collections.Generic;
using PokeMemories.Menu;
using UnityEngine;

namespace PokeMemories.Gameplay
{
    // Scene, particles and audio for the bowl. Everything here is built at runtime from code and the
    // existing Golden Hour backdrop, and everything only reads the simulation.
    public partial class BowlGame
    {
        const int OrderSky = -400, OrderScene = -300, OrderBunting = -250, OrderRing = 8, OrderBlob = 9, OrderFx = 12;
        static readonly Color Amber = new(0.98f, 0.75f, 0.14f), Mint = new(0.2f, 0.83f, 0.6f);

        GameObject world;
        SpriteRenderer sky, shadow, buntingRenderer, glow;
        SpriteRenderer[] layerTiles;       // 3 layers x 3 tiles
        float[] layerWidths;
        readonly SpriteRenderer[][] lipRings = new SpriteRenderer[2][];
        readonly SpriteRenderer[] lipGlows = new SpriteRenderer[2];
        readonly SpriteRenderer[] pumpRings = new SpriteRenderer[3];
        float deckY => World(0, BowlSimulation.Coping.h).y;

        static SpriteRenderer NewSprite(Transform parent, string name, Sprite sprite, int order)
        {
            var renderer = new GameObject(name).AddComponent<SpriteRenderer>();
            renderer.transform.SetParent(parent, false);
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            return renderer;
        }

        void BuildWorld()
        {
            if (world != null) return;
            world = new GameObject("Bowl World");
            world.transform.SetParent(transform, false);
            var root = world.transform;

            var set = course.Art.backgrounds[course.Art.backgrounds.Length - 1];
            sky = NewSprite(root, "Sky", set.sky, OrderSky);
            layerTiles = new SpriteRenderer[9];
            layerWidths = new float[3];
            var layers = new[] { set.far, set.mid, set.near };
            for (var l = 0; l < 3; l++)
                for (var t = 0; t < 3; t++)
                    layerTiles[l * 3 + t] = NewSprite(root, $"Backdrop {l}", layers[l], OrderSky + 10 * (l + 1));

            var scene = NewSprite(root, "Bowl", BowlArt.Scene(), OrderScene);
            scene.transform.position = World(0, 0);

            // Everything below the scene texture is solid ground.
            var white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 1f), 4f);
            var fill = NewSprite(root, "Ground Fill", white, OrderScene);
            fill.color = BowlGroundColour;
            fill.transform.position = World(0, BowlArt.Bottom) + Vector3.up * 0.02f;
            fill.transform.localScale = new Vector3(120, 40, 1);

            buntingRenderer = NewSprite(root, "Bunting", BowlArt.Bunting(), OrderBunting);
            buntingRenderer.transform.position = World(0, BowlSimulation.Coping.h + 118);
            buntingRenderer.transform.localScale = Vector3.one * 1.0f;

            shadow = NewSprite(root, "Skater Shadow", BowlArt.Blob(), OrderBlob);
            shadow.color = new Color(0.3f, 0.04f, 0.15f, 0.3f);

            glow = NewSprite(root, "Pump Glow", BowlArt.Blob(), OrderBlob);
            glow.color = new Color(1f, 0.45f, 0.65f, 0f);

            for (var side = 0; side < 2; side++)
            {
                lipRings[side] = new SpriteRenderer[3];
                for (var k = 0; k < 3; k++) lipRings[side][k] = NewSprite(root, $"Lip Ring {side}{k}", BowlArt.Ring(), OrderRing);
                lipGlows[side] = NewSprite(root, $"Lip Glow {side}", BowlArt.Blob(), OrderRing - 1);
                lipGlows[side].color = new Color(1f, 0.95f, 0.7f, 0);
            }
            for (var i = 0; i < pumpRings.Length; i++) pumpRings[i] = NewSprite(root, "Pump Ring", BowlArt.Ring(), OrderBlob);

            BuildEffects(root);
            BuildAudio();
        }

        static Color BowlGroundColour => new(0.53f, 0.35f, 0.44f);

        void UpdateWorld()
        {
            if (world == null) return;
            var cam = view.transform.position;
            var half = view.orthographicSize;
            var width = half * 2 * view.aspect + 2f;
            var t = Time.time;

            // Sky stretches from the deck line to the top of the view.
            var skyTop = cam.y + half + 0.5f;
            var skyHeight = Mathf.Max(1f, skyTop - deckY);
            var skySize = sky.sprite.bounds.size;
            sky.transform.position = new Vector3(Origin.x, deckY + skyHeight / 2, 0);
            sky.transform.localScale = new Vector3(width / skySize.x, skyHeight / skySize.y, 1);

            // Three backdrop layers drift slowly past, each tiled so the view is always covered.
            var drift = new[] { 0.05f, 0.11f, 0.0f };
            for (var l = 0; l < 3; l++)
            {
                var first = layerTiles[l * 3];
                var native = first.sprite.bounds.size;
                var scale = 3.6f / native.y;
                var tileWidth = native.x * scale - 0.02f;
                var offset = Mathf.Repeat(t * drift[l], tileWidth);
                for (var k = 0; k < 3; k++)
                {
                    var tile = layerTiles[l * 3 + k];
                    tile.transform.localScale = new Vector3(scale, scale, 1);
                    tile.transform.position = new Vector3(Origin.x + (k - 1) * tileWidth - offset, deckY - 0.25f + native.y * scale / 2, 0);
                }
            }

            buntingRenderer.transform.rotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 0.9f) * 0.5f);

            // Soft shadow under her wheels while she is on the wall, tilted to the surface.
            var pose = sim.CurrentPose();
            var grounded = !sim.Airborne && sim.Stall <= 0;
            shadow.enabled = grounded;
            if (grounded)
            {
                shadow.transform.position = World(pose.X, pose.H) + Vector3.forward * 0.01f;
                shadow.transform.rotation = Quaternion.Euler(0, 0, -pose.Angle * Mathf.Rad2Deg);
                shadow.transform.localScale = new Vector3(0.62f, 0.12f, 1);
            }

            // Pump rings and a rose glow around her while she builds speed.
            var pumping = started && sim.Pumping && grounded && !sim.Ended;
            for (var i = 0; i < pumpRings.Length; i++)
            {
                var ring = pumpRings[i];
                ring.enabled = pumping;
                if (!pumping) continue;
                var age = Mathf.Repeat(sim.Elapsed * 3f + i / 3f, 1f);
                ring.transform.position = World(pose.X, pose.H + 8) + Vector3.forward * 0.01f;
                ring.transform.rotation = Quaternion.Euler(0, 0, -pose.Angle * Mathf.Rad2Deg);
                ring.transform.localScale = new Vector3((0.25f + age * 0.55f), (0.25f + age * 0.55f) * 0.6f, 1);
                ring.color = new Color(1f, 0.43f, 0.52f, (1 - age) * 0.8f);
            }
            var glowTarget = pumping ? 0.5f : 0;
            var gc = glow.color;
            gc.a = Mathf.MoveTowards(gc.a, glowTarget, Time.deltaTime * 3f);
            glow.color = gc;
            glow.transform.position = World(pose.X, pose.H + 70) + Vector3.forward * 0.01f;
            glow.transform.localScale = Vector3.one * (2.2f + sim.PumpPulse * 0.4f);
            glow.enabled = gc.a > 0.01f;

            UpdateLipRings();
        }

        // Timing rings on the coping: amber is Good, green is Perfect; they light up as she nears the lip.
        void UpdateLipRings()
        {
            var eta = started ? sim.TimeToCoping() : float.PositiveInfinity;
            var heading = sim.S < 0 ? 0 : 1;
            for (var side = 0; side < 2; side++)
            {
                var sign = side == 0 ? -1 : 1;
                var centre = World(sign * BowlSimulation.Coping.x, BowlSimulation.Coping.h);
                var near = side == heading && eta <= BowlSimulation.LipGoodWindow + 0.25f;
                var good = near && eta <= BowlSimulation.LipGoodWindow;
                var perfect = near && eta <= BowlSimulation.LipPerfectWindow;
                var armed = side == heading && sim.LipArmed is LipState.Good or LipState.Perfect;
                var pulse = 1 + 0.05f * Mathf.Sin(Time.time * 12f);
                Ring(lipRings[side][0], centre, 0.58f * pulse, Amber, good ? 0.95f : 0.28f);
                Ring(lipRings[side][1], centre, 0.32f * pulse, Mint, perfect ? 0.95f : 0.28f);
                Ring(lipRings[side][2], centre, 0.78f * pulse, sim.LipArmed == LipState.Perfect ? Mint : Amber, armed ? 0.95f : 0f);
                var glowAlpha = armed ? 0.7f : near ? 0.35f : 0f;
                var c = lipGlows[side].color;
                c.a = Mathf.MoveTowards(c.a, glowAlpha, Time.deltaTime * 5f);
                lipGlows[side].color = c;
                lipGlows[side].transform.position = centre;
                lipGlows[side].transform.localScale = Vector3.one * 1.6f;
                lipGlows[side].enabled = c.a > 0.01f;
            }
        }

        static void Ring(SpriteRenderer ring, Vector3 centre, float radius, Color colour, float alpha)
        {
            ring.enabled = alpha > 0.01f;
            ring.transform.position = centre;
            ring.transform.localScale = Vector3.one * radius;
            ring.color = new Color(colour.r, colour.g, colour.b, alpha);
        }

        // ───────────── Particles ─────────────

        ParticleSystem dust, sparkle, hearts, trailSparks;
        readonly List<Material> fxMaterials = new();
        int seenLaunches, seenTricks, seenPerfects, seenStalls;
        float seenLanding;
        bool endBurst;

        void BuildEffects(Transform root)
        {
            Look.Ensure();
            dust = MakeParticles(root, "Dust", BowlArt.Blob().texture, new Color(1f, 0.93f, 0.95f, 0.9f), new Color(0.98f, 0.78f, 0.86f, 0f), 0.34f, 0.55f, 2.2f, -0.05f, 11, 0);
            sparkle = MakeParticles(root, "Sparkle", Look.Sparkle, Color.white, new Color(1f, 0.7f, 0.9f, 0f), 0.34f, 0.7f, 3.4f, -0.15f, 13, 0);
            hearts = MakeParticles(root, "Hearts", Look.Heart, new Color(1f, 0.5f, 0.62f, 1f), new Color(1f, 0.75f, 0.85f, 0f), 0.42f, 1.1f, 2.6f, -0.35f, 13, 0);
            trailSparks = MakeParticles(root, "Wheel Sparks", Look.Sparkle, new Color(1f, 0.9f, 0.6f, 1f), new Color(1f, 0.4f, 0.65f, 0f), 0.22f, 0.4f, 1.8f, 0.4f, 12, 0);
        }

        ParticleSystem MakeParticles(Transform root, string name, Texture texture, Color start, Color end, float size, float life, float speed, float gravity, int order, int rate)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.35f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = start;
            main.gravityModifier = gravity;
            main.maxParticles = 400;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            emission.enabled = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.05f;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(start, 0), new GradientColorKey(end, 1) },
                new[] { new GradientAlphaKey(start.a, 0), new GradientAlphaKey(end.a, 1) });
            color.color = gradient;
            var scale = ps.sizeOverLifetime;
            scale.enabled = true;
            scale.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.2f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var material = new Material(skaterSprite.sharedMaterial) { mainTexture = texture };
            fxMaterials.Add(material);
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            ps.Play();
            return ps;
        }

        static void Burst(ParticleSystem system, Vector3 position, int count)
        {
            system.transform.position = position;
            system.Emit(count);
        }

        void ResetEffects()
        {
            seenLaunches = sim.Launches;
            seenTricks = sim.TricksLanded;
            seenPerfects = sim.Perfects;
            seenStalls = 0;
            seenLanding = 0;
            endBurst = false;
            if (dust == null) return;
            dust.Clear(); sparkle.Clear(); hearts.Clear(); trailSparks.Clear();
        }

        void UpdateEffects()
        {
            if (dust == null || sim == null) return;
            var pose = sim.CurrentPose();
            var foot = World(pose.X, pose.H);
            var copingPoint = World(sim.Side * BowlSimulation.Coping.x, BowlSimulation.Coping.h);

            if (sim.Launches != seenLaunches)
            {
                seenLaunches = sim.Launches;
                Burst(dust, copingPoint, 12);
                Burst(sparkle, copingPoint, 6);
                Play(launchClip, 0.5f + Mathf.Clamp01(sim.AirV / 980f) * 0.4f);
            }
            if (sim.LandingPulse > seenLanding + 0.5f && !sim.Airborne)
            {
                Burst(dust, foot, 14);
                Play(landClip, 0.8f);
            }
            seenLanding = sim.LandingPulse;

            if (sim.TricksLanded != seenTricks)
            {
                var gained = sim.TricksLanded - seenTricks;
                seenTricks = sim.TricksLanded;
                Burst(sparkle, foot + Vector3.up * 0.9f, 8 + gained * 4);
                Play(trickClip, 0.7f);
            }
            if (sim.Perfects != seenPerfects)
            {
                if (sim.Perfects > seenPerfects)
                {
                    Burst(hearts, foot + Vector3.up * 1.0f, 8);
                    Play(perfectClip, 0.8f);
                }
                seenPerfects = sim.Perfects;
            }
            var stalling = sim.Stall > 0 ? 1 : 0;
            if (stalling == 1 && seenStalls == 0)
            {
                Burst(sparkle, copingPoint + Vector3.up * 0.3f, 14);
                Burst(hearts, copingPoint + Vector3.up * 0.6f, 6);
                Play(stallClip, 0.7f);
            }
            seenStalls = stalling;
            if (sim.Fallen > 0.85f)
            {
                Burst(dust, foot, 18);
                Play(bailClip, 1f);
            }

            if (sim.Pumping && !sim.Airborne && sim.Stall <= 0 && started && !sim.Ended && Time.frameCount % 3 == 0)
                Burst(trailSparks, foot + Vector3.up * 0.1f, 1);

            if (sim.Ended && !endBurst)
            {
                endBurst = true;
                Play(endClip, 0.9f);
                if (BowlSimulation.GoalTier(sim.Score) > 0)
                    for (var i = 0; i < 4; i++)
                        Burst(hearts, new Vector3(Origin.x + (i - 1.5f) * 2.2f, view.transform.position.y + view.orthographicSize * 0.55f, 0), 8);
            }
            UpdateAudioLoops();
        }

        // ───────────── Audio ─────────────

        const int Rate = 22050;
        AudioSource rollSource, musicSource, sfxSource;
        AudioClip launchClip, landClip, trickClip, perfectClip, stallClip, bailClip, endClip;

        void BuildAudio()
        {
            rollSource = MakeLoop(MakeNoise(1.2f, 0.12f, 0.2f), 0f, true);
            musicSource = MakeLoop(MakeMusic(), 0.24f, false);
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            launchClip = MakeSweep("Launch", 0.22f, 260, 700, 0.35f);
            landClip = MakeSweep("Land", 0.16f, 140, 55, 0.6f);
            trickClip = MakeArpeggio("Trick", new[] { 659f, 880f }, 0.06f, 0.3f);
            perfectClip = MakeArpeggio("Perfect", new[] { 784f, 988f, 1318f, 1568f }, 0.06f, 0.32f);
            stallClip = MakeArpeggio("Stall", new[] { 523f, 659f, 784f }, 0.09f, 0.3f);
            bailClip = MakeSweep("Bail", 0.35f, 190, 60, 0.6f, 0.45f);
            endClip = MakeArpeggio("End", new[] { 523f, 659f, 784f, 1046f }, 0.12f, 0.3f);
        }

        AudioSource MakeLoop(AudioClip clip, float volume, bool playNow)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = volume;
            source.spatialBlend = 0;
            if (playNow) source.Play();
            return source;
        }

        void Play(AudioClip clip, float volume)
        {
            if (sfxSource != null && clip != null && active) sfxSource.PlayOneShot(clip, volume);
        }

        void UpdateAudioLoops()
        {
            if (rollSource == null) return;
            var rolling = started && !sim.Ended && !sim.Airborne && sim.Stall <= 0 && !paused;
            var speed = Mathf.Clamp01(Mathf.Abs(sim.V) / 760f);
            rollSource.volume = Mathf.MoveTowards(rollSource.volume, rolling ? 0.07f + 0.3f * speed : 0, Time.deltaTime * 4f);
            rollSource.pitch = 0.75f + speed * 0.7f;
            if (started && !sim.Ended && !musicSource.isPlaying) musicSource.Play();
            if ((!started || sim.Ended) && musicSource.isPlaying) musicSource.volume = Mathf.MoveTowards(musicSource.volume, 0, Time.deltaTime);
            else musicSource.volume = Mathf.MoveTowards(musicSource.volume, 0.24f, Time.deltaTime * 0.5f);
        }

        void SilenceAudio()
        {
            if (rollSource != null) rollSource.volume = 0;
            if (musicSource != null) musicSource.Stop();
        }

        static AudioClip FromSamples(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static AudioClip MakeNoise(float seconds, float smoothing, float gain)
        {
            var n = (int)(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(11);
            var low = 0f;
            for (var i = 0; i < n; i++)
            {
                var white = (float)(rng.NextDouble() * 2 - 1);
                low += (white - low) * Mathf.Clamp01(smoothing);
                data[i] = low * gain;
            }
            var fade = Rate / 20;
            for (var i = 0; i < fade; i++)
            {
                var k = i / (float)fade;
                data[i] = Mathf.Lerp(data[n - fade + i], data[i], k);
            }
            return FromSamples("roll", data);
        }

        static AudioClip MakeSweep(string name, float seconds, float from, float to, float gain, float noise = 0.1f)
        {
            var n = (int)(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            var phase = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)n;
                phase += 2 * Mathf.PI * Mathf.Lerp(from, to, t) / Rate;
                var envelope = Mathf.Pow(1 - t, 2f) * Mathf.Min(1, i / 120f);
                data[i] = (Mathf.Sin(phase) + ((float)rng.NextDouble() * 2 - 1) * noise) * envelope * gain;
            }
            return FromSamples(name, data);
        }

        static AudioClip MakeArpeggio(string name, float[] notes, float noteSeconds, float gain)
        {
            var per = (int)(noteSeconds * Rate);
            var data = new float[per * notes.Length + Rate / 6];
            for (var k = 0; k < notes.Length; k++)
                for (var i = 0; i < Rate / 6 + per && k * per + i < data.Length; i++)
                {
                    var t = i / (float)Rate;
                    data[k * per + i] += Mathf.Sin(2 * Mathf.PI * notes[k] * t) * Mathf.Exp(-t * 16f) * gain;
                }
            return FromSamples(name, data);
        }

        // A relaxed, bouncy pentatonic loop in the same family as the course music, a touch faster.
        static AudioClip MakeMusic()
        {
            const float beat = 60f / 118f;
            float[] scale = { 0, 2, 4, 7, 9 };
            int[] melody = { 2, -1, 4, 3, 2, -1, 0, 1, 2, 4, -1, 3, 2, 1, 0, -1, 1, 2, 3, -1, 4, 5, 4, 3, 2, -1, 3, 2, 1, -1, 0, -1 };
            int[] bass = { 0, -5, -3, -7, 0, -5, -3, -2 };
            var step = (int)(beat * 0.5f * Rate);
            var total = melody.Length * step;
            var data = new float[total];
            for (var m = 0; m < melody.Length; m++)
            {
                if (melody[m] < 0) continue;
                var semis = scale[melody[m] % 5] + 12 * (melody[m] / 5);
                var freq = 392f * Mathf.Pow(2, semis / 12f);
                for (var i = 0; i < step * 2 && m * step + i < total; i++)
                {
                    var t = i / (float)Rate;
                    data[m * step + i] += (Mathf.Sin(2 * Mathf.PI * freq * t) + 0.28f * Mathf.Sin(4 * Mathf.PI * freq * t)) * Mathf.Exp(-t * 4.5f) * 0.13f;
                }
            }
            for (var b = 0; b < bass.Length; b++)
            {
                var freq = 110f * Mathf.Pow(2, bass[b] / 12f);
                var start = b * step * 4;
                for (var i = 0; i < step * 4 && start + i < total; i++)
                {
                    var t = i / (float)Rate;
                    data[start + i] += Mathf.Sin(2 * Mathf.PI * freq * t) * Mathf.Min(1, i / 200f) * Mathf.Exp(-t * 1.6f) * 0.17f;
                }
            }
            return FromSamples("bowl-music", data);
        }
    }
}
