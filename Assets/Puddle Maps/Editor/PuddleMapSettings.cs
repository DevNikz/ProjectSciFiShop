// Serializable generator parameters. Validate() clamps every field (same pattern as SSRSettings.Validate).
using System;
using UnityEngine;

namespace PuddleMaps
{
    public enum PuddleKind { Scattered, Pooled, Cracked, Ruts, Cobblestone }
    public enum PuddlePreset { ScatteredRain, RoadBasins, CrackedDirt, WheelRuts, CobblesWet }

    [Serializable]
    public sealed class PuddleMapSettings
    {
        public PuddleKind kind = PuddleKind.Scattered;
        [Tooltip("0 = not yet chosen (the window picks a random seed on first open).")]
        public int seed = 0;
        public bool tileable = true;

        [Header("Noise")]
        public int noiseScale = 4;
        public int octaves = 5;
        [Tooltip("Ignored (forced to 2) when Tileable is on.")] public float lacunarity = 2f;
        public float gain = 0.5f;

        [Header("Water")]
        [Tooltip("Fraction of the ground that floods (lowest areas first), independent of seed and kind.")] public float coverage = 0.25f;
        public float edgeSoftness = 0.06f;
        public float edgeNoise = 0.3f;

        [Header("Surface")]
        public float normalStrength = 1f;
        public float rippleAmount = 0.3f;
        [Tooltip("Smoothness of dry ground (puddles are always 1).")] public float dryGloss = 0.1f;
        [Tooltip("Albedo multiplier inside puddles (1 = no darkening).")] public float wetAlbedoMultiplier = 0.65f;

        [Header("Kind: Pooled")] public float pooledDetail = 0.3f;
        [Header("Kind: Cracked / Cobblestone")] public int cellCount = 8;
        [Header("Kind: Cracked")] public float crackWidth = 0.12f; public float crackDepth = 0.8f; public float crackWobble = 0.4f;
        [Header("Kind: Ruts")] public int rutLanes = 3; public float rutSpacing = 0.3f; public float rutWidth = 0.08f; public float rutBreakup = 0.6f; public float rutWobble = 0.4f;
        [Header("Kind: Cobblestone")] public float gapWidth = 0.12f; public float gapSoftness = 0.1f; public float stoneVariation = 0.25f;

        public PuddleMapSettings Clone() => (PuddleMapSettings)MemberwiseClone();

        public void Validate()
        {
            noiseScale = Mathf.Clamp(noiseScale, 1, 32);
            octaves = Mathf.Clamp(octaves, 1, 8);
            lacunarity = Mathf.Clamp(lacunarity, 1.5f, 3f);
            gain = Mathf.Clamp(gain, 0.2f, 0.8f);
            coverage = Mathf.Clamp01(coverage);
            edgeSoftness = Mathf.Clamp(edgeSoftness, 0.005f, 0.3f);
            edgeNoise = Mathf.Clamp01(edgeNoise);
            normalStrength = Mathf.Clamp(normalStrength, 0f, 4f);
            rippleAmount = Mathf.Clamp01(rippleAmount);
            dryGloss = Mathf.Clamp01(dryGloss);
            wetAlbedoMultiplier = Mathf.Clamp(wetAlbedoMultiplier, 0.2f, 1f);
            pooledDetail = Mathf.Clamp01(pooledDetail);
            cellCount = Mathf.Clamp(cellCount, 2, 32);
            crackWidth = Mathf.Clamp(crackWidth, 0.02f, 0.5f);
            crackDepth = Mathf.Clamp01(crackDepth);
            crackWobble = Mathf.Clamp01(crackWobble);
            rutLanes = Mathf.Clamp(rutLanes, 1, 16);
            rutSpacing = Mathf.Clamp(rutSpacing, 0.05f, 0.9f);
            rutWidth = Mathf.Clamp(rutWidth, 0.01f, 0.3f);
            rutBreakup = Mathf.Clamp01(rutBreakup);
            rutWobble = Mathf.Clamp01(rutWobble);
            gapWidth = Mathf.Clamp(gapWidth, 0.01f, 0.5f);
            gapSoftness = Mathf.Clamp(gapSoftness, 0.01f, 0.5f);
            stoneVariation = Mathf.Clamp01(stoneVariation);
        }

        /// <summary>Output resolution: nearest power of two in 64..4096.</summary>
        public static int ClampResolution(int r) => Mathf.ClosestPowerOfTwo(Mathf.Clamp(r, 64, 4096));

        /// <summary>Loads a starting point. Keeps seed and tileable; everything stays editable afterwards.</summary>
        public void ApplyPreset(PuddlePreset p)
        {
            int keepSeed = seed; bool keepTile = tileable;
            var d = new PuddleMapSettings();
            switch (p)
            {
                case PuddlePreset.ScatteredRain: d.kind = PuddleKind.Scattered; d.noiseScale = 4; d.coverage = 0.25f; d.edgeSoftness = 0.05f; d.rippleAmount = 0.4f; break;
                case PuddlePreset.RoadBasins:    d.kind = PuddleKind.Pooled; d.noiseScale = 4; d.coverage = 0.22f; d.pooledDetail = 0.25f; d.edgeSoftness = 0.08f; break;
                case PuddlePreset.CrackedDirt:   d.kind = PuddleKind.Cracked; d.cellCount = 8; d.coverage = 0.2f; d.crackWidth = 0.12f; d.crackDepth = 0.8f; break;
                case PuddlePreset.WheelRuts:     d.kind = PuddleKind.Ruts; d.rutLanes = 3; d.coverage = 0.15f; d.rutWidth = 0.08f; d.rutBreakup = 0.6f; break;
                case PuddlePreset.CobblesWet:    d.kind = PuddleKind.Cobblestone; d.cellCount = 8; d.coverage = 0.3f; d.gapWidth = 0.12f; d.stoneVariation = 0.25f; break;
            }
            d.seed = keepSeed; d.tileable = keepTile;
            CopyFrom(d);
        }

        void CopyFrom(PuddleMapSettings o)
        {
            foreach (var f in typeof(PuddleMapSettings).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                f.SetValue(this, f.GetValue(o));
        }
    }
}
