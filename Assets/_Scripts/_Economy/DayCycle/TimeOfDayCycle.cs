using System;
using UnityEngine;
using UnityEngine.Rendering;

public enum DayPhase
{
    Morning,
    Midday,
    Afternoon,
    Evening,
}

[Serializable]
public class DayPhaseSettings
{
    public DayPhase phase;

    [Tooltip("Hour (0-24) at which this phase is fully 'held'. Keep phases in ascending order.")]
    [Range(0f, 24f)]
    public float startHour = 7f;

    [Header("Sun")]
    [Range(2f, 90f)]
    public float sunPitch = 20f; // degrees above horizon

    [Tooltip("Keep yaw increasing Morning -> Evening so the sun sweeps one way.")]
    [Range(0f, 360f)]
    public float sunYaw = 100f;
    public Color lightColor = Color.white;

    [Min(0f)]
    public float intensity = 1f;

    [Header("Environment")]
    public Color ambientColor = Color.gray;
    public Color fogColor = Color.gray;

    [Header("Skybox (Skybox/Procedural)")]
    public Color skyTint = new Color(.5f, .5f, .5f);
    public Color groundColor = new Color(.37f, .35f, .34f);

    [Range(0f, 8f)]
    public float exposure = 1.3f;

    [Range(0f, 5f)]
    public float atmosphereThickness = 1f;
}

/// <summary>
/// Drives the main Directional Light (and optional ambient/fog/procedural skybox) through
/// Morning -> Midday -> Afternoon -> Evening. Unity 6 / URP (works with Forward+).
/// Attach to any GameObject (e.g. "TimeOfDay"), assign the sun.
/// </summary>
[ExecuteAlways]
public class TimeOfDayCycle : MonoBehaviour
{
    static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
    static readonly int GroundColorId = Shader.PropertyToID("_GroundColor");

    // static readonly int SkyTintId = Shader.PropertyToID("_HorizonColor");
    // static readonly int GroundColorId = Shader.PropertyToID("_ZenithColor");
    static readonly int ExposureId = Shader.PropertyToID("_Exposure");
    static readonly int AtmosphereId = Shader.PropertyToID("_AtmosphereThickness");

    [Header("References")]
    [SerializeField]
    Light sun;

    [Tooltip(
        "Material using Skybox/Procedural (e.g. DefaultSky). Leave empty to use the scene's current skybox."
    )]
    [SerializeField]
    Material skyMaterial;

    [Header("Clock")]
    [Range(0f, 24f)]
    public float timeOfDay = 8f;

    [Tooltip("Real seconds for a full 24h loop.")]
    [Min(1f)]
    public float dayLengthSeconds = 240f;
    public bool autoAdvance = true;

    [Header("Blending")]
    [Tooltip("Fraction of each phase spent holding its look before blending to the next.")]
    [Range(0f, 0.9f)]
    public float holdFraction = 0.4f;

    [Tooltip(
        "Evening -> Morning has no night phase, so dim the sun and sky to 0 mid-blend instead of spinning the sun visibly."
    )]
    public bool fadeThroughNight = true;

    [Header("What this drives")]
    public bool driveAmbient = true; // sets AmbientMode.Flat
    public bool driveFog = false; // only color; enable Fog in Lighting settings yourself

    [Tooltip(
        "Tint/ground/exposure/atmosphere on the procedural skybox. The sun disk and sky gradient already follow the sun's rotation via RenderSettings.sun."
    )]
    public bool driveSkybox = true;

    [Header("Reflections (optional)")]
    [Tooltip(
        "Re-bake the skybox reflection probe periodically in Play Mode so reflections track the sky. Costs a GPU/CPU update each interval; profile before enabling."
    )]
    public bool updateReflections = false;

    [Min(0.25f)]
    public float reflectionUpdateInterval = 2f;

    [Header("Phases (ascending startHour)")]
    public DayPhaseSettings[] phases;

    public DayPhase CurrentPhase { get; private set; }
    public event Action<DayPhase> PhaseChanged;

