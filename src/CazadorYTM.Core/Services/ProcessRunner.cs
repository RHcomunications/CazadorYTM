namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class ProcessResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public bool TimedOut { get; set; }
    public bool Canceled { get; set; }
    public bool OutputTruncated { get; set; }
    public bool ErrorTruncated { get; set; }
    public bool Success => !TimedOut && !Canceled && ExitCode == 0;
}

public static class ProcessRunner
{
    private const int DefaultMaxCapturedChars = 4 * 1024 * 1024;

    private sealed class StreamCaptureState
    {
        public readonly object Lock = new();
        public readonly StringBuilder OutputBuilder = new();
        public readonly StringBuilder ErrorBuilder = new();
        public bool OutputTruncated;
        public bool ErrorTruncated;
    }

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        int timeoutMs = 30000,
        CancellationToken cancellationToken = default,
        Action<string>? outputLineCallback = null,
        Action<string>? errorLineCallback = null,
        int maxCapturedChars = DefaultMaxCapturedChars)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StandardError = "Nombre o ruta de ejecutable no válido."
            };
        }

        if (!File.Exists(fileName))
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StandardError = $"El binario o ejecutable no existe en disco: {fileName}"
            };
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        var capture = new StreamCaptureState();

        using var outputWaitHandle = new SemaphoreSlim(0, 1);
        using var errorWaitHandle = new SemaphoreSlim(0, 1);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                try { outputWaitHandle.Release(); } catch { }
            }
            else
            {
                lock (capture.Lock)
                {
                    AppendBounded(capture.OutputBuilder, e.Data, maxCapturedChars, ref capture.OutputTruncated);
                }
                InvokeSafely(outputLineCallback, e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                try { errorWaitHandle.Release(); } catch { }
            }
            else
            {
                lock (capture.Lock)
                {
                    AppendBounded(capture.ErrorBuilder, e.Data, maxCapturedChars, ref capture.ErrorTruncated);
                }
                InvokeSafely(errorLineCallback, e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessResult
                {
                    ExitCode = -1,
                    StandardError = "No se pudo iniciar el proceso del sistema."
                };
            }

            ProcessTracker.Track(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                var processExitTask = process.WaitForExitAsync();
                var timeoutTask = timeoutMs > 0
                    ? Task.Delay(timeoutMs, cts.Token)
                    : Task.Delay(Timeout.Infinite, cts.Token);

                var completedTask = await Task.WhenAny(processExitTask, timeoutTask);

                if (cancellationToken.IsCancellationRequested)
                {
                    KillProcess(process);
                    await WaitForStreamsAsync(outputWaitHandle, errorWaitHandle);
                    return BuildResult(process, capture, canceled: true);
                }

                if (completedTask == timeoutTask)
                {
                    KillProcess(process);
                    await WaitForStreamsAsync(outputWaitHandle, errorWaitHandle);
                    var timeoutResult = BuildResult(process, capture);
                    timeoutResult.TimedOut = true;
                    timeoutResult.ExitCode = -999;
                    timeoutResult.StandardError += "\n[ERROR] Proceso cancelado defensivamente por tiempo de espera (Timeout).";
                    return timeoutResult;
                }

                await processExitTask;
                await WaitForStreamsAsync(outputWaitHandle, errorWaitHandle);
                return BuildResult(process, capture);
            }
            finally
            {
                cts.Cancel();
            }
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            return BuildResult(process, capture, canceled: true);
        }
        catch (Exception ex)
        {
            KillProcess(process);
            string stdout;
            bool outTrunc;
            bool errTrunc;
            lock (capture.Lock)
            {
                stdout = capture.OutputBuilder.ToString();
                outTrunc = capture.OutputTruncated;
                errTrunc = capture.ErrorTruncated;
            }
            return new ProcessResult
            {
                ExitCode = -1,
                StandardOutput = stdout,
                StandardError = $"Excepción defensiva al ejecutar proceso: {ex.Message}",
                OutputTruncated = outTrunc,
                ErrorTruncated = errTrunc
            };
        }
        finally
        {
            ProcessTracker.Untrack(process);
        }
    }

    private static void AppendBounded(StringBuilder builder, string line, int maxChars, ref bool truncated)
    {
        if (maxChars <= 0)
        {
            return;
        }

        if (builder.Length >= maxChars)
        {
            truncated = true;
            return;
        }

        var remaining = maxChars - builder.Length;
        if (line.Length + Environment.NewLine.Length > remaining)
        {
            var allowed = Math.Max(0, remaining - 1);
            if (allowed > 0)
            {
                builder.Append(line.AsSpan(0, Math.Min(line.Length, allowed)));
            }
            builder.Append('…');
            truncated = true;
            return;
        }

        builder.AppendLine(line);
    }

    private static void InvokeSafely(Action<string>? callback, string line)
    {
        if (callback is null)
        {
            return;
        }

        try
        {
            callback(line);
        }
        catch
        {
        }
    }

    private static async Task WaitForStreamsAsync(SemaphoreSlim outputWaitHandle, SemaphoreSlim errorWaitHandle)
    {
        await Task.WhenAll(
            outputWaitHandle.WaitAsync(2000),
            errorWaitHandle.WaitAsync(2000));
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        try
        {
            process.WaitForExit(3000);
        }
        catch
        {
        }
    }

    private static ProcessResult BuildResult(
        Process process,
        StreamCaptureState capture,
        bool canceled = false)
    {
        var exitCode = -998;
        if (canceled)
        {
            try
            {
                if (process.HasExited)
                {
                    exitCode = process.ExitCode;
                }
            }
            catch
            {
            }
        }
        else
        {
            try
            {
                exitCode = process.ExitCode;
            }
            catch
            {
            }
        }

        string stdout;
        string stderr;
        bool outTrunc;
        bool errTrunc;
        lock (capture.Lock)
        {
            stdout = capture.OutputBuilder.ToString();
            stderr = capture.ErrorBuilder.ToString();
            outTrunc = capture.OutputTruncated;
            errTrunc = capture.ErrorTruncated;
        }

        return new ProcessResult
        {
            ExitCode = exitCode,
            StandardOutput = stdout,
            StandardError = canceled
                ? stderr + "Operación cancelada por el usuario o sistema."
                : stderr,
            Canceled = canceled,
            OutputTruncated = outTrunc,
            ErrorTruncated = errTrunc
        };
    }
}
