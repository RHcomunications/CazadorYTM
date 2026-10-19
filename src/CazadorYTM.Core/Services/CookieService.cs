namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CazadorYTM.Core;

public class CookieEntry
{
    public string Domain { get; set; } = string.Empty;
    public bool IncludeSubdomains { get; set; } = true;
    public string Path { get; set; } = "/";
    public bool Secure { get; set; } = true;
    public long Expiration { get; set; } = 0;
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class CookiesStatusInfo
{
    public bool Exists { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string FormattedSize => FileSizeBytes > 1024 ? $"{FileSizeBytes / 1024.0:F1} KB" : $"{FileSizeBytes} bytes";
    public DateTime? LastModified { get; set; }
    public int EstimatedCookieCount { get; set; }
    public bool HasYouTubeAuthCookies { get; set; }
    public List<string> DetectedAuthKeys { get; set; } = new();
}

public static class CookieService
{
    private static readonly string[] AuthCookieKeys = new[]
    {
        "SID", "HSID", "SSID", "LOGIN_INFO", "SAPISID", "APISID", "__Secure-3PSID", "__Secure-1PSID"
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static bool RepairBomIfNeeded(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                File.WriteAllBytes(path, bytes[3..]);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public static CookiesStatusInfo GetCookiesStatus(string? path = null)
    {
        var targetPath = string.IsNullOrEmpty(path) ? Helpers.GetDefaultCookiesPath() : path;
        var info = new CookiesStatusInfo
        {
            FilePath = targetPath
        };

        if (!File.Exists(targetPath))
        {
            info.Exists = false;
            return info;
        }

        try
        {
            var fileInfo = new FileInfo(targetPath);
            info.Exists = true;
            info.FileSizeBytes = fileInfo.Length;
            info.LastModified = fileInfo.LastWriteTime;

            var content = File.ReadAllText(targetPath);
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int count = 0;
            var detected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#") && !trimmed.StartsWith("#HttpOnly_")) continue;

                var parts = trimmed.Split('\t');
                if (parts.Length >= 7)
                {
                    count++;
                    var cookieName = parts[5];
                    if (AuthCookieKeys.Contains(cookieName, StringComparer.OrdinalIgnoreCase))
                    {
                        detected.Add(cookieName);
                    }
                }
            }

            info.EstimatedCookieCount = count;
            info.DetectedAuthKeys = detected.ToList();
            info.HasYouTubeAuthCookies = detected.Count > 0;
        }
        catch
        {
            info.Exists = false;
        }

        return info;
    }

    public static async Task<(bool Success, string Message)> ExtractCookiesFromBrowserAsync(
        string browser,
        string targetPath,
        bool asJson = false,
        Action<string>? logger = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                var exe = BinaryManager.GetYtDlpPath();
                if (!File.Exists(exe))
                {
                    return (false, "No se encontró el ejecutable yt-dlp en el sistema.");
                }

                if (string.IsNullOrWhiteSpace(browser) || browser.Equals("ninguno", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Debes seleccionar un navegador válido.");
                }

                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var tempCookiesTxt = Path.Combine(Helpers.GetAppDataDir(), "temp_extracted_cookies.txt");
                if (File.Exists(tempCookiesTxt))
                {
                    try { File.Delete(tempCookiesTxt); } catch { }
                }

                logger?.Invoke($"[COOKIES] Extrayendo cookies desde {browser}...");

                var args = new List<string>
                {
                    "--cookies-from-browser",
                    browser.ToLower(),
                    "--cookies",
                    tempCookiesTxt,
                    "--skip-download",
                    "--no-warnings",
                    "https://www.youtube.com/robots.txt"
                };

                var runResult = ProcessRunner.RunAsync(exe, args, timeoutMs: 45000).GetAwaiter().GetResult();
                var combinedErr = $"{runResult.StandardOutput}\n{runResult.StandardError}";

                if (combinedErr.Contains("DPAPI", StringComparison.OrdinalIgnoreCase) ||
                    combinedErr.Contains("Failed to decrypt", StringComparison.OrdinalIgnoreCase))
                {
                    logger?.Invoke("[COOKIES] El navegador tiene protección de cifrado DPAPI activa.");
                    return (false, $"El navegador '{browser}' bloquea el acceso directo por protección de cifrado DPAPI de Windows.\n\n" +
                                   "Te recomendamos usar la Vía 2 (Asistente con la extensión 'Get cookies.txt LOCALLY' en youtube.com/robots.txt) " +
                                   "para obtener tus cookies en un clic sin este bloqueo.");
                }

                if (!File.Exists(tempCookiesTxt) || new FileInfo(tempCookiesTxt).Length == 0)
                {
                    return (false, $"No se pudieron extraer cookies desde {browser}. Asegúrate de cerrar el navegador o de que hayas iniciado sesión en YouTube.");
                }

                var rawNetscape = File.ReadAllText(tempCookiesTxt, Encoding.UTF8);

                if (asJson)
                {
                    var jsonContent = NetscapeToJson(rawNetscape);
                    var jsonPath = Path.ChangeExtension(targetPath, ".json");
                    File.WriteAllText(jsonPath, jsonContent, Utf8NoBom);
                }

                File.WriteAllText(targetPath, rawNetscape, Utf8NoBom);

                try { File.Delete(tempCookiesTxt); } catch { }

                var status = GetCookiesStatus(targetPath);
                var authNote = status.HasYouTubeAuthCookies
                    ? " (Sesión de YouTube verificada correctamente)"
                    : " (Advertencia: No se detectaron tokens de sesión; asegúrate de haber iniciado sesión en YouTube en dicho navegador)";

                logger?.Invoke($"[COOKIES] Cookies extraídas con éxito: {status.EstimatedCookieCount} cookies guardadas.");
                return (true, $"¡Cookies extraídas y guardadas exitosamente desde {browser}! {status.EstimatedCookieCount} cookies registradas{authNote}.");
            }
            catch (Exception ex)
            {
                logger?.Invoke($"[COOKIES] Error al extraer: {ex.Message}");
                return (false, $"Error al extraer cookies: {ex.Message}");
            }
        });
    }

    public static async Task<(bool Success, string Message, int Count)> SaveCookiesFromTextAsync(
        string rawText,
        string targetPath,
        bool asJson = false)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    return (false, "El texto de cookies está vacío.", 0);
                }