    // Play Mode uses a private instance so the DefaultSky asset is never modified.
    Material runtimeSky;
    Material originalSky;
    float reflectionTimer;

    /// <summary>Jump the clock to a phase (gameplay/cutscene hook).</summary>
    public void SetPhase(DayPhase phase)
    {
        foreach (var p in phases)
            if (p.phase == phase)
            {
                timeOfDay = p.startHour;
                Apply();
                return;
            }
    }

    /// <summary>Set the clock (wraps to 0-24) and apply immediately. Use when another system owns time.</summary>
    public void SetTime(float hour)
    {
        timeOfDay = Mathf.Repeat(hour, 24f);
        Apply();
    }

    void Reset()
    {
        sun = RenderSettings.sun;
        skyMaterial = RenderSettings.skybox;
        phases = new[]
        {
            Make(
                DayPhase.Morning,
                7f,
                18f,
                100f,
                new Color(1f, .82f, .65f),
                1.0f,
                new Color(.55f, .60f, .70f),
                new Color(.75f, .70f, .65f),
                new Color(.60f, .50f, .50f),
                new Color(.37f, .35f, .34f),
                1.2f,
                0.9f
            ),
            Make(
                DayPhase.Midday,
                12f,
                72f,
                180f,
                new Color(1f, .97f, .90f),
                1.3f,
                new Color(.60f, .70f, .85f),
                new Color(.70f, .80f, .90f),
                new Color(.50f, .50f, .50f),
                new Color(.40f, .38f, .35f),
                1.3f,
                1.0f
            ),
            Make(
                DayPhase.Afternoon,
                16f,
                40f,
                240f,
                new Color(1f, .90f, .75f),
                1.1f,
                new Color(.60f, .62f, .70f),
                new Color(.80f, .78f, .72f),
                new Color(.55f, .50f, .45f),
                new Color(.37f, .34f, .30f),
                1.2f,
                1.1f
            ),
            Make(
                DayPhase.Evening,
                19.5f,
                8f,
                280f,
                new Color(1f, .50f, .28f),
                0.7f,
                new Color(.35f, .30f, .40f),
                new Color(.55f, .35f, .30f),
                new Color(.60f, .40f, .35f),
                new Color(.25f, .20f, .20f),
                0.9f,
                1.6f
            ),
        };
    }

    static DayPhaseSettings Make(
        DayPhase ph,
        float hour,
        float pitch,
        float yaw,
        Color light,
        float intensity,
        Color amb,
        Color fog,
        Color skyTint,
        Color groundColor,
        float exposure,
        float atmosphere
    ) =>
        new DayPhaseSettings
        {
            phase = ph,
            startHour = hour,
            sunPitch = pitch,
            sunYaw = yaw,
            lightColor = light,
            intensity = intensity,
            ambientColor = amb,
            fogColor = fog,
            skyTint = skyTint,
            groundColor = groundColor,
            exposure = exposure,
            atmosphereThickness = atmosphere,
        };

    void OnEnable()
    {
        if (sun == null)
            sun = RenderSettings.sun;
        Apply();
    }

    void OnDisable() => ReleaseRuntimeSky();

    void OnValidate() => Apply(); // scrub timeOfDay in the Inspector, even in Edit Mode

    void Update()
    {
        if (Application.isPlaying && autoAdvance)
            timeOfDay = Mathf.Repeat(timeOfDay + Time.deltaTime * 24f / dayLengthSeconds, 24f);
        Apply();

        if (Application.isPlaying && driveSkybox && updateReflections)
        {
            reflectionTimer += Time.deltaTime;
            if (reflectionTimer >= reflectionUpdateInterval)
            {
                reflectionTimer = 0f;
                DynamicGI.UpdateEnvironment();
            }
        }
    }

