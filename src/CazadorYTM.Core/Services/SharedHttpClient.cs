namespace CazadorYTM.Core.Services;

using System;
using System.Net;
using System.Net.Http;

/// <summary>
/// Fábrica y contenedor centralizado de <see cref="HttpClient"/> para evitar
/// duplicación de <see cref="SocketsHttpHandler"/> y garantizar rotación uniforme
/// de conexiones DNS/TLS en todo <c>CazadorYTM.Core</c>.
/// </summary>
public static class SharedHttpClient
{
    public static HttpClient GitHubAndDownloads { get; } = CreateClient(
        connectTimeout: TimeSpan.FromSeconds(15),
        requestTimeout: TimeSpan.FromMinutes(15),
        userAgent: $"CazadorYTM/{Constants.AppVersion} (Windows NT 10.0; Win64; x64)",
        accept: "application/vnd.github.v3+json, application/json, text/plain, */*");

    public static HttpClient Metadata { get; } = CreateClient(
        connectTimeout: TimeSpan.FromSeconds(10),
        requestTimeout: TimeSpan.FromSeconds(10),
        userAgent: $"CazadorYTM/{Constants.AppVersion} (https://github.com/{Constants.GitHubRepo})");

    public static HttpClient CreateClient(
        TimeSpan connectTimeout,
        TimeSpan requestTimeout,
        string userAgent,
        string? accept = null)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = connectTimeout,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        };

        var client = new HttpClient(handler)
        {
            Timeout = requestTimeout
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        if (!string.IsNullOrWhiteSpace(accept))
        {
            client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        }

        return client;
    }
}
