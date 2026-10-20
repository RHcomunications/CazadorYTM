namespace CazadorYTM.Core.Services;

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public static class DownloadIntegrity
{
    private static readonly Regex Sha256Regex = new(@"\b[a-fA-F0-9]{64}\b", RegexOptions.Compiled);

    public static bool TryNormalizeSha256(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..].Trim();
        }

        var match = Sha256Regex.Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        normalized = match.Value.ToLowerInvariant();
        return true;
    }

    public static string? ParseChecksumFile(string? content)
    {
        return TryNormalizeSha256(content, out var normalized) ? normalized : null;
    }

    public static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task VerifyFileAsync(
        string filePath,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeSha256(expectedSha256, out var expected))
        {
            throw new InvalidDataException("El checksum SHA-256 esperado no es válido.");
        }

        var actual = await ComputeFileSha256Async(filePath, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(expected)))
        {
            throw new InvalidDataException(
                $"La verificación SHA-256 falló. Esperado: {expected}. Recibido: {actual}.");
        }
    }

    public static async Task<string?> FetchSha256Async(
        HttpClient httpClient,
        string checksumUrl,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            checksumUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("La descarga del checksum no usa HTTPS.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

        var buffer = new char[8192];
        var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        return ParseChecksumFile(new string(buffer, 0, read));
    }
}