    void Apply()
    {
        if (sun == null || phases == null || phases.Length < 2)
            return;
        RenderSettings.sun = sun; // the procedural skybox reads this light for sun disk + scattering

        int n = phases.Length;
        int a = -1;
        for (int i = 0; i < n; i++)
            if (timeOfDay >= phases[i].startHour)
                a = i;

        float aStart,
            bStart;
        int b;
        if (a < 0)
        {
            a = n - 1;
            aStart = phases[a].startHour - 24f;
            b = 0;
            bStart = phases[0].startHour;
        }
        else
        {
            aStart = phases[a].startHour;
            b = (a + 1) % n;
            bStart = b > a ? phases[b].startHour : phases[b].startHour + 24f;
        }
        bool wrap = a == n - 1; // Evening -> Morning segment

        float u = Mathf.InverseLerp(aStart, bStart, timeOfDay);
        float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(holdFraction, 1f, u));

        var A = phases[a];
        var B = phases[b];

        // 1 normally; dips to 0 at the middle of the Evening -> Morning blend.
        float nightFade = wrap && fadeThroughNight ? Mathf.Abs(2f * k - 1f) : 1f;

        float pitch = Mathf.Lerp(A.sunPitch, B.sunPitch, k);
        float yaw = Mathf.Lerp(A.sunYaw, wrap ? B.sunYaw + 360f : B.sunYaw, k);
        float inten = Mathf.Lerp(A.intensity, B.intensity, k) * nightFade;

        sun.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        sun.color = Color.Lerp(A.lightColor, B.lightColor, k);
        sun.intensity = inten;

        if (driveAmbient)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(A.ambientColor, B.ambientColor, k);
        }
        if (driveFog)
            RenderSettings.fogColor = Color.Lerp(A.fogColor, B.fogColor, k);

        if (driveSkybox)
            ApplySkybox(A, B, k, nightFade);
        else
            ReleaseRuntimeSky();

        var current = u < 0.5f ? A.phase : B.phase;
        if (current != CurrentPhase)
        {
            CurrentPhase = current;
            if (Application.isPlaying)
                PhaseChanged?.Invoke(current);
        }
    }

    void ApplySkybox(DayPhaseSettings A, DayPhaseSettings B, float k, float nightFade)
    {
        var mat = ResolveSkyMaterial();
        if (mat == null)
            return;

        // Guard against a non-procedural skybox shader being assigned.
        if (mat.HasProperty(SkyTintId))
            mat.SetColor(SkyTintId, Color.Lerp(A.skyTint, B.skyTint, k));
        if (mat.HasProperty(GroundColorId))
            mat.SetColor(GroundColorId, Color.Lerp(A.groundColor, B.groundColor, k));
        if (mat.HasProperty(ExposureId))
            mat.SetFloat(ExposureId, Mathf.Lerp(A.exposure, B.exposure, k) * nightFade);
        if (mat.HasProperty(AtmosphereId))
            mat.SetFloat(AtmosphereId, Mathf.Lerp(A.atmosphereThickness, B.atmosphereThickness, k));
    }

    Material ResolveSkyMaterial()
    {
        if (!Application.isPlaying)
        {
            // Edit Mode: edit the real asset so scrubbing shows up in the Scene/Game view.
            // Note: this dirties the skybox material asset.
            var m = skyMaterial != null ? skyMaterial : RenderSettings.skybox;
            if (m != null && RenderSettings.skybox != m)
                RenderSettings.skybox = m;
            return m;
        }

        if (runtimeSky == null)
        {
            var src = skyMaterial != null ? skyMaterial : RenderSettings.skybox;
            if (src == null)
                return null;
            originalSky = RenderSettings.skybox;
            runtimeSky = new Material(src)
            {
                name = src.name + " (TimeOfDay)",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
        if (RenderSettings.skybox != runtimeSky)
            RenderSettings.skybox = runtimeSky;
        return runtimeSky;
    }

    void ReleaseRuntimeSky()
    {
        if (runtimeSky == null)
            return;
        if (RenderSettings.skybox == runtimeSky)
            RenderSettings.skybox = originalSky;
        if (Application.isPlaying)
            Destroy(runtimeSky);
        else
            DestroyImmediate(runtimeSky);
        runtimeSky = null;
        originalSky = null;
    }
}
