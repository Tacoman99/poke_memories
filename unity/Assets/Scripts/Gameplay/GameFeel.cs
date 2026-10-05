using UnityEngine;

namespace PokeMemories.Gameplay
{
    /// <summary>
    /// Effects, sound, haptics and camera feel for CourseGame. Reads CourseSimulation state only
    /// (Rail, Landings, LandingPulse, Collected, Hearts, Grounded, Speed); it never writes to the
    /// simulation, so it cannot change physics.
    /// </summary>
    public class GameFeel : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] float followSmoothTime = 0.12f;
        [SerializeField] float baseOrthoSize = 5f;
        [SerializeField] float maxZoomOut = 1.15f;
        [Tooltip("Foot height above the ground (world units) at which the zoom-out is complete.")]
        [SerializeField] float zoomFullHeight = 2.6f;
        [SerializeField] float shakeDuration = 0.3f;
        [SerializeField] float shakeAmplitude = 0.22f;

        CourseSimulation sim;
        Camera view;
        float groundY;
        float camVelocityX, shakeLeft, orthoVelocity;
        bool camPlaced;

        ParticleSystem sparks, dust, sparkle;
        AudioSource rollSource, grindSource, musicSource, sfxSource;
        AudioClip jumpClip, landClip, collectClip, hitClip;

        int lastLandings, lastCollected, lastHearts;
        bool wasGrounded = true;
        bool wasStarted;

        // Particles reuse a material that is already referenced by the scene (the skater's sprite material),
        // so a player build can never lose it the way a Shader.Find lookup can.
        Material particleMaterial;
        bool baseOrthoCaptured;

        public void Bind(CourseSimulation simulation, Camera camera, float groundWorldY, Material spriteMaterial)
        {
            sim = simulation;
            view = camera;
            groundY = groundWorldY;
            particleMaterial = spriteMaterial;
            // Capture once: a restart while zoomed out must not take the zoomed size as the base.
            if (!baseOrthoCaptured) { baseOrthoSize = camera.orthographicSize; baseOrthoCaptured = true; }
            camPlaced = false;
            lastLandings = sim.Landings;
            lastCollected = sim.Collected;
            lastHearts = sim.Hearts;
            wasGrounded = sim.Grounded;
            shakeLeft = 0;
            if (sparks == null) Build();
        }

        void Build()
        {
            sparks = MakeParticles("GrindSparks", new Color(1f, 0.85f, 0.35f), new Color(1f, 0.35f, 0.5f), 0.07f, 0.35f, 3.5f, 10, 1.6f, 11);
            dust = MakeParticles("LandingDust", new Color(1f, 0.93f, 0.95f, 0.9f), new Color(0.98f, 0.8f, 0.86f, 0f), 0.22f, 0.5f, 1.4f, 0, 0.1f, 9);
            sparkle = MakeParticles("CollectSparkle", Color.white, new Color(1f, 0.7f, 0.9f, 0f), 0.12f, 0.6f, 2.8f, 0, -0.2f, 12);

            rollSource = MakeLoop("Roll", MakeNoise(1.2f, 0.12f, 0.18f), 0.0f);
            grindSource = MakeLoop("Grind", MakeNoise(0.8f, 0.9f, 0.35f), 0.0f);
            musicSource = MakeLoop("Music", MakeMusic(), 0.28f);
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;

            jumpClip = MakeSweep("Jump", 0.18f, 330, 720, 0.35f);
            landClip = MakeSweep("Land", 0.16f, 140, 55, 0.6f);
            collectClip = MakeArpeggio("Collect", new[] { 784f, 988f, 1318f }, 0.07f, 0.35f);
            hitClip = MakeSweep("Hit", 0.35f, 190, 60, 0.6f, 0.45f);
        }

        // Called every frame from CourseGame, after the simulation advanced.
        public void Tick(bool started, Vector3 skaterPosition, float skaterFootWorldY)
        {
            if (sim == null) return;
            DetectEvents(started, skaterPosition);
            UpdateLoops(started);
            UpdateCamera(skaterPosition, skaterFootWorldY);
        }

        void DetectEvents(bool started, Vector3 pos)
        {
            if (started && !wasStarted) musicSource.Play();
            if (!started && wasStarted) musicSource.Stop();
            wasStarted = started;

            var feet = new Vector3(pos.x, pos.y, 0);

            if (sim.Landings != lastLandings || (sim.Grounded && !wasGrounded))
            {
                Burst(dust, feet + Vector3.right * 0.1f, 14);
                sfxSource.PlayOneShot(landClip, 0.8f);
                Haptic(18, 120);
                lastLandings = sim.Landings;
            }
            if (wasGrounded && !sim.Grounded) sfxSource.PlayOneShot(jumpClip, 0.7f);
            wasGrounded = sim.Grounded;

            if (sim.Collected != lastCollected)
            {
                for (var i = lastCollected; i < sim.Collected; i++) Burst(sparkle, feet + new Vector3(0.7f, 0.9f, 0), 12);
                sfxSource.PlayOneShot(collectClip, 0.8f);
                lastCollected = sim.Collected;
            }

            if (sim.Hearts < lastHearts)
            {
                shakeLeft = shakeDuration;
                sfxSource.PlayOneShot(hitClip, 1f);
                Haptic(60, 255);
            }
            lastHearts = sim.Hearts;

            // Grind sparks follow the wheels while on a rail.
            var emission = sparks.emission;
            emission.enabled = sim.Rail != null && !sim.Ended;
            sparks.transform.position = feet + Vector3.right * 0.1f;
        }

        void UpdateLoops(bool started)
        {
            var rolling = started && !sim.Ended && sim.Grounded && sim.Rail == null;
            var speedFactor = Mathf.Clamp01(sim.Speed / CourseSimulation.MaxSpeed);
            SetLoop(rollSource, rolling ? 0.25f * speedFactor : 0, 0.8f + speedFactor * 0.5f);
            SetLoop(grindSource, started && !sim.Ended && sim.Rail != null ? 0.45f : 0, 0.9f + speedFactor * 0.3f);
        }

        void UpdateCamera(Vector3 skaterPos, float footWorldY)
        {
            var camPos = view.transform.position;
            var targetX = skaterPos.x + followOffsetX;
            var x = camPlaced ? Mathf.SmoothDamp(camPos.x, targetX, ref camVelocityX, followSmoothTime) : targetX;
            camPlaced = true;

            var height = Mathf.Max(0, footWorldY - groundY);
            var zoomTarget = baseOrthoSize * Mathf.Lerp(1, maxZoomOut, Mathf.Clamp01(height / zoomFullHeight));
            view.orthographicSize = Mathf.SmoothDamp(view.orthographicSize, zoomTarget, ref orthoVelocity, 0.25f);

            var shake = Vector3.zero;
            if (shakeLeft > 0)
            {
                shakeLeft -= Time.deltaTime;
                var strength = shakeAmplitude * Mathf.Clamp01(shakeLeft / shakeDuration);
                shake = new Vector3(Mathf.PerlinNoise(Time.time * 60f, 0) - 0.5f, Mathf.PerlinNoise(0, Time.time * 60f) - 0.5f, 0) * 2f * strength;
            }
            view.transform.position = new Vector3(x, camPos.y, camPos.z) + shake;
        }

        // How far the skater sits ahead of the camera centre; set by CourseGame.
        float followOffsetX;
        public void SetFollowOffset(float skaterScreenOffset) => followOffsetX = -skaterScreenOffset;

        // ---- helpers ----

        static void Burst(ParticleSystem system, Vector3 position, int count)
        {
            system.transform.position = position;
            system.Emit(count);
        }

        static void SetLoop(AudioSource source, float volume, float pitch)
        {
            source.volume = Mathf.MoveTowards(source.volume, volume, Time.deltaTime * 4f);
            source.pitch = pitch;
        }

        // Unity adds the Android VIBRATE permission to the manifest only when the built code references
        // Handheld.Vibrate. The JNI vibrator path below needs that permission, so keep a (never taken)
        // reference to it. This flag is never set.
        static bool neverTrue;

        static void Haptic(long milliseconds, int amplitude)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (neverTrue) Handheld.Vibrate();
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                if (version.GetStatic<int>("SDK_INT") >= 26)
                {
                    using var effect = new AndroidJavaClass("android.os.VibrationEffect")
                        .CallStatic<AndroidJavaObject>("createOneShot", milliseconds, amplitude);
                    vibrator.Call("vibrate", effect);
                }
                else vibrator.Call("vibrate", milliseconds);
            }
            catch (System.Exception) { }
