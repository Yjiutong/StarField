using System;

namespace StarFieldAlgo
{
    /// <summary>
    /// 13-character seed codec.
    /// A seed is a 13-character string, each character being a letter or digit
    /// (36^13 possible codes). The same code always maps to the same seed hash,
    /// so the same code produces an identical star field.
    /// 十三位种子编码：每位为字母或数字（0-9A-Za-z），同一编码 → 同一确定性哈希 → 同一底图。
    /// </summary>
    public static class SeedCodec
    {
        public const int SeedLength = 13;

        /// <summary>Valid iff exactly 13 characters, each in [0-9A-Za-z].</summary>
        public static bool IsValid(string code)
        {
            if (code == null || code.Length != SeedLength) return false;
            foreach (char c in code)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// Deterministic 32-bit hash of the seed string (FNV-1a).
        /// Case-sensitive: different case → different hash. Same string → same hash.
        /// </summary>
        public static uint Hash(string code)
        {
            uint h = 2166136261u;
            foreach (char c in code)
            {
                h ^= (byte)c;
                h *= 16777619u;
            }
            return h;
        }

        /// <summary>
        /// Decode the 13-char code to a seed integer (32-bit, lower bits of base-36 decoding).
        /// Same code → same seed; used as the deterministic generator seed.
        /// </summary>
        public static uint DecodeBase36(string code)
        {
            uint v = 0;
            foreach (char c in code)
            {
                v *= 36u;
                if (c >= '0' && c <= '9') v += (uint)(c - '0');
                else if (c >= 'A' && c <= 'Z') v += (uint)(c - 'A' + 10);
                else if (c >= 'a' && c <= 'z') v += (uint)(c - 'a' + 10);
            }
            return v;   // 13 digits overflow 32 bits; lower 32 bits kept (still deterministic)
        }

        /// <summary>Canonical example seed (13 chars).</summary>
        public const string ExampleSeed = "4s5t7gb7s8f75";
    }
}