                var trimmed = rawText.Trim();
                string netscapeContent;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    // Formato JSON (EditThisCookie o Cookie-Editor)
                    netscapeContent = JsonToNetscape(trimmed);
                }
                else
                {
                    // Formato Netscape
                    netscapeContent = trimmed;
                }

                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(targetPath, netscapeContent, Utf8NoBom);

                if (asJson)
                {
                    var jsonPath = Path.ChangeExtension(targetPath, ".json");
                    var jsonContent = NetscapeToJson(netscapeContent);
                    File.WriteAllText(jsonPath, jsonContent, Utf8NoBom);
                }

                var status = GetCookiesStatus(targetPath);
                if (status.EstimatedCookieCount == 0 && !status.HasYouTubeAuthCookies)
                {
                    return (false, "El contenido guardado no contiene entradas de cookies válidas en formato Netscape o JSON.", 0);
                }

                var authNote = status.HasYouTubeAuthCookies
                    ? " (Tokens de sesión de YouTube detectados: " + string.Join(", ", status.DetectedAuthKeys) + ")"
                    : " (Aviso: No se encontraron tokens de autenticación explícitos como SID o LOGIN_INFO)";

                return (true, $"Cookies guardadas correctamente. Se identificaron {status.EstimatedCookieCount} cookies{authNote}.", status.EstimatedCookieCount);
            }
            catch (Exception ex)
            {
                return (false, $"Error al procesar y guardar cookies: {ex.Message}", 0);
            }
        });
    }

    public static async Task<(bool Success, string Message)> ExportCookiesAsync(
        string sourcePath,
        string destinationPath,
        bool asJson = false)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    return (false, "No existe el archivo de cookies de origen para exportar.");
                }

                var content = File.ReadAllText(sourcePath, Encoding.UTF8);
                var destDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                if (asJson || destinationPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var jsonContent = NetscapeToJson(content);
                    File.WriteAllText(destinationPath, jsonContent, Utf8NoBom);
                }
                else
                {
                    File.WriteAllText(destinationPath, content, Utf8NoBom);
                }

                return (true, $"Archivo de cookies exportado con éxito a:\n{destinationPath}");
            }
            catch (Exception ex)
            {
                return (false, $"Error al exportar cookies: {ex.Message}");
            }
        });
    }

    public static async Task<(bool Success, string Message)> ImportCookiesAsync(
        string sourcePath,
        string destinationPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    return (false, "El archivo seleccionado no existe.");
                }

                var content = File.ReadAllText(sourcePath, Encoding.UTF8);
                var trimmed = content.Trim();
                string netscapeContent;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    netscapeContent = JsonToNetscape(trimmed);
                }
                else
                {
                    netscapeContent = trimmed;
                }

                var destDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.WriteAllText(destinationPath, netscapeContent, Utf8NoBom);

                var status = GetCookiesStatus(destinationPath);
                return (true, $"Cookies importadas correctamente: {status.EstimatedCookieCount} entradas registradas.");
            }
            catch (Exception ex)
            {
                return (false, $"Error al importar cookies: {ex.Message}");
            }
        });
    }

    public static string NetscapeToJson(string netscapeText)
    {
        var entries = new List<CookieEntry>();
        var lines = netscapeText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#") && !trimmed.StartsWith("#HttpOnly_")) continue;

            string lineToParse = trimmed;
            if (lineToParse.StartsWith("#HttpOnly_"))
            {
                lineToParse = lineToParse.Substring("#HttpOnly_".Length);
            }

            var parts = lineToParse.Split('\t');
            if (parts.Length >= 7)
            {
                long.TryParse(parts[4], out var exp);
                entries.Add(new CookieEntry
                {
                    Domain = parts[0],
                    IncludeSubdomains = parts[1].Equals("TRUE", StringComparison.OrdinalIgnoreCase),
                    Path = parts[2],
                    Secure = parts[3].Equals("TRUE", StringComparison.OrdinalIgnoreCase),
                    Expiration = exp,
                    Name = parts[5],
                    Value = parts[6]
                });
            }
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(entries, options);
    }

    public static string JsonToNetscape(string jsonText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Netscape HTTP Cookie File");
        sb.AppendLine("# http://curl.haxx.se/rfc/cookie_spec.html");
        sb.AppendLine("# Converted by Cazador YTM");
        sb.AppendLine();

        using var doc = JsonDocument.Parse(jsonText);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return sb.ToString();
        }

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var domain = item.TryGetProperty("domain", out var d) ? d.GetString() ?? "" : "";
            var path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "/" : "/";
            var secure = item.TryGetProperty("secure", out var s) && s.GetBoolean();
            var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var val = item.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";

            long exp = 0;
            if (item.TryGetProperty("expirationDate", out var ed))
            {
                if (ed.ValueKind == JsonValueKind.Number)
                {
                    exp = (long)ed.GetDouble();
                }
            }
            else if (item.TryGetProperty("Expiration", out var expProp) && expProp.ValueKind == JsonValueKind.Number)
            {
                exp = expProp.GetInt64();
            }

            bool includeSubdomains = domain.StartsWith(".");
            string secStr = secure ? "TRUE" : "FALSE";
            string subStr = includeSubdomains ? "TRUE" : "FALSE";

            sb.AppendLine($"{domain}\t{subStr}\t{path}\t{secStr}\t{exp}\t{name}\t{val}");
        }

        return sb.ToString();
    }
}
