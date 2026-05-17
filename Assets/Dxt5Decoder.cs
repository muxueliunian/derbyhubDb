namespace derbyhubDb.Assets;

public static class Dxt5Decoder
{
    public static byte[] DecodeRgba(byte[] data, int width, int height, bool flipVertical)
    {
        if (width <= 0 || height <= 0 || width % 4 != 0 || height % 4 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "DXT5 尺寸必须为 4 的倍数");
        }

        var expected = width * height;
        if (data.Length < expected)
        {
            throw new InvalidDataException($"DXT5 数据不足: {data.Length}/{expected}");
        }

        var output = new byte[width * height * 4];
        var offset = 0;
        for (var blockY = 0; blockY < height; blockY += 4)
        {
            for (var blockX = 0; blockX < width; blockX += 4)
            {
                DecodeBlock(data, offset, output, width, height, blockX, blockY, flipVertical);
                offset += 16;
            }
        }

        return output;
    }

    private static void DecodeBlock(
        byte[] data,
        int offset,
        byte[] output,
        int width,
        int height,
        int blockX,
        int blockY,
        bool flipVertical)
    {
        var alphas = BuildAlphaTable(data[offset], data[offset + 1]);
        ulong alphaBits = 0;
        for (var i = 0; i < 6; i++)
        {
            alphaBits |= (ulong)data[offset + 2 + i] << (8 * i);
        }

        var color0 = ReadUInt16Le(data, offset + 8);
        var color1 = ReadUInt16Le(data, offset + 10);
        var colors = BuildColorTable(color0, color1);
        var colorBits = ReadUInt32Le(data, offset + 12);

        for (var py = 0; py < 4; py++)
        {
            for (var px = 0; px < 4; px++)
            {
                var pixel = py * 4 + px;
                var alphaIndex = (int)((alphaBits >> (3 * pixel)) & 0x7);
                var colorIndex = (int)((colorBits >> (2 * pixel)) & 0x3);

                var x = blockX + px;
                var y = blockY + py;
                if (x >= width || y >= height)
                {
                    continue;
                }

                if (flipVertical)
                {
                    y = height - 1 - y;
                }

                var target = (y * width + x) * 4;
                output[target] = colors[colorIndex].R;
                output[target + 1] = colors[colorIndex].G;
                output[target + 2] = colors[colorIndex].B;
                output[target + 3] = alphas[alphaIndex];
            }
        }
    }

    private static byte[] BuildAlphaTable(byte alpha0, byte alpha1)
    {
        var table = new byte[8];
        table[0] = alpha0;
        table[1] = alpha1;
        if (alpha0 > alpha1)
        {
            table[2] = (byte)((6 * alpha0 + alpha1) / 7);
            table[3] = (byte)((5 * alpha0 + 2 * alpha1) / 7);
            table[4] = (byte)((4 * alpha0 + 3 * alpha1) / 7);
            table[5] = (byte)((3 * alpha0 + 4 * alpha1) / 7);
            table[6] = (byte)((2 * alpha0 + 5 * alpha1) / 7);
            table[7] = (byte)((alpha0 + 6 * alpha1) / 7);
        }
        else
        {
            table[2] = (byte)((4 * alpha0 + alpha1) / 5);
            table[3] = (byte)((3 * alpha0 + 2 * alpha1) / 5);
            table[4] = (byte)((2 * alpha0 + 3 * alpha1) / 5);
            table[5] = (byte)((alpha0 + 4 * alpha1) / 5);
            table[6] = 0;
            table[7] = 255;
        }

        return table;
    }

    private static Rgb[] BuildColorTable(ushort color0, ushort color1)
    {
        var c0 = DecodeRgb565(color0);
        var c1 = DecodeRgb565(color1);
        var table = new Rgb[4];
        table[0] = c0;
        table[1] = c1;
        if (color0 > color1)
        {
            table[2] = new Rgb(
                (byte)((2 * c0.R + c1.R) / 3),
                (byte)((2 * c0.G + c1.G) / 3),
                (byte)((2 * c0.B + c1.B) / 3));
            table[3] = new Rgb(
                (byte)((c0.R + 2 * c1.R) / 3),
                (byte)((c0.G + 2 * c1.G) / 3),
                (byte)((c0.B + 2 * c1.B) / 3));
        }
        else
        {
            table[2] = new Rgb(
                (byte)((c0.R + c1.R) / 2),
                (byte)((c0.G + c1.G) / 2),
                (byte)((c0.B + c1.B) / 2));
            table[3] = new Rgb(0, 0, 0);
        }

        return table;
    }

    private static Rgb DecodeRgb565(ushort value)
    {
        var r = (value >> 11) & 0x1f;
        var g = (value >> 5) & 0x3f;
        var b = value & 0x1f;
        return new Rgb(
            (byte)((r << 3) | (r >> 2)),
            (byte)((g << 2) | (g >> 4)),
            (byte)((b << 3) | (b >> 2)));
    }

    private static ushort ReadUInt16Le(byte[] data, int offset)
    {
        return (ushort)(data[offset] | (data[offset + 1] << 8));
    }

    private static uint ReadUInt32Le(byte[] data, int offset)
    {
        return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
    }

    private readonly record struct Rgb(byte R, byte G, byte B);
}
