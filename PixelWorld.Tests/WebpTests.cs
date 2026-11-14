using System.Buffers.Binary;
using System.Diagnostics;
using PixelWorld.Imaging;

namespace PixelWorld.Tests;

public class WebpTests
{
    // Files this encoder wrote, each decoded by libwebp (through Pillow) to confirm every pixel came back
    // the same. The encoder is deterministic, so any change to its output shows up here; when that is
    // deliberate, decode the new bytes with libwebp the same way before replacing these.
    private const string TwoColourWebp =
        "UklGRioAAABXRUJQVlA4TB0AAAAvDIABAA8w//M///MfeBAIEAb8nzj0ENH/GkDmBQA=";

    private const string TransparentWebp =
        "UklGRiQAAABXRUJQVlA4TBgAAAAvCAACEA8QEfMfQiCQjLC/bw4R/d+EsBE=";

    private const string SixteenWebp =
        "UklGRngAAABXRUJQVlA4TGwAAAAvH8AFAH9w+P+BmIP5jzQzMzMzMzMzoKYAJymkkEIKKaSQQgoppJBCCilMYQr7aLf3ziCi/0bDNomnYnyRUs6l1Npa72PMyfN3a+19zr3v8fydiKrZL/D8XRTl4R4Y5eEeGOXhHhjlRR/yUR4=";

    private const string HundredWebp =
        "UklGRhYDAABXRUJQVlA4TAkDAAAvJ8AEAB/DEBSlMCF8hBCGEMIQQgghhIcQwhBCOKNpMAQEKVBIhxDCI4QwCCGEEMIjhBBCCIvgRBmooRnsFOu7wAcLi55DBJPCC8a/mcXoFV43eRHBaDfZzfMff3sFAAAAAAAAqCoAAAAAAACgqgAAAAAAuL0BADDOD0BVUdUBgCS0VgGAJADA3A9IPpIAQAKougB839taLSDNPUkCCUitLfo7vP9gnK8kzN3b84/U3+b20Z6zM063r86oPfe21i4JkKgBGFRIisiRKznHXUlKQodjSDTkvpeZMcfO+z9F/7f7zRtE9B9tIgfpHhFzruFCULcH8/pwfyAYIsKRaCxOJqhkKp3J5p7yhWKpXKnSDFvj6nxDACU2pRYHrHK70xUVR65qek/uD4bPSpxMjMagXAhqqBNzyrAz6+UpP1/YDmkJ4Ai6WidTm2CoKSFbYzep7g/pzJEItzi3qplT5jTL5s6RaJ3HaOYEQCio1OYSarqu2z/lFZVcBrrPyu1etF8TI7xfs3LnCOdWQQVFACIFn/xfUPf57b0Q4zF9ekhfQk1Q3t6DnR0JCdmW/nGfkJCPEin4HmzmSLiuoJhTBkjjDX3KpC+icissPqNutUKN2wPrK4xsS6TQe5wuIeVWiPGauV8Ght9PEc7YrYp2Q6dTmy5glBM+mc2A8kyqSf/AyoWRD5v0Tg+71QiQ3ud1tULh/dmxCWo9bteyhOu+4DUA1elUYAi0mLlfdqwv6VoEJFAACsx4fxZS8lF3OdFj0t3vc2trN8z9cmCBqlA4mxHf8pyx6b6ckWJcq44BlDNehWkq8Dcn3ReeCSWzF/EngpZIPUvc5s7pAZhiamX0cJ5Firy2xkE5H0u2nQGsOu5nxN8KrgtPZcRshjmpUN8leqmORdyixquX9teOSh4tNcwkWHGXBXpZOyqfqI1VcTb4fb4u1JXvEAArpL5vz8SfyEe8YY77F4ByTrMOHVvjp+CLdK/vAP0tso1rFCWnh1+3uUGClfA9thNofy300yryFd+hYzXzqI2t5TQA";

