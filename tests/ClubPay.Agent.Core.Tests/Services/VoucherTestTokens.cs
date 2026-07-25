using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Tests.Services;

/// <summary>
/// Test-only Ed25519 signer for voucher tokens — deliberately reimplements the base64url/compact-token
/// mechanics independently of VoucherService (which only ever verifies, never signs) so a bug shared
/// between production and test encoding can't hide a real defect. The only thing intentionally shared
/// with production is ControllerJsonOptions.Default, since that's the actual wire contract for the
/// payload JSON shape, not an implementation detail of the crypto/encoding.
/// </summary>
internal static class VoucherTestTokens
{
    public static (Key SigningKey, string PublicKeyBase64) GenerateKeyPair()
    {
        var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var publicKeyBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        return (key, Convert.ToBase64String(publicKeyBytes));
    }

    public static string EncodePayload(VoucherPayload payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, ControllerJsonOptions.Default);
        return Base64UrlEncode(json);
    }

    public static string Sign(Key signingKey, string payloadB64)
    {
        var signature = SignatureAlgorithm.Ed25519.Sign(signingKey, Encoding.ASCII.GetBytes(payloadB64));
        return Base64UrlEncode(signature);
    }

    public static string BuildToken(Key signingKey, VoucherPayload payload)
    {
        var payloadB64 = EncodePayload(payload);
        return payloadB64 + "." + Sign(signingKey, payloadB64);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
