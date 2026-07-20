using System.Security.Cryptography;

namespace Roadhog.Core.Licensing;

public sealed record LicenseCredential
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public string Cdkey { get; init; } = string.Empty;

    public string ClientInstanceId { get; init; } = string.Empty;

    public string InstallSecret { get; init; } = string.Empty;

    public bool Activated { get; init; }

    public static LicenseCredential Create(string cdkey)
    {
        return new LicenseCredential
        {
            Cdkey = NormalizeCdkey(cdkey),
            ClientInstanceId = Guid.NewGuid().ToString("D"),
            InstallSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            Activated = false
        };
    }

    public LicenseCredential MarkActivated()
    {
        return this with { Activated = true };
    }

    public bool Validate(out string error)
    {
        if (Version != CurrentVersion)
        {
            error = "Unsupported local license credential version.";
            return false;
        }

        if (!IsValidCdkey(Cdkey))
        {
            error = "CDKEY format is invalid.";
            return false;
        }

        if (!Guid.TryParseExact(ClientInstanceId, "D", out _))
        {
            error = "Client instance id is invalid.";
            return false;
        }

        if (InstallSecret.Length < 32 || InstallSecret.Length > 512)
        {
            error = "Install secret is invalid.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string NormalizeCdkey(string? value)
    {
        return string.Concat((value ?? string.Empty).Where(character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant();
    }

    public static bool IsValidCdkey(string? value)
    {
        var normalized = NormalizeCdkey(value);
        return normalized.Length is >= 16 and <= 128
            && normalized.All(character =>
                (character is >= 'A' and <= 'Z')
                || (character is >= '0' and <= '9')
                || character == '-');
    }
}