    private const string TruecolourWebp =
        "UklGRuIEAABXRUJQVlA4TNUEAAAvF8ADEIVBACAcOHDgwIEDBw4cOHDgwIEDB65gAAAAtEmSJEmSDkmHDh06NOjQoEGDBg0aNNigwQYbbLDBBhssWLBgwYIFCxYsWPBgwYMHD54ET5IkSSAAAECA8P///////////////////weFBDD5/12SJEmSJEmSJEmSJMlkMplMJpMFAAAOEIQBgqCAYQgQBAwUBQHDgMBxAAgCHySJB4rCBVXjwNRgw9JiwdZhwsFjwCmgwyWiwS2hwiOjwLMgw09Fgr+GCP8dActPx/rREIRUhAEFUUxGHJGQpESkCQFZzo8840NR8qIseHCguXGsuHBuOXFtOHDv2fHo2PAcWfEaWPCemfErMeF3ZcTfwkDZk1PxZVQdKQFXQhASE4JFhAEhEVBAFNMnhusRR3RJoDokKW1StBapQpOmUoOWTp22Xo0ORpVOVoUuSZluWYkeQZGeUYHja+f02DjfVi6XhWtoZhCYGP5GRp+BcerPJPFjGvsyi3yYl94sCi+WuScPmAePrTvPjRuvtSvvlQsfozOfgxNfvSPfnYMEJy7RjUnyopL9iBQjLNUMCbCCAu2AICRfMJonBMsViucIA7KFg1kioEyRcIYoJl1UmyZTlypLnyJbkSwHmSQnlSgXnaDurqu/ahreqsanomkva95KWs6i1qOgIO5XGPUpSnsVJz1K/m6lX5eysFN50KGibldZtenAturYtOicN+uaNeleNupRNJi15ubsmXljasGcWNTGlvSRZWVoRR1YhfYNwHsGgV1D4I5hb9uIv2XU2TTmbhin1k3QayaJVVPkiqls2aZ8yZZk0bZ0wfV5d3Pc3O5Xd9vF/Xr2sJw8zkdP08Fz+O8l+PP6/zr4fhy+346eL8f3p5Prw2n57qx4c56/usheXKbPPiBPPsaPPkcPsZnw0BYsjA0N60DCueDwHiiCD4wIACKJ+JElvCgyblQFJ4CKHVDDCqRjBjYwgpDoQSm0YDRqcAYlBIsckkMKxSOGGhByNui56rTcLWrqk5LmIae9Sek+YvqXkGHgz9jzZZp4M488WRburDNXgo0z4cqRqGBPnLMlqViTlizJGubkNVOKjjFly1DDLq/JkNWiSmvTJKVtcRlHVNYUlrME5Xn9CoJeRVa3EqdTWdauomhVFTULSBoFcfVChFphVLUIplKUVi7GKBUnFUtQCt1t9u5XW4+KtWfZ0qtl7t00tT6MbXZD286/XevXvvHtUPt0nLw7jV6dB88uvUfXzL1B6tYwcW0UuzSunJuUTk0Lx2a5w7RqfDotNr0eneGIzCSGZ5FCs8nB0UpgDJs/lssbx+eOF3ImkNkTqaxJdOZkJmMKmj4VSxuApw4kUgaByYOhpCFw4lAkYRulvs1c23arbrdWth/KO/KlnaXirmJhd6Z/dd23putd2/asK7rX510bqs6NZcempH1z3LYla92atiwImhf+TYuixsVhw73l+X2U2X3G6bXS5Dp+fL0wuqEd3sQNbqH3b2P2TpO7Z6ids/j2OWLrPLp5Adu4CK9fQtYug6tXoJWr/vIBYOmgu3jIW/iXdP/XePv3cP2Gv3zTnb9tT981x+/rw4/U/8/k36/E7+/4z+vs+0369Tb5fBd/vI/eP4RvH4PXT//L5+/5y/v09Xl8cD8AAA==";

