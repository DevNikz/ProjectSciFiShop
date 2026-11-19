using System;
using UnityEngine;

namespace Studio.PlanarReflections
{
    internal readonly struct PlanarReflectionTextureSpec : IEquatable<PlanarReflectionTextureSpec>
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int DepthBits;
        public readonly int Samples;
        public readonly RenderTextureFormat Format;
        public readonly bool UseMips;
        public readonly bool RandomWrite;

        public PlanarReflectionTextureSpec(
            int width, int height, int depthBits, int samples,
            RenderTextureFormat format, bool useMips, bool randomWrite)
        {
            Width = width;
            Height = height;
            DepthBits = depthBits;
            Samples = samples;
            Format = format;
            UseMips = useMips;
            RandomWrite = randomWrite;
        }

        public bool Equals(PlanarReflectionTextureSpec other)
        {
            return Width == other.Width
                && Height == other.Height
                && DepthBits == other.DepthBits
                && Samples == other.Samples
                && Format == other.Format
                && UseMips == other.UseMips
                && RandomWrite == other.RandomWrite;
        }

        public override bool Equals(object obj)
        {
            return obj is PlanarReflectionTextureSpec other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Width;
                hash = (hash * 397) ^ Height;
                hash = (hash * 397) ^ DepthBits;
                hash = (hash * 397) ^ Samples;
                hash = (hash * 397) ^ (int)Format;
                hash = (hash * 397) ^ (UseMips ? 1 : 0);
                hash = (hash * 397) ^ (RandomWrite ? 1 : 0);
                return hash;
            }
        }
    }
}
