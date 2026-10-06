using PokeMemories.Menu;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PokeMemories.Gameplay
{
    /// <summary>
    /// Look-and-feel layer for CourseGame: URP 2D lights, bloom/vignette post-processing and a
    /// speed trail behind the skater's wheels. Reads CourseSimulation state only (Pace,
    /// BoostPulse, Rail, Grounded); it never writes to the simulation, so it cannot change physics.
    /// Everything is built at runtime so no scene or asset has to be hand-edited.
    /// </summary>
    public class VisualPolish : MonoBehaviour
    {
        CourseSimulation sim;
        Light2D globalLight, skaterGlow;
        TrailRenderer trail;
        Gradient trailGradient;
        VolumeProfile profile;
        Bloom bloom;
        Vignette vignette;
        ChromaticAberration aberration;

        public void Bind(CourseSimulation simulation, Camera camera, Material spriteMaterial, Transform skater)
        {
            sim = simulation;
            if (globalLight != null) return; // restart: everything already exists

            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;

            globalLight = new GameObject("Global Light").AddComponent<Light2D>();
            globalLight.transform.SetParent(transform, false);
            globalLight.lightType = Light2D.LightType.Global;
            globalLight.color = new Color(1f, 0.95f, 0.97f);
            globalLight.intensity = 1f;

            // Soft warm halo that rides with the skater and swells with speed.
            skaterGlow = new GameObject("Skater Glow").AddComponent<Light2D>();
            skaterGlow.transform.SetParent(skater, false);
            skaterGlow.transform.localPosition = new Vector3(0, 0.9f, 0);
            skaterGlow.lightType = Light2D.LightType.Point;
            skaterGlow.color = new Color(1f, 0.55f, 0.75f);
            skaterGlow.pointLightOuterRadius = 3.2f;
            skaterGlow.pointLightInnerRadius = 0.3f;
            skaterGlow.intensity = 0.35f;
            skaterGlow.falloffIntensity = 0.7f;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            bloom = profile.Add<Bloom>();
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.6f);
            bloom.tint.Override(new Color(1f, 0.85f, 0.95f));
            vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.55f);
            vignette.color.Override(new Color(0.35f, 0.05f, 0.2f));
            var grade = profile.Add<ColorAdjustments>();
            grade.saturation.Override(10f);
            grade.contrast.Override(6f);
            grade.postExposure.Override(0.1f);
            // Warm highlights and rosy shadows: a gentle golden-hour grade over the pastel art.
            var lgg = profile.Add<LiftGammaGain>();
            lgg.lift.Override(new Vector4(1.04f, 0.97f, 1.02f, -0.02f));
            lgg.gain.Override(new Vector4(1.04f, 1.0f, 0.95f, 0.03f));
            var grain = profile.Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.16f);
            grain.response.Override(0.85f);
            aberration = profile.Add<ChromaticAberration>();
            aberration.intensity.Override(0f);
            var volume = new GameObject("Post Volume").AddComponent<Volume>();
            volume.transform.SetParent(transform, false);
            volume.isGlobal = true;
            volume.priority = 10;
            volume.sharedProfile = profile;

            // Trail from the wheels: hot pink at the skater fading to nothing.
            var trailObject = new GameObject("Speed Trail");
            trailObject.transform.SetParent(skater, false);
            trailObject.transform.localPosition = new Vector3(-0.35f, 0.12f, 0);
            trail = trailObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = spriteMaterial;
            trail.time = 0.35f;
            trail.minVertexDistance = 0.05f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, 0.16f), new Keyframe(1, 0));
            trail.sortingOrder = 9;
            trail.emitting = false;
            trailGradient = new Gradient();
            trailGradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0), new GradientColorKey(new Color(1f, 0.35f, 0.6f), 1) },
                new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0, 1) });
            trail.colorGradient = trailGradient;
        }

        // Called every frame from CourseGame after the simulation advanced.
        public void Tick(bool started)
        {
            if (sim == null || trail == null) return;
            var fast = Mathf.InverseLerp(1.08f, CourseSimulation.MaxPace, sim.Pace);
            var intensity = Mathf.Clamp01(Mathf.Max(fast, sim.BoostPulse, sim.Rail != null ? 0.6f : 0));
            trail.emitting = started && !sim.Ended && intensity > 0.05f;
            trail.widthMultiplier = 0.6f + intensity * 1.2f;
            trail.time = 0.2f + intensity * 0.3f;
            skaterGlow.intensity = 0.3f + intensity * 0.5f + sim.BoostPulse * 0.3f;
            bloom.intensity.value = 0.45f + intensity * 0.35f + sim.BoostPulse * 0.25f;
            aberration.intensity.value = Mathf.Lerp(aberration.intensity.value, sim.BoostPulse * 0.35f, 0.3f);
            vignette.intensity.value = 0.2f + intensity * 0.1f;
        }

        float flow = 1;

        /// <summary>
        /// Atmosphere over the whole scene: warm slanted sun shafts and drifting petals. Purely
        /// cosmetic and screen-space, so it never touches the simulation or the sprites.
        /// </summary>
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || Look.BookOpen) return;
            Look.Ensure();
            float W = Screen.width, H = Screen.height, k = H / 720f, t = Time.time;
            var pace = sim != null ? sim.Pace : 1f;
            flow = Mathf.Lerp(flow, pace, 0.05f);

            // Soft shafts of low sun, slowly breathing.
            for (var i = 0; i < 3; i++)
            {
                var width = (150 + i * 90) * k;
                var rect = new Rect(W * (0.55f + i * 0.16f) - width / 2, -H * 0.2f, width, H * 1.5f);
                var matrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(24, new Vector2(rect.center.x, 0));
                var a = (0.09f + 0.04f * Mathf.Sin(t * 0.5f + i * 2f)) * (i == 1 ? 1.3f : 1f);
                Look.FadeLeft(new Rect(rect.x, rect.y, rect.width / 2, rect.height), UIKit.WithAlpha(UIKit.Hex("#fff0c9"), a));
                Look.FadeRight(new Rect(rect.center.x, rect.y, rect.width / 2, rect.height), UIKit.WithAlpha(UIKit.Hex("#fff0c9"), a));
                GUI.matrix = matrix;
            }
            Look.Tex(new Rect(-W * 0.1f, H * 0.35f, W * 1.2f, H * 0.9f), Look.Glow, UIKit.WithAlpha(UIKit.Hex("#ffb27a"), 0.12f));

            // Petals: two depth layers, near ones larger and quicker.
            for (var i = 0; i < 26; i++)
            {
                var near = i % 3 == 0;
                var seed = i * 17.37f;
                var speed = (near ? 70f : 36f) * k;
                var drift = flow * (near ? 150f : 70f) * k;
                var x = Mathf.Repeat(Mathf.Sin(seed) * 9999f - t * drift - t * speed * 0.3f, W + 120 * k) - 60 * k;
                var y = Mathf.Repeat(Mathf.Sin(seed * 1.3f) * 7777f + t * speed, H + 80 * k) - 40 * k;
                x += Mathf.Sin(t * 1.3f + seed) * 14 * k;
                var size = (near ? 32f : 18f) * k * (0.8f + 0.4f * Mathf.Repeat(seed, 1f));
                var angle = t * (40 + i * 5) + seed * 30;
                var centre = new Vector2(x, y);
                var matrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, centre);
                var tint = i % 2 == 0 ? UIKit.Hex("#ffd1dc") : UIKit.Hex("#ffffff");
                Look.Tex(new Rect(x - size / 2, y - size / 2, size, size * 1.15f), Look.Heart, UIKit.WithAlpha(tint, near ? 0.75f : 0.5f));
                GUI.matrix = matrix;
            }
        }

        void OnDestroy()
        {
            if (profile != null) Destroy(profile);
        }
    }
}