#elif UNITY_IOS && !UNITY_EDITOR
            if (amplitude > 200) Handheld.Vibrate();
#endif
        }

        ParticleSystem MakeParticles(string name, Color start, Color end, float size, float life, float speed, int rate, float gravity, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = start;
            main.gravityModifier = gravity;
            main.maxParticles = 300;

            var emission = ps.emission;
            emission.rateOverTime = rate;
            emission.enabled = rate > 0;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = rate > 0 ? 28 : 70;
            shape.radius = 0.02f;
            shape.rotation = new Vector3(rate > 0 ? -135 : -90, 0, 0);

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
            renderer.sharedMaterial = particleMaterial;
            renderer.sortingOrder = order;
            ps.Play();
            return ps;
        }

        AudioSource MakeLoop(string name, AudioClip clip, float volume)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = volume;
            source.spatialBlend = 0;
            if (name != "Music") source.Play();
            return source;
        }

        // ---- procedural placeholder audio ----

        const int Rate = 22050;

        static AudioClip FromSamples(string name, float[] samples, bool loopable = false)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static AudioClip MakeNoise(float seconds, float smoothing, float gain)
        {
            var n = (int)(seconds * Rate);
            var data = new float[n];
            var rng = new System.Random(7);
            var low = 0f;
            for (var i = 0; i < n; i++)
            {
                var white = (float)(rng.NextDouble() * 2 - 1);
                low += (white - low) * Mathf.Clamp01(smoothing);
                // Blend towards white noise for a brighter scrape when smoothing is high.
                data[i] = (smoothing > 0.6f ? white - low * 0.5f : low) * gain;
            }
            // Cross-fade the tail into the head so the loop has no click.
            var fade = Rate / 20;
            for (var i = 0; i < fade; i++)
            {
                var t = i / (float)fade;
                data[i] = Mathf.Lerp(data[n - fade + i], data[i], t);
            }
            return FromSamples("noise", data, true);
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
            var data = new float[per * notes.Length + Rate / 8];
            for (var k = 0; k < notes.Length; k++)
                for (var i = 0; i < per + Rate / 8 && k * per + i < data.Length; i++)
                {
                    var t = i / (float)Rate;
                    data[k * per + i] += Mathf.Sin(2 * Mathf.PI * notes[k] * t) * Mathf.Exp(-t * 18f) * gain;
                }
            return FromSamples(name, data);
        }

        // Soft pentatonic loop: 8 bars of bass + bell melody at ~110bpm.
        static AudioClip MakeMusic()
        {
            const float beat = 60f / 110f;
            float[] scale = { 0, 2, 4, 7, 9 }; // C major pentatonic semitones
            int[] melody = { 4, 3, 2, 3, 4, 4, 4, -1, 3, 3, 3, -1, 4, 6, 6, -1, 4, 3, 2, 3, 4, 4, 4, 4, 3, 3, 4, 3, 2, -1, -1, -1 };
            int[] bass = { 0, 0, -3, -3, -5, -5, -3, -3 };
            var total = (int)(melody.Length * beat * 0.5f * Rate);
            var data = new float[total];
            var step = (int)(beat * 0.5f * Rate);
            for (var m = 0; m < melody.Length; m++)
            {
                if (melody[m] < 0) continue;
                var semis = scale[melody[m] % 5] + 12 * (melody[m] / 5);
                var freq = 523.25f * Mathf.Pow(2, semis / 12f);
                for (var i = 0; i < step * 2 && m * step + i < total; i++)
                {
                    var t = i / (float)Rate;
                    data[m * step + i] += (Mathf.Sin(2 * Mathf.PI * freq * t) + 0.3f * Mathf.Sin(4 * Mathf.PI * freq * t)) * Mathf.Exp(-t * 4f) * 0.14f;
                }
            }
            for (var b = 0; b < bass.Length; b++)
            {
                var freq = 130.81f * Mathf.Pow(2, bass[b] / 12f);
                var start = b * step * 4;
                for (var i = 0; i < step * 4 && start + i < total; i++)
                {
                    var t = i / (float)Rate;
                    data[start + i] += Mathf.Sin(2 * Mathf.PI * freq * t) * Mathf.Min(1, i / 200f) * Mathf.Exp(-t * 1.5f) * 0.18f;
                }
            }
            return FromSamples("music", data, true);
        }
    }
}