    private const string RepeatsWebp =
        "UklGRrwBAABXRUJQVlA4TK8BAAAvK8AEAEdAEECiMFpmK40SwjgEASQKI4Qy0mihjUAkQBJtLPCE/7cwxDb/0e0TqPwEAtRaKEAaAOBGUyw6SibH5DHHPIqjGnL0iK9jU74ci2U7MsT/3p+I6H8Sq9XnLL1DWgv1lKHqkOVwRjdHywp27PB0sp8yVC5jabdfWMvpZP79ILj2cBMNXXlkwxQbh6S+8Xg5yIvwpqqs8AtBvqpTzCHOGIRyP2JZkYt2N9WJDNBPjgiEbblIOm+6Tm3EeOSjWUwD7wppdZEYLEIR2c61Pe6VhBQfpFDuuQ93UO/EwRudeJq8XfMSt/4nv0WgTAs8GkCyjecWiB2rjf8umFYcNtK3JY0O6KWDS8O0LO+qCSKBaaT/5dYCbBsj2vuVDmJn3gqTU79Ak0YqLpb9aph9XBRqXar4S1l9Il2MQNfPm6+Nrs9BpfQJhQNofQTYWFub66BQK9ScitqpRX/EhowgVLdDqOn6+/HNRny3lJmA/vMl13ye8Y6ljmi1dlEh8u9jHxddAwCSkeheSPjZU3YwzK56nWddYTuuW6ansow7+xLX7tIZtcfVHgRcALvYBAA=";

    private static Color Shade(int i) => new((byte)(i * 16), (byte)(255 - i * 16), (byte)(i * 7));

