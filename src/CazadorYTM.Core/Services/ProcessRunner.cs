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
    public bool Success => !TimedOut && ExitCode == 0;
}

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        int timeoutMs = 30000,
        CancellationToken cancellationToken = default,
        Action<string>? outputLineCallback = null,
        Action<string>? errorLineCallback = null)
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
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        using var outputWaitHandle = new SemaphoreSlim(0, 1);
        using var errorWaitHandle = new SemaphoreSlim(0, 1);

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                try { outputWaitHandle.Release(); } catch { }
            }
            else
            {
                outputBuilder.AppendLine(e.Data);
                outputLineCallback?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                try { errorWaitHandle.Release(); } catch { }
            }
            else
            {
                errorBuilder.AppendLine(e.Data);
                errorLineCallback?.Invoke(e.Data);
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

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var processExitTask = process.WaitForExitAsync(cts.Token);
            var timeoutTask = Task.Delay(timeoutMs, cts.Token);

            var completedTask = await Task.WhenAny(processExitTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }

                return new ProcessResult
                {
                    TimedOut = true,
                    ExitCode = -999,
                    StandardOutput = outputBuilder.ToString(),
                    StandardError = errorBuilder.ToString() + "\n[ERROR] Proceso cancelado defensivamente por tiempo de espera (Timeout)."
                };
            }

            // Await asynchronous completion of streams
            await Task.WhenAll(outputWaitHandle.WaitAsync(2000), errorWaitHandle.WaitAsync(2000));

            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = outputBuilder.ToString(),
                StandardError = errorBuilder.ToString(),
                TimedOut = false
            };
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult
            {
                TimedOut = true,
                ExitCode = -998,
                StandardError = "Operación cancelada por el usuario o sistema."
            };
        }
        catch (Exception ex)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult
            {
                ExitCode = -1,
                StandardError = $"Excepción defensiva al ejecutar proceso: {ex.Message}"
            };
        }
    }
}
