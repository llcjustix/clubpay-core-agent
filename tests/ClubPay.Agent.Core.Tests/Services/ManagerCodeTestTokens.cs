using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Tests.Services;

/// <summary>
/// Test-only Ed25519 signer for manager master-code tokens — like <see cref="VoucherTestTokens"/>,
/// deliberately reimplements the base64url/compact-token mechanics independently of the production
/// verifier so a shared encoding bug can't hide a defect. Only ControllerJsonOptions.Default is shared
/// (it is the wire contract for the payload JSON shape).
/// </summary>
internal static class ManagerCodeTestTokens
{
    public static (Key SigningKey, string PublicKeyBase64) GenerateKeyPair()
    {
        var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var publicKeyBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        return (key, Convert.ToBase64String(publicKeyBytes));
    }

    public static string EncodePayload(ManagerCodePayload payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, ControllerJsonOptions.Default);
        return Base64UrlEncode(json);
    }

    public static string Sign(Key signingKey, string payloadB64)
    {
        var signature = SignatureAlgorithm.Ed25519.Sign(signingKey, Encoding.ASCII.GetBytes(payloadB64));
        return Base64UrlEncode(signature);
    }

    public static string BuildToken(Key signingKey, ManagerCodePayload payload)
    {
        var payloadB64 = EncodePayload(payload);
        return payloadB64 + "." + Sign(signingKey, payloadB64);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
