namespace ClubPay.Agent.Core.Contracts;

/// <summary>Base64url (RFC 4648 §5) decoding shared by the compact-token verifiers
/// (VoucherService, ManagerCodeService, LockCodeService). Throws FormatException on bad input.</summary>
internal static class Base64Url
{
    public static byte[] Decode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
