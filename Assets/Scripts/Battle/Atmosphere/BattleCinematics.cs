using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EmpireBattle
{
    // Camera-only presentation: never changes the simulation clock or UI rendering.
    [DefaultExecutionOrder(100)]
    public sealed class BattleCinematics : MonoBehaviour
    {
        public bool CinemaEnabled = true;
        public bool FocusEnabled = false;
        public bool BloomEnabled=true, ColorEnabled=true, VignetteEnabled=true;
        Bloom bloom;
        ColorAdjustments color;
        Tonemapping tonemapping;
        SplitToning split;
        Vignette vignette;
        BattleView view;
        Volume volume;
        VolumeProfile profile;
        DepthOfField focus;
        float focusDepth;

        public static void ConfigureProfile(VolumeProfile profile)
        {
            profile.Add<Tonemapping>(true).mode.value = TonemappingMode.ACES;
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.value = .45f;
            color.contrast.value = 3f;
            color.saturation.value = -3f;
            var split = profile.Add<SplitToning>(true);
            split.shadows.value = new Color(.49f, .50f, .51f);
            split.highlights.value = new Color(.53f, .51f, .47f);
            split.balance.value = 5f;
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 1.1f;
            bloom.intensity.value = .18f;
            bloom.scatter.value = .55f;
            bloom.highQualityFiltering.value = false;
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = .08f;
            vignette.smoothness.value = .55f;
            var depth = profile.Add<DepthOfField>(true);
            depth.mode.value = DepthOfFieldMode.Gaussian;
            depth.gaussianStart.value = 30f;
            depth.gaussianEnd.value = 110f;
            depth.gaussianMaxRadius.value = .8f;
        }

        public static void ApplyReadabilityGrade(VolumeProfile profile)
        {
            if(profile.TryGet<ColorAdjustments>(out var color)){
                color.postExposure.value=.45f;color.contrast.value=3f;color.saturation.value=-3f;
            }
            if(profile.TryGet<SplitToning>(out var split))split.shadows.value=new Color(.49f,.50f,.51f);
            if(profile.TryGet<Vignette>(out var vignette))vignette.intensity.value=.08f;
        }

        public void Initialize(BattleView battleView)
        {
            view = battleView;
            var camera = view.BattleCamera;
            camera.allowHDR = true;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.requiresDepthTexture = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            // A saved Resources profile keeps the required player shader variants.
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20;
            var saved = Resources.Load<VolumeProfile>("BattleCinematicProfile");
            if (saved) volume.sharedProfile = saved;
            profile = volume.profile;
            if (!saved) ConfigureProfile(profile);
            ApplyReadabilityGrade(profile);
            profile.TryGet(out focus);
            profile.TryGet(out bloom);profile.TryGet(out color);profile.TryGet(out tonemapping);profile.TryGet(out split);profile.TryGet(out vignette);
            focusDepth = view.CameraFocusDepth;
            LateUpdate();
        }

        void LateUpdate()
        {
            if (!view || !volume || focus == null) return;
            if(bloom!=null)bloom.active=BloomEnabled;
            if(color!=null)color.active=ColorEnabled;
            if(tonemapping!=null)tonemapping.active=ColorEnabled;
            if(split!=null)split.active=ColorEnabled;
            if(vignette!=null)vignette.active=VignetteEnabled;
            volume.weight = CinemaEnabled ? 1f - view.AtlasBlend : 0f;
            float target = view.CameraFocusDepth;
            focusDepth = Mathf.Lerp(focusDepth, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 7f));
            float close = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(40f, 150f, target));
            focus.active = CinemaEnabled && FocusEnabled && close > .01f;
            // Keep the focal formation and foreground sharp; soften only its background.
            focus.gaussianStart.value = focusDepth + Mathf.Max(12f, focusDepth * .5f);
            focus.gaussianEnd.value = focus.gaussianStart.value + Mathf.Lerp(180f, 55f, close);
            focus.gaussianMaxRadius.value = Mathf.Lerp(.5f, 1f, close);
        }

        void OnDestroy()
        {
            if (!profile) return;
            foreach (var component in profile.components) if (component) Destroy(component);
            Destroy(profile);
        }
    }
}
