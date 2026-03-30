using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UkProxyMonitor
{
    public static class ProcessUtil
    {
        public static async Task<(int ExitCode, string Output)> RunAsync(string file, string args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var sb = new StringBuilder();
            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            await using var _ = ct.Register(() =>
            {
                try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
                tcs.TrySetCanceled(ct);
            });

            var exit = await tcs.Task;
            return (exit, sb.ToString());
        }
    }
}