    private static Bitmap Make(int width, int height, Func<int, int, Color> pixel)
    {
        var image = new Bitmap(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            image[x, y] = pixel(x, y);
        return image;
    }

    private static Bitmap TwoColourImage() => Make(13, 7, (x, y) => x * y % 3 == 0 ? Color.Black : Color.White);

    private static Bitmap TransparentImage() => Make(9, 9, (x, y) => x == 4 && y == 4 ? Color.Black : Color.Transparent);

    private static Bitmap SixteenImage() => Make(32, 24, (x, y) => Shade((x / 4 + y / 3 * 5) % 16));

    private static Bitmap HundredImage() => Make(40, 20, (x, y) => Shade((x * 7 + y * 13 + x * y / 5) % 100));

    private static Bitmap TruecolourImage() =>
        Make(24, 16, (x, y) => new Color((byte)(x * 10), (byte)(y * 15), (byte)((x ^ y) * 8), (byte)(255 - x * 5)));

    /// <summary>Random pixels with short runs copied from just above and to the side, which become back references.</summary>
    private static Bitmap RepeatsImage()
    {
        var seed = 7u;
        var image = Make(44, 20, (_, _) =>
        {
            seed = seed * 1664525u + 1013904223u;
            return Shade((int)(seed >> 24) % 9);
        });

        for (var k = 0; k < 12; k++)
        {
            var width = 4 + k % 5;
            var dx = k % 7 - 3;
            var dy = k % 4;
            if (dx == 0 && dy == 0) dy = 1;
            for (var x = 0; x < width; x++)
                image[5 + k * 2 + x, 4 + k] = image[5 + k * 2 + x - dx, 4 + k - dy];
        }

        return image;
    }

    private static byte[] Encode(Bitmap image)
    {
        var stream = new MemoryStream();
        Webp.Write(image, stream);
        return stream.ToArray();
    }

    private static void AssertGolden(Bitmap image, string expected) => Assert.Equal(expected, Convert.ToBase64String(Encode(image)));

    [Fact]
    public void Write_TwoColours_MatchesVerifiedOutput() => AssertGolden(TwoColourImage(), TwoColourWebp);

    [Fact]
    public void Write_TransparentBackground_MatchesVerifiedOutput() => AssertGolden(TransparentImage(), TransparentWebp);

    [Fact]
    public void Write_SixteenColours_MatchesVerifiedOutput() => AssertGolden(SixteenImage(), SixteenWebp);

    [Fact]
    public void Write_HundredColours_MatchesVerifiedOutput() => AssertGolden(HundredImage(), HundredWebp);

    [Fact]
    public void Write_MoreThan256ColoursWithAlpha_MatchesVerifiedOutput()
    {
        var image = TruecolourImage();
        var distinct = Enumerable.Range(0, image.Height).SelectMany(y => Enumerable.Range(0, image.Width).Select(x => image[x, y])).Distinct().Count();
        Assert.True(distinct > 256, "this image should not fit a palette");

        AssertGolden(image, TruecolourWebp);
    }

    [Fact]
    public void Write_RepeatedRuns_MatchesVerifiedOutput()
    {
        // Back references are what keep this smaller than one byte a pixel.
        Assert.True(Convert.FromBase64String(RepeatsWebp).Length < 44 * 20);

        AssertGolden(RepeatsImage(), RepeatsWebp);
    }

    [Fact]
    public void Write_IsDeterministic()
    {
        Assert.Equal(Encode(RepeatsImage()), Encode(RepeatsImage()));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(13, 7)]
    [InlineData(256, 192)]
    [InlineData(16384, 1)]
    public void Write_HasAValidContainerAndHeader(int width, int height)
    {
        var image = new Bitmap(width, height);
        image[width - 1, height - 1] = Color.Red;
        var webp = Encode(image);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(webp, 0, 4));
        Assert.Equal("WEBPVP8L", System.Text.Encoding.ASCII.GetString(webp, 8, 8));
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(4));
        var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(16));
        Assert.Equal(webp.Length - 8, (int)riffSize);
        Assert.Equal(0, webp.Length % 2); // chunks are padded to an even length
        Assert.Equal(12 + 8 + chunkSize + chunkSize % 2, (uint)webp.Length);

        // The VP8L header is a signature byte, then 14 bits each of width and height minus one.
        Assert.Equal(0x2F, webp[20]);
        var header = BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(21));
        Assert.Equal(width - 1, (int)(header & 0x3FFF));
        Assert.Equal(height - 1, (int)((header >> 14) & 0x3FFF));
        Assert.Equal(0u, header >> 29); // version
    }

    [Fact]
    public void Write_SetsTheAlphaHintOnlyWhenThereIsTransparency()
    {
        static uint Header(byte[] webp) => BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(21));

        Assert.Equal(0u, (Header(Encode(TwoColourImage())) >> 28) & 1);
        Assert.Equal(1u, (Header(Encode(TransparentImage())) >> 28) & 1);
    }

    [Theory]
    [InlineData(16385, 1)]
    [InlineData(1, 16385)]
    public void Write_TooLarge_Throws(int width, int height)
    {
        Assert.Throws<ArgumentException>(() => Encode(new Bitmap(width, height)));
    }

    [Fact]
    public void Write_DecodesIdenticallyWithDwebpWhenInstalled()
    {
        // Only runs where libwebp's dwebp is on the PATH; the golden files above are what normally guard the output.
        var folder = Path.Combine(Path.GetTempPath(), "pw-webp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var images = new[] { TwoColourImage(), TransparentImage(), SixteenImage(), HundredImage(), TruecolourImage(), RepeatsImage() };
            for (var i = 0; i < images.Length; i++)
            {
                var webp = Path.Combine(folder, i + ".webp");
                var png = Path.Combine(folder, i + ".png");
                File.WriteAllBytes(webp, Encode(images[i]));

                if (!TryRunDwebp(webp, png)) return;

                var decoded = Png.Read(new MemoryStream(File.ReadAllBytes(png)));
                Assert.Equal((images[i].Width, images[i].Height), (decoded.Width, decoded.Height));
                for (var y = 0; y < decoded.Height; y++)
                for (var x = 0; x < decoded.Width; x++)
                    Assert.True(images[i][x, y] == decoded[x, y], $"image {i} ({x},{y})");
            }
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static bool TryRunDwebp(string webp, string png)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("dwebp", ["-png", webp, "-o", png])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            })!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // dwebp is not installed
        }
    }
}
