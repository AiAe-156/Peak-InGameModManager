#nullable enable
namespace ModConfigEnhance.Overview;
internal static class ShopImageBounds
{
    internal static bool Allows(byte[] b, int maximum)
    {
        bool Size(long w, long h) => w > 0 && h > 0 && w <= maximum && h <= maximum;
        long Big(int p) => ((long)b[p] << 24) | ((long)b[p+1] << 16) | ((long)b[p+2] << 8) | b[p+3];
        if (b.Length >= 24 && b[0] == 137 && b[1] == 80 && b[2] == 78 && b[3] == 71 && b[4] == 13 && b[5] == 10 && b[6] == 26 && b[7] == 10)
            return Size(Big(16), Big(20));
        if (b.Length < 4 || b[0] != 255 || b[1] != 216) return false;
        int p = 2;
        while (p + 4 <= b.Length)
        {
            if (b[p++] != 255) return false;
            while (p < b.Length && b[p] == 255) p++;
            if (p >= b.Length) return false;
            int marker = b[p++];
            if (marker == 217 || marker == 218) return false;
            if (marker == 1 || (marker >= 208 && marker <= 215)) continue;
            if (p + 2 > b.Length) return false;
            int length = b[p] * 256 + b[p+1];
            if (length < 2 || p + length > b.Length) return false;
            if ((marker >= 192 && marker <= 195) || (marker >= 197 && marker <= 199) || (marker >= 201 && marker <= 203) || (marker >= 205 && marker <= 207))
                return length >= 7 && Size(b[p+5] * 256 + b[p+6], b[p+3] * 256 + b[p+4]);
            p += length;
        }
        return false;
    }
}
