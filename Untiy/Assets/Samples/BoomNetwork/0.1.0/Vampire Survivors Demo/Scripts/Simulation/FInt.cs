// BoomNetwork VampireSurvivors Demo — Deterministic Fixed-Point Number
//
// 22.10 fixed-point: 22-bit integer + 10-bit fraction.
// All arithmetic is integer-only — bit-level identical across all platforms.
// Sin/Cos/Sqrt use precomputed lookup tables, no floating-point at runtime.
//
// Range: ±2,097,151.999  Precision: ~0.001 (1/1024)

using System;
using System.Runtime.CompilerServices;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public struct FInt : IEquatable<FInt>, IComparable<FInt>
    {
        public const int SHIFT = 10;
        public const int SCALE = 1 << SHIFT; // 1024

        public int Raw;

        // ==================== Construction ====================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FInt(int raw) { Raw = raw; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt FromInt(int v) => new FInt(v << SHIFT);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt FromFloat(float v) => new FInt((int)(v * SCALE));

        // ==================== Conversion (Rendering only!) ====================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ToFloat() => Raw * (1f / SCALE);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ToInt() => Raw >> SHIFT;

        // ==================== Constants ====================

        public static readonly FInt Zero = new FInt(0);
        public static readonly FInt One = new FInt(SCALE);
        public static readonly FInt Half = new FInt(SCALE / 2);
        public static readonly FInt MinValue = new FInt(int.MinValue);
        public static readonly FInt MaxValue = new FInt(int.MaxValue);
        public static readonly FInt Epsilon = new FInt(1);
        public static readonly FInt Pi = new FInt(3217);       // π × 1024 ≈ 3216.99
        public static readonly FInt TwoPi = new FInt(6434);
        public static readonly FInt Deg2Rad = new FInt(18);    // (π/180) × 1024 ≈ 17.87

        // ==================== Operators ====================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt operator +(FInt a, FInt b) => new FInt(a.Raw + b.Raw);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt operator -(FInt a, FInt b) => new FInt(a.Raw - b.Raw);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt operator -(FInt a) => new FInt(-a.Raw);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt operator *(FInt a, FInt b) => new FInt((int)((long)a.Raw * b.Raw >> SHIFT));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt operator /(FInt a, FInt b) => new FInt((int)(((long)a.Raw << SHIFT) / b.Raw));

        // FInt × int (no shift needed for the int side)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt operator *(FInt a, int b) => new FInt(a.Raw * b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt operator *(int a, FInt b) => new FInt(a * b.Raw);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt operator /(FInt a, int b) => new FInt(a.Raw / b);

        // ==================== Comparison ====================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(FInt a, FInt b) => a.Raw > b.Raw;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(FInt a, FInt b) => a.Raw < b.Raw;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(FInt a, FInt b) => a.Raw >= b.Raw;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(FInt a, FInt b) => a.Raw <= b.Raw;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FInt a, FInt b) => a.Raw == b.Raw;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FInt a, FInt b) => a.Raw != b.Raw;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(FInt other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is FInt f && Raw == f.Raw;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => Raw;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(FInt other) => Raw.CompareTo(other.Raw);

        // ==================== Math ====================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt Abs(FInt v) => new FInt(v.Raw < 0 ? -v.Raw : v.Raw);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt Min(FInt a, FInt b) => a.Raw < b.Raw ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt Max(FInt a, FInt b) => a.Raw > b.Raw ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt Clamp(FInt v, FInt min, FInt max)
        {
            if (v.Raw < min.Raw) return min;
            if (v.Raw > max.Raw) return max;
            return v;
        }

        /// <summary>Linear interpolation. t is in [0,1] fixed-point.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt Lerp(FInt a, FInt b, FInt t)
        {
            return new FInt(a.Raw + (int)(((long)(b.Raw - a.Raw) * t.Raw) >> SHIFT));
        }

        /// <summary>Move value towards target by at most maxDelta per call.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt MoveTowards(FInt current, FInt target, FInt maxDelta)
        {
            int diff = target.Raw - current.Raw;
            if (diff > maxDelta.Raw) return new FInt(current.Raw + maxDelta.Raw);
            if (diff < -maxDelta.Raw) return new FInt(current.Raw - maxDelta.Raw);
            return target;
        }

        /// <summary>Integer square root in fixed-point. Binary algorithm — pure bitwise, no division.</summary>
        public static FInt Sqrt(FInt v)
        {
            if (v.Raw <= 0) return Zero;

            // Binary square root: result.Raw = isqrt(v.Raw << SHIFT)
            ulong val = (ulong)v.Raw << SHIFT;
            ulong result = 0;
            ulong bit = 1UL << 30; // start from highest even bit

            while (bit > val) bit >>= 2;
            while (bit != 0)
            {
                ulong t = result + bit;
                result >>= 1;
                if (val >= t)
                {
                    val -= t;
                    result += bit;
                }
                bit >>= 2;
            }
            return new FInt((int)result);
        }

        /// <summary>1 / sqrt(v). Avoids separate Sqrt + division. Deterministic.</summary>
        public static FInt InvSqrt(FInt v)
        {
            if (v.Raw <= 0) return Zero;

            // Binary square root then invert
            ulong val = (ulong)v.Raw << SHIFT;
            ulong result = 0;
            ulong bit = 1UL << 30;

            while (bit > val) bit >>= 2;
            while (bit != 0)
            {
                ulong t = result + bit;
                result >>= 1;
                if (val >= t)
                {
                    val -= t;
                    result += bit;
                }
                bit >>= 2;
            }
            if (result == 0) return MaxValue;
            return new FInt((int)(((long)SCALE * SCALE) / (long)result));
        }

        /// <summary>Distance squared between two 2D points. Use for comparison to avoid Sqrt.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt DistanceSqr(FInt ax, FInt az, FInt bx, FInt bz)
        {
            long dx = ax.Raw - bx.Raw;
            long dz = az.Raw - bz.Raw;
            return new FInt((int)((dx * dx + dz * dz) >> SHIFT));
        }

        /// <summary>Magnitude squared of a 2D vector. Use for comparison to avoid Sqrt.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt LengthSqr(FInt x, FInt z)
        {
            return new FInt((int)(((long)x.Raw * x.Raw + (long)z.Raw * z.Raw) >> SHIFT));
        }

        // ==================== Trigonometry (Lookup Table) ====================

        // 1024 entries covering [0°, 360°), precomputed at class init.
        // Resolution: 360/1024 ≈ 0.35° per step.
        const int TABLE_SIZE = 1024;
        const int TABLE_MASK = TABLE_SIZE - 1;
        static readonly int[] SinTable;

        static FInt()
        {
            // Computed once at startup using double-precision Math.
            // The resulting integer table is identical on all platforms.
            SinTable = new int[TABLE_SIZE];
            for (int i = 0; i < TABLE_SIZE; i++)
            {
                double angle = i * 2.0 * 3.14159265358979323846 / TABLE_SIZE;
                SinTable[i] = (int)Math.Round(Math.Sin(angle) * SCALE);
            }
        }

        /// <summary>Sin of angle in degrees. Deterministic lookup.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt SinDeg(FInt degrees)
        {
            // idx = deg * TABLE_SIZE / (360 * SCALE)
            long idx = ((long)degrees.Raw * TABLE_SIZE) / (360 * SCALE);
            return new FInt(SinTable[(int)(idx & TABLE_MASK)]);
        }

        /// <summary>Cos of angle in degrees. Deterministic lookup.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt CosDeg(FInt degrees)
        {
            // cos(x) = sin(x + 90)
            long idx = ((long)(degrees.Raw + 90 * SCALE) * TABLE_SIZE) / (360 * SCALE);
            return new FInt(SinTable[(int)(idx & TABLE_MASK)]);
        }

        /// <summary>Sin of angle in radians (FInt). Deterministic lookup.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt Sin(FInt radians)
        {
            // idx = rad * TABLE_SIZE / (2π), 2π in fixed-point = 6434
            long idx = ((long)radians.Raw * TABLE_SIZE) / 6434;
            return new FInt(SinTable[(int)(idx & TABLE_MASK)]);
        }

        /// <summary>Cos of angle in radians (FInt). Deterministic lookup.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FInt Cos(FInt radians)
        {
            long idx = ((long)radians.Raw * TABLE_SIZE) / 6434 + TABLE_SIZE / 4; // +90°
            return new FInt(SinTable[(int)(idx & TABLE_MASK)]);
        }

        // ==================== Debug ====================

        public override string ToString() => ToFloat().ToString("F3");
    }
}
