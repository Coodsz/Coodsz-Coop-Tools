// Coodsz Image Exporter: a small PNG writer and the red/blue fix for the engine's PNG files.
// Ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// src/CoopKeep.ImageExporter/ImageExporterSubModule.cs, translated from Turkish.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CoodszImageExporter;

/// <summary>A small PNG writer without dependencies (RGBA, 8 bit).</summary>
internal static class Png
{
    public static byte[] Encode(byte[] pixels, int width, int height, bool swapRedBlue)
    {
        var stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0;
            for (var x = 0; x < width; x++)
            {
                var s = y * stride + x * 4;
                var d = y * (stride + 1) + 1 + x * 4;
                raw[d] = swapRedBlue ? pixels[s + 2] : pixels[s];
                raw[d + 1] = pixels[s + 1];
                raw[d + 2] = swapRedBlue ? pixels[s] : pixels[s + 2];
                raw[d + 3] = pixels[s + 3];
            }
        }

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

        var ihdr = new byte[13];
        BigEndian(ihdr, 0, (uint)width);
        BigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; // 8 bit, RGBA
        Chunk(ms, "IHDR", ihdr);
        Chunk(ms, "IDAT", Zlib(raw));
        Chunk(ms, "IEND", new byte[0]);
        return ms.ToArray();
    }

    private static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0x9C);
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, true))
            deflate.Write(data, 0, data.Length);

        uint a = 1, b = 0;
        foreach (var x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
        var adler = new byte[4];
        BigEndian(adler, 0, (b << 16) | a);
        ms.Write(adler, 0, 4);
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BigEndian(len, 0, (uint)data.Length);
        s.Write(len, 0, 4);
        var t = Encoding.ASCII.GetBytes(type);
        s.Write(t, 0, 4);
        s.Write(data, 0, data.Length);
        var crc = new byte[4];
        BigEndian(crc, 0, Crc(t, data));
        s.Write(crc, 0, 4);
    }

    private static readonly uint[] Table = MakeTable();

    private static uint[] MakeTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in type) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in data) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void BigEndian(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }
}

/// <summary>Reads the engine's 8 bit RGBA PNG and swaps its red and blue channels.</summary>
internal static class PngFix
{
    public static bool SwapRedBlue(string path)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length < 33 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G') return false;

        int w = 0, h = 0;
        var idat = new MemoryStream();
        var i = 8;
        while (i + 8 <= b.Length)
        {
            var len = (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
            var type = Encoding.ASCII.GetString(b, i + 4, 4);
            if (type == "IHDR")
            {
                w = (b[i + 8] << 24) | (b[i + 9] << 16) | (b[i + 10] << 8) | b[i + 11];
                h = (b[i + 12] << 24) | (b[i + 13] << 16) | (b[i + 14] << 8) | b[i + 15];
                if (b[i + 16] != 8 || b[i + 17] != 6) return false; // 8 bit RGBA only
            }
            else if (type == "IDAT") idat.Write(b, i + 8, len);
            i += 12 + len;
        }

        if (w <= 0 || h <= 0) return false;

        // Skip the zlib header and inflate.
        var z = idat.ToArray();
        byte[] raw;
        using (var inflate = new DeflateStream(new MemoryStream(z, 2, z.Length - 2), CompressionMode.Decompress))
        using (var outMs = new MemoryStream())
        {
            inflate.CopyTo(outMs);
            raw = outMs.ToArray();
        }

        var stride = w * 4;
        if (raw.Length < (stride + 1) * h) return false;

        var pixels = new byte[stride * h];
        var prev = new byte[stride];
        for (var y = 0; y < h; y++)
        {
            var filter = raw[y * (stride + 1)];
            var row = new byte[stride];
            Buffer.BlockCopy(raw, y * (stride + 1) + 1, row, 0, stride);
            for (var x = 0; x < stride; x++)
            {
                int a = x >= 4 ? row[x - 4] : 0, up = prev[x], c = x >= 4 ? prev[x - 4] : 0;
                row[x] = filter switch
                {
                    1 => (byte)(row[x] + a),
                    2 => (byte)(row[x] + up),
                    3 => (byte)(row[x] + (a + up) / 2),
                    4 => (byte)(row[x] + Paeth(a, up, c)),
                    _ => row[x],
                };
            }

            Buffer.BlockCopy(row, 0, pixels, y * stride, stride);
            prev = row;
        }

        File.WriteAllBytes(path, Png.Encode(pixels, w, h, swapRedBlue: true));
        return true;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
