namespace Studio.PlanarReflections
{
    public enum PlanarReflectionBlurMode
    {
        None,
        /// <summary>Box-filtered mip chain. Roughness picks the LOD.</summary>
        Mips,
        /// <summary>Half-res Kawase blur in a second texture. Roughness blends sharp and blurred.</summary>
        Blurred
    }

    public enum PlanarReflectionMsaa
    {
        Off = 1,
        X2 = 2,
        X4 = 4,
        X8 = 8
    }

    public enum PlanarReflectionFormat
    {
        Hdr,
        HdrHalf,
        Ldr
    }

    public enum PlanarReflectionBlurBackend
    {
        /// <summary>Compute on DX12, raster everywhere else.</summary>
        Auto,
        Raster,
        Compute
    }
}
