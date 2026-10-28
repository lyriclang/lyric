using System.Numerics;
using System.Security.Cryptography;

namespace Lyric5.Toolchain;

/// <summary>
/// Ed25519 signature verification (RFC 8032 §5.1.7), as libsodium's
/// <c>crypto_sign_verify_detached</c> decides it, which minisign calls: an encoding of a point
/// or of S that is not canonical is refused, a key or an R of small order is refused, and the
/// equation is checked without the cofactor. Only verification: it handles public data, so
/// the arithmetic is plain <see cref="BigInteger"/> and need not run in constant time.
/// </summary>
public static class Ed25519
{
    public const int KeySize = 32;
    public const int SignatureSize = 64;

    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private static readonly BigInteger L = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");
    private static readonly BigInteger D = Mod(-121665 * Inverse(121666));
    private static readonly BigInteger SqrtMinusOne = BigInteger.ModPow(2, (P - 1) / 4, P);

    private static readonly Point Identity = new(0, 1, 1, 0);
    private static readonly Point Base = Decode(Convert.FromHexString("5866666666666666666666666666666666666666666666666666666666666666"))!.Value;

    /// <summary>Whether <paramref name="signature"/> is <paramref name="publicKey"/>'s over
    /// <paramref name="message"/>.</summary>
    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (publicKey.Length != KeySize || signature.Length != SignatureSize) return false;
        var s = Little(signature[32..]);
        if (s >= L) return false;
        if (Decode(publicKey) is not { } a || SmallOrder(a)) return false;
        if (Decode(signature[..32]) is not { } r || SmallOrder(r)) return false;

        var input = new byte[64 + message.Length];
        signature[..32].CopyTo(input);
        publicKey.CopyTo(input.AsSpan(32));
        message.CopyTo(input.AsSpan(64));
        var h = Little(SHA512.HashData(input)) % L;

        return Same(Multiply(Base, s), Add(r, Multiply(a, h)));
    }

    // points in extended coordinates: x = X/Z, y = Y/Z, x·y = T/Z
    private readonly record struct Point(BigInteger X, BigInteger Y, BigInteger Z, BigInteger T);

    private static BigInteger Mod(BigInteger value)
    {
        var rest = value % P;
        return rest.Sign < 0 ? rest + P : rest;
    }

    private static BigInteger Inverse(BigInteger value) => BigInteger.ModPow(value, P - 2, P);

    private static BigInteger Little(ReadOnlySpan<byte> bytes) => new(bytes, isUnsigned: true, isBigEndian: false);

    private static Point Add(Point p, Point q)
    {
        var a = Mod((p.Y - p.X) * (q.Y - q.X));
        var b = Mod((p.Y + p.X) * (q.Y + q.X));
        var c = Mod(p.T * 2 * D * q.T);
        var d = Mod(p.Z * 2 * q.Z);
        var (e, f, g, h) = (b - a, d - c, d + c, b + a);
        return new Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    private static Point Multiply(Point p, BigInteger scalar)
    {
        var result = Identity;
        for (var bit = (int)scalar.GetBitLength() - 1; bit >= 0; bit--)
        {
            result = Add(result, result);
            if (!(scalar >> bit).IsEven) result = Add(result, p);
        }
        return result;
    }

    private static bool Same(Point p, Point q) =>
        Mod(p.X * q.Z - q.X * p.Z) == 0 && Mod(p.Y * q.Z - q.Y * p.Z) == 0;

    // a point whose eighth multiple is the identity: it says nothing about the key it stands for
    private static bool SmallOrder(Point p) => Same(Multiply(p, 8), Identity);

    /// <summary>The point 32 bytes encode (RFC 8032 §5.1.3); <c>null</c> for y ≥ p, for an x
    /// that does not exist, and for −0.</summary>
    private static Point? Decode(ReadOnlySpan<byte> bytes)
    {
        Span<byte> copy = stackalloc byte[32];
        bytes.CopyTo(copy);
        var sign = copy[31] >> 7;
        copy[31] &= 0x7f;
        var y = Little(copy);
        if (y >= P) return null;

        var x2 = Mod((y * y - 1) * Inverse(Mod(D * y * y + 1)));
        if (x2.IsZero)
        {
            if (sign == 1) return null;
            return new Point(0, y, 1, 0);
        }
        var x = BigInteger.ModPow(x2, (P + 3) / 8, P);
        if (Mod(x * x - x2) != 0) x = Mod(x * SqrtMinusOne);
        if (Mod(x * x - x2) != 0) return null;
        if ((int)(x & 1) != sign) x = P - x;
        return new Point(x, y, 1, Mod(x * y));
    }
}
