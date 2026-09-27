namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CazadorYTM.Core;
using CazadorYTM.Core.Models;

public static class FileVerifier
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".opus", ".wav", ".ogg", ".wma", ".aac", ".webm", ".mp4"
    };

    private static readonly HashSet<int> ValidSampleRates = new()
    {
        8000, 11025, 16000, 22050, 32000, 44100, 48000, 88200, 96000
    };

    private const double MinDurationSecs = 5.0;
    private const string ReportFilename = "_REPORTE_INTEGRIDAD.txt";
    private const int MaxVerifyWorkers = 4;

    private static readonly Regex AnsiEscapeRegex = new(@"\x1b\[[0-9;]*[a-zA-ZK]|\x1b\[[0-9;]*[A-Za-z]");

    private static string? RunFfprobe(string ffprobePath, string filepath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("fatal");
        psi.ArgumentList.Add("-print_format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("-show_streams");
        psi.ArgumentList.Add("-show_format");
        psi.ArgumentList.Add(filepath);

        try
        {
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var exited = process.WaitForExit(10000);

            if (!exited)
            {
                DownloadEngine.KillProcessTree(process.Id);
                return null;
            }

            if (!string.IsNullOrWhiteSpace(output))
                return output;
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? DecodeCheck(string ffprobePath, string filepath)
    {
        var ffmpegPath = ffprobePath.Replace("ffprobe.exe", "ffmpeg.exe").Replace("ffprobe", "ffmpeg");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(filepath);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("null");
        psi.ArgumentList.Add("-");

        try
        {
            using var process = Process.Start(psi)!;
            var stderrText = process.StandardError.ReadToEnd();
            var exited = process.WaitForExit(30000);

            if (!exited)
            {
                DownloadEngine.KillProcessTree(process.Id);
                return "Timeout al decodificar (proceso colgado)";
            }

            if (process.ExitCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(stderrText))
                {
                    var errors = stderrText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Take(3)
                        .ToList();
                    if (errors.Count > 0)
                        return string.Join("; ", errors);
                }
                return $"Fallo al decodificar audio (código {process.ExitCode})";
            }
            return null;
        }
        catch (TimeoutException)
        {
            return "Timeout al decodificar (posible archivo muy grande o corrupto)";
        }
        catch (Exception e)
        {
            return $"Error de sistema: {e.Message}";
        }
    }

    public static ResultadoArchivo VerificarArchivo(string ffprobeExe, string filepath, bool deepVerify = false)
    {
        var r = new ResultadoArchivo(filepath);

        var probeDataStr = RunFfprobe(ffprobeExe, filepath);

        if (probeDataStr is null)
        {
            r.Errores.Add("ffprobe no pudo leer el archivo (posible corrupción total)");
            return r;
        }

        using JsonDocument probeData = JsonDocument.Parse(probeDataStr);

        var root = probeData.RootElement;
        var formatInfo = root.TryGetProperty("format", out var fmt) ? fmt : default;
        var streams = root.TryGetProperty("streams", out var str) ? str : default;

        try
        {
            var tBytes = new FileInfo(filepath).Length;
            r.Info["Tamaño"] = $"{tBytes / (1024.0 * 1024.0):F2} MB";
            if (tBytes == 0)
            {
                r.Errores.Add("El archivo tiene 0 bytes (está vacío)");
                return r;
            }
        }
        catch (IOException)
        {
            r.Errores.Add("No se pudo obtener el tamaño del archivo");
            return r;
        }

        var audioStream = streams.ValueKind != JsonValueKind.Null
            ? streams.EnumerateArray().FirstOrDefault(s =>
                s.TryGetProperty("codec_type", out var ct) && ct.GetString() == "audio")
            : default;

        if (audioStream.ValueKind == JsonValueKind.Null)
        {
            r.Errores.Add("No se encontró ningún stream de audio en el archivo");
            return r;
        }

        var codecName = audioStream.TryGetProperty("codec_name", out var cn) ? cn.GetString()?.ToUpper() : "DESCONOCIDO";
        r.Info["Códec"] = codecName ?? "DESCONOCIDO";

        if (audioStream.TryGetProperty("sample_rate", out var sr))
        {
            try
            {
                var sampleRate = int.Parse(sr.GetString() ?? "0");
                r.Info["Frecuencia"] = $"{sampleRate} Hz";
                if (!ValidSampleRates.Contains(sampleRate))
                    r.Advertencias.Add($"Frecuencia de muestreo inusual: {sampleRate} Hz");
            }
            catch { }
        }
        else
        {
            r.Advertencias.Add("No se pudo leer la frecuencia de muestreo");
        }

        var channels = audioStream.TryGetProperty("channels", out var ch) ? ch.GetInt32() : 0;
        var channelLayout = audioStream.TryGetProperty("channel_layout", out var cl) ? cl.GetString() : "";
        if (channels == 1)
            r.Info["Canales"] = "1 (Mono)";
        else if (channels == 2)
            r.Info["Canales"] = "2 (Estéreo)";
        else if (channels > 2)
            r.Info["Canales"] = $"{channels} ({channelLayout ?? "Multicanal"})";
        else
            r.Info["Canales"] = "Desconocido";

        var durStr = formatInfo.ValueKind != JsonValueKind.Null && formatInfo.ValueKind != JsonValueKind.Undefined
            ? (formatInfo.TryGetProperty("duration", out var dur) ? dur.GetString() : null)
            : null;
        if (string.IsNullOrEmpty(durStr) && streams.ValueKind != JsonValueKind.Null && streams.ValueKind != JsonValueKind.Undefined)
        {
            foreach (var s in streams.EnumerateArray())
            {
                if (s.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.String)
                {
                    durStr = d.GetString();
                    if (!string.IsNullOrEmpty(durStr)) break;
                }
            }
        }

        if (!string.IsNullOrEmpty(durStr))
        {
            try
            {
                var durSecs = double.Parse(durStr);
                var mins = (int)(durSecs / 60);
                var secs = (int)(durSecs % 60);
                r.Info["Duración"] = $"{mins}:{secs:D2} ({durSecs:F1}s)";
                if (durSecs < MinDurationSecs)
                    r.Advertencias.Add($"Duración muy corta ({durSecs:F1}s, mínimo {MinDurationSecs}s)");
            }
            catch { }
        }

        var bitrateStr = formatInfo.ValueKind != JsonValueKind.Null && formatInfo.ValueKind != JsonValueKind.Undefined
            ? (formatInfo.TryGetProperty("bit_rate", out var br) ? br.GetString() : null)
            : null;
        if (string.IsNullOrEmpty(bitrateStr) && streams.ValueKind != JsonValueKind.Null && streams.ValueKind != JsonValueKind.Undefined)
        {
            foreach (var s in streams.EnumerateArray())
            {
                if (s.TryGetProperty("bit_rate", out var b) && b.ValueKind == JsonValueKind.String)
                {
                    bitrateStr = b.GetString();
                    if (!string.IsNullOrEmpty(bitrateStr)) break;
                }
            }
        }

        if (!string.IsNullOrEmpty(bitrateStr))
        {
            try
            {
                var brKbps = int.Parse(bitrateStr) / 1000;
                r.Info["Bitrate"] = $"{brKbps} kbps";
                if (brKbps < 64)
                    r.Advertencias.Add($"Bitrate excesivamente bajo ({brKbps} kbps)");
            }
            catch { }
        }

        if (formatInfo.ValueKind != JsonValueKind.Null && formatInfo.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind != JsonValueKind.Null)
        {
            var tagsLower = new Dictionary<string, string>();
            foreach (var prop in tagsProp.EnumerateObject())
                tagsLower[prop.Name.ToLower()] = prop.Value.GetString() ?? "";

            var tieneTitulo = tagsLower.ContainsKey("title") && !string.IsNullOrEmpty(tagsLower["title"]);
            var tieneArtista = tagsLower.ContainsKey("artist") && !string.IsNullOrEmpty(tagsLower["artist"]);

            if (tieneTitulo && tieneArtista)
                r.Info["Etiquetas"] = $"\"{tagsLower["title"]}\" — {tagsLower["artist"]}";
            else if (tieneTitulo)
                r.Info["Etiquetas"] = $"\"{tagsLower["title"]}\" (Sin artista)";
            else
                r.Advertencias.Add("Faltan etiquetas ID3/metadatos principales (Título/Artista)");
        }
        else
        {
            r.Advertencias.Add("Faltan etiquetas ID3/metadatos principales (Título/Artista)");
        }

        if (deepVerify && r.Ok)
        {
            var errDecode = DecodeCheck(ffprobeExe, filepath);
            if (errDecode is not null)
                r.Errores.Add($"Corrupción en stream de audio: {errDecode}");
        }

        return r;
    }

    public static string GenerarReporte(List<ResultadoArchivo> resultados, string carpeta)
    {
        var lineas = new List<string>();
        var fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var (okCnt, warnCnt, errCnt) = Helpers.SummarizeResults(resultados);

        lineas.Add("========================================================================");
        lineas.Add($"  REPORTE DE INTEGRIDAD DE AUDIO — CazadorYTM v{Constants.AppVersion}");
        lineas.Add($"  Fecha: {fecha}");
        lineas.Add($"  Carpeta analizada: {carpeta}");
        lineas.Add("========================================================================");
        lineas.Add("");
        lineas.Add("RESUMEN GENERAL:");
        lineas.Add($"  • Total de archivos analizados: {resultados.Count}");
        lineas.Add($"  • Archivos correctos (OK):     {okCnt}");
        lineas.Add($"  • Archivos con advertencias:   {warnCnt}");
        lineas.Add($"  • Archivos con errores/fallos:  {errCnt}");
        lineas.Add("");
        lineas.Add("------------------------------------------------------------------------");
        lineas.Add("DETALLE POR ARCHIVO:");
        lineas.Add("------------------------------------------------------------------------");

        foreach (var r in resultados)
        {
            lineas.Add($"Archivo: {r.Nombre}");
            lineas.Add($"Estado:  {r.Estado}");
            if (r.Info.Count > 0)
            {
                var infoStr = string.Join(" | ", r.Info.Select(kv => $"{kv.Key}: {kv.Value}"));
                lineas.Add($"Info:    {infoStr}");
            }
            if (r.Errores.Count > 0)
            {
                foreach (var err in r.Errores)
                    lineas.Add($"  [Error] {err}");
            }
            if (r.Advertencias.Count > 0)
            {
                foreach (var adv in r.Advertencias)
                    lineas.Add($"  [Aviso] {adv}");
            }
            lineas.Add("");
        }

        return string.Join("\n", lineas);
    }

    public static List<ResultadoArchivo> VerificarDirectorio(
        string carpeta,
        Action<string>? loggerCallback = null,
        Action<int, string>? progressCallback = null,
        bool deepVerify = false,
        int maxWorkers = MaxVerifyWorkers)
    {
        if (!Directory.Exists(carpeta))
        {
            loggerCallback?.Invoke($"[ERROR] La carpeta no existe: {carpeta}");
            return new List<ResultadoArchivo>();
        }

        var ffprobeLoc = BinaryManager.FindFfmpegPath();
        if (ffprobeLoc is null)
        {
            loggerCallback?.Invoke("[ERROR] ffprobe/ffmpeg no encontrado. No se puede verificar la integridad.");
            return new List<ResultadoArchivo>();
        }

        var ffprobeExe = Path.Combine(ffprobeLoc, Environment.OSVersion.Platform == PlatformID.Win32NT ? "ffprobe.exe" : "ffprobe");

        var archivosAudio = Directory.EnumerateFiles(carpeta, "*.*", SearchOption.AllDirectories)
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLower()))
            .ToList();

        if (archivosAudio.Count == 0)
        {
            loggerCallback?.Invoke("[AVISO] No se encontraron archivos de audio soportados en la carpeta.");
            return new List<ResultadoArchivo>();
        }

        var total = archivosAudio.Count;
        loggerCallback?.Invoke($"[SISTEMA] Iniciando verificación {(deepVerify ? "profunda" : "rápida")} de {total} archivos con {maxWorkers} hilos...");

        var resultados = new List<ResultadoArchivo>();
        var lockObj = new object();
        var completed = 0;

        var options = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };
        Parallel.ForEach(archivosAudio, options, fpath =>
        {
            try
            {
                var res = VerificarArchivo(ffprobeExe, fpath, deepVerify);
                lock (lockObj)
                {
                    resultados.Add(res);
                    completed++;
                    var pct = (int)((completed * 100.0) / total);
                    progressCallback?.Invoke(pct, $"{completed}/{total}");
                }
            }
            catch (Exception e)
            {
                lock (lockObj)
                {
                    var rErr = new ResultadoArchivo(fpath);
                    rErr.Errores.Add($"Excepción durante verificación: {e.Message}");
                    resultados.Add(rErr);
                    completed++;
                    var pct = (int)((completed * 100.0) / total);
                    progressCallback?.Invoke(pct, $"{completed}/{total}");
                }
            }
        });

        resultados.Sort((a, b) => string.Compare(a.Nombre, b.Nombre, StringComparison.Ordinal));

        var reporteContent = GenerarReporte(resultados, carpeta);
        var reportePath = Path.Combine(carpeta, ReportFilename);
        try
        {
            File.WriteAllText(reportePath, reporteContent, Encoding.UTF8);
            loggerCallback?.Invoke($"[SISTEMA] Reporte de integridad guardado en: {reportePath}");
        }
        catch (IOException)
        {
            loggerCallback?.Invoke("[AVISO] No se pudo guardar el archivo de reporte.");
        }

        var (ok, warn, err) = Helpers.SummarizeResults(resultados);
        loggerCallback?.Invoke($"[SISTEMA] Verificación finalizada: {ok} OK, {warn} Avisos, {err} Errores.");

        return resultados;
    }

    public class VerificadorArchivos
    {
        private readonly string? _ffprobePath;

        public VerificadorArchivos(string? ffprobePath = null)
        {
            _ffprobePath = ffprobePath ?? BinaryManager.FindFfmpegPath();
        }

        public ResultadoArchivo VerificarArchivo(string filepath, bool deepVerify = false)
        {
            if (string.IsNullOrEmpty(_ffprobePath))
            {
                var r = new ResultadoArchivo(filepath);
                r.Errores.Add("ffprobe no encontrado");
                return r;
            }

            var ffprobeExe = Path.Combine(_ffprobePath, Environment.OSVersion.Platform == PlatformID.Win32NT ? "ffprobe.exe" : "ffprobe");
            return FileVerifier.VerificarArchivo(ffprobeExe, filepath, deepVerify);
        }

        public List<ResultadoArchivo> VerificarDirectorio(string carpeta, Action<string>? loggerCallback = null, Action<int, string>? progressCallback = null, bool deepVerify = false)
        {
            return FileVerifier.VerificarDirectorio(carpeta, loggerCallback, progressCallback, deepVerify);
        }
    }
}