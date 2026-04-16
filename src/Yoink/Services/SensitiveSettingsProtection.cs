using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Yoink.Models;

namespace Yoink.Services;

internal static class SensitiveSettingsProtection
{
    private const string ProtectedPrefix = "dpapi:v1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Yoink.Settings.Secrets.v1");

    public static AppSettings ProtectForStorage(AppSettings settings, JsonSerializerOptions options)
    {
        var copy = Clone(settings, options);
        Transform(copy, Protect);
        return copy;
    }

    public static AppSettings RedactForExport(AppSettings settings, JsonSerializerOptions options)
    {
        var copy = Clone(settings, options);
        Transform(copy, Redact);
        return copy;
    }

    public static void Unprotect(AppSettings settings)
        => Transform(settings, Unprotect);

    internal static string Redact(string value)
        => "";

    private static AppSettings Clone(AppSettings settings, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<AppSettings>(
               JsonSerializer.Serialize(settings, options),
               options)
           ?? new AppSettings();

    private static void Transform(AppSettings settings, Func<string, string> transform)
    {
        settings.GoogleTranslateApiKey = TransformNullable(settings.GoogleTranslateApiKey, transform);
        Transform(settings.ImageUploadSettings, transform);
        Transform(settings.StickerUploadSettings, transform);
        Transform(settings.UpscaleUploadSettings, transform);
    }

    private static void Transform(UploadSettings settings, Func<string, string> transform)
    {
        settings.ImgurClientId = transform(settings.ImgurClientId);
        settings.ImgurAccessToken = transform(settings.ImgurAccessToken);
        settings.ImgBBApiKey = transform(settings.ImgBBApiKey);
        settings.GyazoAccessToken = transform(settings.GyazoAccessToken);
        settings.DropboxAccessToken = transform(settings.DropboxAccessToken);
        settings.GoogleDriveAccessToken = transform(settings.GoogleDriveAccessToken);
        settings.OneDriveAccessToken = transform(settings.OneDriveAccessToken);
        settings.AzureBlobSasUrl = transform(settings.AzureBlobSasUrl);
        settings.GitHubToken = transform(settings.GitHubToken);
        settings.ImmichApiKey = transform(settings.ImmichApiKey);
        settings.FtpPassword = transform(settings.FtpPassword);
        settings.SftpPassword = transform(settings.SftpPassword);
        settings.WebDavPassword = transform(settings.WebDavPassword);
        settings.S3AccessKey = transform(settings.S3AccessKey);
        settings.S3SecretKey = transform(settings.S3SecretKey);
        settings.CustomHeaders = transform(settings.CustomHeaders);
    }

    private static void Transform(StickerSettings settings, Func<string, string> transform)
    {
        settings.RemoveBgApiKey = transform(settings.RemoveBgApiKey);
        settings.PhotoroomApiKey = transform(settings.PhotoroomApiKey);
    }

    private static void Transform(UpscaleSettings settings, Func<string, string> transform)
        => settings.DeepAiApiKey = transform(settings.DeepAiApiKey);

    private static string? TransformNullable(string? value, Func<string, string> transform)
        => value is null ? null : transform(value);

    private static string Protect(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            return value;

        try
        {
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value),
                Entropy,
                DataProtectionScope.CurrentUser);
            return ProtectedPrefix + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return value;
        }
    }

    private static string Unprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            return value;

        try
        {
            var payload = Convert.FromBase64String(value[ProtectedPrefix.Length..]);
            var bytes = ProtectedData.Unprotect(payload, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return value;
        }
    }
}
