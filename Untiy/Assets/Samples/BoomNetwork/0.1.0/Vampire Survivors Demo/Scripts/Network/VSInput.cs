// BoomNetwork VampireSurvivors Demo — Input Encoding
//
// 4-byte input format:
//   [0] sbyte: movement X (dirX * 127)
//   [1] sbyte: movement Z (dirZ * 127)
//   [2] byte:  ability bitmask (reserved for upgrades)
//   [3] byte:  reserved

namespace BoomNetwork.Samples.VampireSurvivors
{
    public static class VSInput
    {
        public const int InputSize = 4;

        public static void Encode(byte[] buf, float dirX, float dirZ, byte abilityMask = 0)
        {
            int ix = (int)(dirX * 127f);
            int iz = (int)(dirZ * 127f);
            if (ix < -128) ix = -128; else if (ix > 127) ix = 127;
            if (iz < -128) iz = -128; else if (iz > 127) iz = 127;
            buf[0] = (byte)(sbyte)ix;
            buf[1] = (byte)(sbyte)iz;
            buf[2] = abilityMask;
            buf[3] = 0;
        }

        public static void Decode(byte[] buf, int offset,
            out float dirX, out float dirZ, out byte abilityMask)
        {
            dirX = (sbyte)buf[offset] / 127f;
            dirZ = (sbyte)buf[offset + 1] / 127f;
            abilityMask = buf[offset + 2];
        }
    }
}
