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
        }

        void OnDestroy()
        {
            if (profile != null) Destroy(profile);
        }
    }
}
