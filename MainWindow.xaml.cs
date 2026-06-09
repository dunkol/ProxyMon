using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace UkProxyMonitor
{
    public partial class MainWindow : Window
    {
        private AppConfig _config = AppConfig.Default();
        private CancellationTokenSource? _cts;
        private Process? _sshProc;
        private Task? _monitorTask;

        public MainWindow()
        {
            InitializeComponent();

            // Load encrypted config (or defaults)
            _config = ConfigStore.LoadOrDefault();
            UpdateStatus("Idle", ok: true);

            AppendMonitor($"[{Now()}] Loaded config. VPS={_config.VpsUser}@{_config.VpsHost}, SOCKS=127.0.0.1:{_config.SocksPort}");

            if (!string.IsNullOrWhiteSpace(_config.IpinfoToken))
                AppendMonitor($"[{Now()}] IPinfo token present: country check enabled.");
            else
                AppendMonitor($"[{Now()}] IPinfo token not set: country check disabled.");
        }

        private void Start_Click(object sender, RoutedEventArgs e) => StartAll();
        private void Stop_Click(object sender, RoutedEventArgs e) => StopAll();

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            StopAll();
            Close();
        }

        private void Configure_Click(object sender, RoutedEventArgs e)
        {
            var w = new SettingsWindow(_config);
            w.Owner = this;

            if (w.ShowDialog() == true)
            {
                _config = w.Config;

                ConfigStore.Save(_config);

                AppendMonitor($"[{Now()}] Settings saved.");
                AppendMonitor($"[{Now()}] VPS={_config.VpsUser}@{_config.VpsHost}, SOCKS=127.0.0.1:{_config.SocksPort}, HealthPath={_config.HealthPath}");
                AppendMonitor(string.IsNullOrWhiteSpace(_config.IpinfoToken)
                    ? $"[{Now()}] IPinfo token cleared: country check disabled."
                    : $"[{Now()}] IPinfo token set: country check enabled.");
            }
        }

        private void SaveLogs_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save Logs",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"UKProxyLogs_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (dlg.ShowDialog(this) != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("=== SSH LOG ===");
            sb.AppendLine(SshLog.Text);
            sb.AppendLine();
            sb.AppendLine("=== MONITOR LOG ===");

            sb.AppendLine(new TextRange(
                MonitorLog.Document.ContentStart,
                MonitorLog.Document.ContentEnd
            ).Text);

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            AppendMonitor($"[{Now()}] Logs saved: {dlg.FileName}");
        }

        private void StartAll()
        {
            if (_cts != null)
            {
                AppendMonitor($"[{Now()}] Already running.");
                return;
            }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            // Start SSH tunnel
            var autoStartVps = AutoStartVpsCheckBox.IsChecked == true;

            if (autoStartVps)
            {
                try
                {
                    StartSshTunnel();
                    AppendMonitor($"[{Now()}] VPS tunnel started.");
                    UpdateStatus("Running via VPS", ok: true);
                }
                catch (Exception ex)
                {
                    AppendMonitor($"[{Now()}] FAIL - could not start SSH tunnel: {ex.Message}");
                    UpdateStatus("Failed to start", ok: false);
                    StopAll();
                    return;
                }
            }
            else
            {
                AppendMonitor($"[{Now()}] VPS autostart disabled. Running direct country monitor only.");
                UpdateStatus("Direct monitor", ok: true);
            }

            // Start monitor loop
            _monitorTask = Task.Run(() => MonitorLoopAsync(ct), ct);

            AppendMonitor($"[{Now()}] Started.");
        }

        private void StopAll()
        {
            try
            {
                _cts?.Cancel();
            }
            catch { /* ignore */ }
            _cts = null;

            try
            {
                if (_sshProc != null && !_sshProc.HasExited)
                {
                    _sshProc.Kill(entireProcessTree: true);
                }
            }
            catch { /* ignore */ }

            _sshProc = null;
            _monitorTask = null;

            UpdateStatus("Stopped", ok: true);
            AppendMonitor($"[{Now()}] Stopped.");
        }

        private void StartSshTunnel()
        {
            // Validate config
            if (string.IsNullOrWhiteSpace(_config.VpsHost))
                throw new InvalidOperationException("VPS host is empty.");
            if (string.IsNullOrWhiteSpace(_config.VpsUser))
                throw new InvalidOperationException("VPS user is empty.");
            if (string.IsNullOrWhiteSpace(_config.KeyPath) || !File.Exists(_config.KeyPath))
                throw new InvalidOperationException($"SSH key not found: {_config.KeyPath}");

            // Use Windows OpenSSH
            var sshPath = _config.SshExePath;
            if (!File.Exists(sshPath))
                throw new InvalidOperationException($"ssh.exe not found: {sshPath}");

            var hostDest = $"{_config.VpsUser}@{_config.VpsHost}";

            // We do NOT print secrets. Key is a path only.
            string args =
                $"-i \"{_config.KeyPath}\" " +
                "-o IdentitiesOnly=yes -o BatchMode=yes -o PasswordAuthentication=no -o PreferredAuthentications=publickey " +
                $"-D {_config.SocksPort} -N -C -c chacha20-poly1305@openssh.com " +
                "-o TCPKeepAlive=yes -o ServerAliveInterval=15 -o ServerAliveCountMax=2 " +
                hostDest;

            var psi = new ProcessStartInfo
            {
                FileName = sshPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true // we show logs in-app
            };

            _sshProc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _sshProc.OutputDataReceived += (_, e) => { if (e.Data != null) AppendSsh(e.Data); };
            _sshProc.ErrorDataReceived += (_, e) => { if (e.Data != null) AppendSsh(e.Data); };
            _sshProc.Exited += (_, __) =>
            {
                AppendSsh($"[{Now()}] ssh.exe exited (code {_sshProc?.ExitCode}).");
                BeepDisconnect();
            };

            AppendSsh($"[{Now()}] Starting SSH tunnel: {hostDest} (SOCKS 127.0.0.1:{_config.SocksPort})");
            _sshProc.Start();
            _sshProc.BeginOutputReadLine();
            _sshProc.BeginErrorReadLine();
        }

        private async Task MonitorLoopAsync(CancellationToken ct, bool useVpsTunnel = false)
        {
            // Primary DNS-free check: http://<VPS_IP>/health (requires nginx /health)
            var healthUrl = $"http://{_config.VpsHost}{_config.HealthPath}";
            var ipinfoUrl = BuildIpinfoLiteUrl(_config.IpinfoToken);

            AppendMonitor($"[{Now()}] Primary check: {healthUrl}");
            AppendMonitor(string.IsNullOrWhiteSpace(_config.IpinfoToken)
                ? $"[{Now()}] Secondary country check: DISABLED"
                : $"[{Now()}] Secondary country check: {ipinfoUrl}");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 1) Primary: DNS-free health check via curl through SOCKS
                    var healthOk = await CurlHeadViaSocksAsync(healthUrl, _config.SocksPort, ct);

                    // 2) Secondary: optional country check via IPinfo Lite (token)
                    string country = "?";
                    string warn = "";

                    if (useVpsTunnel)
                    {
                        if (!string.IsNullOrWhiteSpace(_config.IpinfoToken))
                        {
                            country = await IpinfoLiteCountryAsync(ipinfoUrl, _config.SocksPort, ct);
                            if (country != "GB")
                                warn = $" WARN(UK={country})";
                        }

                        if (healthOk)
                            AppendMonitor($"[{Now()}] OK   - Proxy OK (VPS /health){warn}");
                        else
                        {
                            AppendMonitor($"[{Now()}] FAIL - Proxy path DOWN (VPS /health)");
                            BeepFail();
                        }
                    }
                    else
                    {
                        country = await IpinfoDirectCountryAsync(ipinfoUrl, ct);
                        AppendMonitor($"[{Now()}] DIRECT - Current country: {country}");
                    }
                }
                catch (OperationCanceledException)
                {
                    // normal
                }
                catch (Exception ex)
                {
                    AppendMonitor($"[{Now()}] FAIL - Monitor exception: {ex.Message}");
                    BeepFail();
                }

                await Task.Delay(TimeSpan.FromSeconds(_config.MonitorIntervalSeconds), ct);
            }
        }

        private async Task<string> IpinfoDirectCountryAsync(string url, CancellationToken ct)
        {
            var curl = _config.CurlExePath;

            // Needed to add --silent because some ISP's block certificate revokation requests
            var args = $"--silent --ssl-no-revoke --show-error --max-time 6 \"{url}\"";

            var (exit, output) = await ProcessUtil.RunAsync(curl, args, ct);

            if (exit != 0 || string.IsNullOrWhiteSpace(output))
                return "?";

            var idx = output.IndexOf("\"country\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "?";

            var colon = output.IndexOf(':', idx);
            if (colon < 0) return "?";

            var quote1 = output.IndexOf('"', colon + 1);
            if (quote1 < 0) return "?";

            var quote2 = output.IndexOf('"', quote1 + 1);
            if (quote2 < 0) return "?";

            var val = output.Substring(quote1 + 1, quote2 - quote1 - 1).Trim();
            return string.IsNullOrWhiteSpace(val) ? "?" : val;
        }

        private static string BuildIpinfoLiteUrl(string? token)
        {
            // IPinfo Lite endpoint: we only care about country code
            // call: https://ipinfo.io/json?token=XYZ
            // (Lite version (my subscription) returns minimal data including country)
            // If token is blank, this won’t be used.
            return $"https://ipinfo.io/json?token={token}";
        }

        private async Task<bool> CurlHeadViaSocksAsync(string url, int socksPort, CancellationToken ct)
        {
            // Use Windows curl.exe (supports SOCKS5)
            var curl = _config.CurlExePath;
            if (!File.Exists(curl))
                throw new FileNotFoundException("curl.exe not found", curl);

            // -I: HEAD
            // --max-time 6: timeout
            // --socks5-hostname: proxy + DNS through SOCKS when hostname is used
            // Here, url is by IP so DNS-free.
            var args = $"--silent --show-error --max-time 6 --socks5-hostname 127.0.0.1:{socksPort} -I \"{url}\"";

            var (exit, output) = await ProcessUtil.RunAsync(curl, args, ct);
            return exit == 0 && output.Contains("200");
        }

        private async Task<string> IpinfoLiteCountryAsync(string url, int socksPort, CancellationToken ct)
        {
            var curl = _config.CurlExePath;
            var args = $"--silent --show-error --max-time 6 --socks5-hostname 127.0.0.1:{socksPort} \"{url}\"";
            var (exit, output) = await ProcessUtil.RunAsync(curl, args, ct);

            if (exit != 0 || string.IsNullOrWhiteSpace(output))
                return "?";

            // Very simple parse: look for "country": "GB"
            // Replace with System.Text.Json later.
            var idx = output.IndexOf("\"country\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "?";

            var colon = output.IndexOf(':', idx);
            if (colon < 0) return "?";

            var quote1 = output.IndexOf('"', colon + 1);
            if (quote1 < 0) return "?";

            var quote2 = output.IndexOf('"', quote1 + 1);
            if (quote2 < 0) return "?";

            var val = output.Substring(quote1 + 1, quote2 - quote1 - 1).Trim();

            return string.IsNullOrWhiteSpace(val) ? "?" : val;
        }

        private void AppendSsh(string line) => Dispatcher.Invoke(() =>
        {
            SshLog.AppendText(line + Environment.NewLine);
            SshLog.ScrollToEnd();
        });

        /// <summary>
        /// TECH DEBT
        /// This method needs refactoring with RegEx to avoid "TOKEN" returning true when checking for "OK"
        ///     
        /// </summary>
        /// <param name="line"></param>
        private void AppendMonitor(string line) => Dispatcher.Invoke(() =>
        {
            Brush colour = Brushes.LightSkyBlue;

            if (line.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||                
                line.Contains("EXCEPTION", StringComparison.OrdinalIgnoreCase))
            {
                colour = Brushes.OrangeRed;
            }
            else if (line.Contains("WARN", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
            {
                colour = Brushes.Gold;
            }
            else if (line.Contains("SECONDARY", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("IPINFO", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("AUTOSTART", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("PRIMARY", StringComparison.OrdinalIgnoreCase))
            {
                colour = Brushes.LightSkyBlue;
            }
            else if (line.Contains("OK", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("LOADED", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("DIRECT", StringComparison.OrdinalIgnoreCase))
            {
                colour = Brushes.LimeGreen;
            }
            else if (line.Contains("STARTED", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("STOPPED", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                colour = Brushes.Gold;
            }

            MonitorLog.Document.Blocks.Add(
                    new Paragraph(new Run(line))
                    {
                        Foreground = colour,
                        Margin = new Thickness(0)
                    });

            MonitorLog.ScrollToEnd();
        });

        private void UpdateStatus(string text, bool ok)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = text;
                StatusText.Foreground = ok ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.OrangeRed;
            });
        }

        private static string Now() => DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");


        /// <summary>
        ///  TECH DEBT
        ///  Replace Beep with PlayNotify(_config) and modify that method and setings page to allow muting sounds
        /// </summary>
        private static void BeepFail()
        {
            try { Console.Beep(800, 180); } catch { }
        }

        private static void BeepDisconnect()
        {
            try { Console.Beep(900, 200); Thread.Sleep(80); Console.Beep(700, 250); } catch { }
        }
    }
}
