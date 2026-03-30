using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls;
using static UkProxyMonitor.AppConfig;


namespace UkProxyMonitor
{
    public partial class MainWindow : Window
    {
        private AppConfig _config = AppConfig.Default();
        private CancellationTokenSource? _cts;
        private Process? _sshProc;
        private Task? _monitorTask;
        private const string AppVersion = "1.0.0";
        private const string CopyrightText = "© 2026 Ortino Limited – Company No. 06137754";
        private int _warningCount = 0;
        private int _errorCount = 0;
        private CancellationTokenSource? _gatewayCts;
        private Task? _gatewayTask;
        private GatewayProfile? _selectedGateway;
        private bool IsVpsRunning() => _cts != null; // StartAll sets _cts; StopAll clears it

        private void ResetCounters()
        {
            _warningCount = 0;
            _errorCount = 0;
            UpdateCountersUi();
        }

        private void IncWarning()
        {
            _warningCount++;
            UpdateCountersUi();
        }

        private void IncError()
        {
            _errorCount++;
            UpdateCountersUi();
        }

        private void UpdateCountersUi()
        {
            Dispatcher.Invoke(() =>
            {
                WarningsText.Text = _warningCount.ToString();
                ErrorsText.Text = _errorCount.ToString();
            });
        }

        public MainWindow()
        {
            InitializeComponent();

            // Load encrypted config (or defaults)
            _config = ConfigStore.LoadOrDefault();
            RebuildGatewayMenu();

            UpdateStatus("Idle", ok: true);

            FooterText.Text = $"{CopyrightText}   |   Version {AppVersion}";

            AppendMonitor($"[{Now()}] Proxy Monitor v{AppVersion}");
            AppendMonitor($"[{Now()}] {CopyrightText}");
            AppendMonitor($"[{Now()}] Loaded config. VPS={_config.VpsUser}@{_config.VpsHost}, SOCKS=127.0.0.1:{_config.SocksPort}");

            if (!string.IsNullOrWhiteSpace(_config.IpinfoToken))
                AppendMonitor($"[{Now()}] IPinfo token present: country check enabled.");
            else
                AppendMonitor($"[{Now()}] IPinfo token not set: country check disabled.");
        }

        private void Start_Click(object sender, RoutedEventArgs e) => StartAll();
        private void Stop_Click(object sender, RoutedEventArgs e) => StopAll();

        //private async void GatewayAuto_Click(object sender, RoutedEventArgs e)
        //{
        //    await SwitchGatewayAsync(GatewayMode.Auto);
        //}

        private async void GatewayRut_Click(object sender, RoutedEventArgs e)
        {
            await SwitchGatewayAsync(GatewayMode.RutPrimary);
        }

        private async void GatewayStarlink_Click(object sender, RoutedEventArgs e)
        {
            await SwitchGatewayAsync(GatewayMode.StarlinkPrimary);
        }

        private void RebuildGatewayMenu()
        {
            GatewayMenuRoot.Items.Clear();

            foreach (var gw in _config.Gateways)
            {
                var item = new MenuItem { Header = gw.Name, Tag = gw };
                item.Click += GatewayProfile_Click;
                GatewayMenuRoot.Items.Add(item);
            }

            if (_config.Gateways.Count == 0)
            {
                GatewayMenuRoot.Items.Add(new MenuItem
                {
                    Header = "(No gateways configured)",
                    IsEnabled = false
                });
            }
        }

        private void GatewayProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi || mi.Tag is not GatewayProfile gw) return;

            _selectedGateway = gw;

            // Only run gateway monitoring if VPS is OFF
            if (IsVpsRunning())
            {
                AppendMonitor($"[{Now}] Gateway monitor not started: VPS is running. (Selected: {gw.Name})");
                return;
            }

            ToggleGatewayLoop();
        }


        private async Task SwitchGatewayAsync(GatewayMode mode)
        {
            //try
            //{
            //    LogInfo($"Gateway switch requested: {mode}");

            //    // 1) Apply via Omada
            //    LogInfo("Applying gateway config via Omada…");
            //    await _omada.ApplyGatewayModeAsync(mode);

            //    // 2) Give routing time to settle
            //    await Task.Delay(TimeSpan.FromSeconds(12));

            //    // 3) If Starlink was requested but still flagged Failed, do a soft reset (optional)
            //    if (mode == GatewayMode.StarlinkPrimary)
            //    {
            //        var status = await _omada.GetWanStatusAsync();
            //        LogInfo($"WAN status after switch: {status}");

            //        if (status.StarlinkFailed)
            //        {
            //            LogWarn("Starlink still shows FAILED. Performing soft-reset: temporarily disabling RUT WAN…");
            //            await _omada.SetWanEnabledAsync(WanPort.Rut, enabled: false);
            //            await Task.Delay(TimeSpan.FromSeconds(10));
            //            await _omada.SetWanEnabledAsync(WanPort.Rut, enabled: true);

            //            await Task.Delay(TimeSpan.FromSeconds(12));
            //            status = await _omada.GetWanStatusAsync();
            //            LogInfo($"WAN status after soft reset: {status}");
            //        }
            //    }

            //    // 4) Run the existing “where am I” check and log it
            //    LogInfo("Running IPinfo check…");
            //    var check = await RunIpInfoCheckAsync();   // your existing code
            //    LogOk($"IP check: {check.CountryCode}  {check.IpAddress}");

            //    LogOk($"Gateway switch complete: {mode}");
            //}
            //catch (Exception ex)
            //{
            //    LogFail($"Gateway switch failed: {ex.Message}");
            //}
        }

        private enum GatewayMode
        {
            Auto,
            RutPrimary,
            StarlinkPrimary
        }


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

                RebuildGatewayMenu();

                AppendMonitor($"[{Now()}] Settings saved.");
                AppendMonitor($"[{Now()}] VPS={_config.VpsUser}@{_config.VpsHost}, SOCKS=127.0.0.1:{_config.SocksPort}, HealthPath={_config.HealthPath}");
                AppendMonitor(string.IsNullOrWhiteSpace(_config.IpinfoToken)
                    ? $"[{Now()}] IPinfo token cleared: country check disabled."
                    : $"[{Now()}] IPinfo token set: country check enabled.");
            }
        }

        private void SetGatewayStatus(string text, bool ok)
        {
            Dispatcher.Invoke(() =>
            {
                GatewayStatusText.Text = text;
                GatewayStatusText.Foreground = ok ? Brushes.LightGreen : Brushes.OrangeRed;
            });
        }

        private void ToggleGatewayLoop()
        {
            if (_gatewayCts != null)
                StopGatewayLoop();
            else
                StartGatewayLoop();
        }

        private void StartGatewayLoop()
        {
            if (_selectedGateway == null)
            {
                AppendMonitor("No gateway selected.");
                return;
            }

            if (IsVpsRunning())
            {
                AppendMonitor("Gateway loop cannot start while VPS is running.");
                return;
            }

            _gatewayCts = new CancellationTokenSource();
            var ct = _gatewayCts.Token;

            SetGatewayStatus($"Gateway: Running ({_selectedGateway.Name})", ok: true);
            AppendMonitor($"[{Now}] Gateway monitor started ({_selectedGateway.Name}).");

            _gatewayTask = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await GatewayCheckOnceAsync(_selectedGateway, ct);
                    }
                    catch (Exception ex)
                    {
                        AppendMonitor($"[{Now}] FAIL - Gateway monitor exception: {ex.Message}");
                        IncError();
                    }

                    await Task.Delay(TimeSpan.FromSeconds(_config.MonitorIntervalSeconds), ct);
                }
            }, ct);
        }

        //private void StopGatewayLoop()
        //{
        //    try { _gatewayCts?.Cancel(); } catch { }
        //    _gatewayCts = null;
        //    _gatewayTask = null;

        //    SetGatewayStatus("Gateway: Stopped", ok: true);
        //    AppendMonitor("Gateway monitor stopped.");
        //}

        private async Task GatewayCheckOnceAsync(GatewayProfile gw, CancellationToken ct)
        {
            // Connectivity ping
            bool pingOk = await PingHostAsync("1.1.1.1");
            if (!pingOk)
            {
                AppendMonitor($"FAIL - Gateway Ping ({gw.Name})");
                IncError();
                AudioPlayer.PlayNotify(_config);
                return;
            }

            // Country check (DIRECT) using ipinfo /country
            var (ok, body) = await HttpGetDirectAsync("https://ipinfo.io/country", ct);
            if (!ok)
            {
                AppendMonitor($"[{Now}] WARN - Gateway Ping ({gw.Name}) ok but IPinfo failed ({body})");
                IncWarning();
                return;
            }

            var cc = body.Trim().ToUpperInvariant();
            if (cc == "GB")
            {
                AppendMonitor($"[{Now}] OK   - Gateway Ping ({gw.Name}) UK={cc}");
            }
            else
            {
                AppendMonitor($"[{Now}] WARN - Gateway Ping ({gw.Name}) UK={cc} (expected GB)");
                IncWarning();
            }
        }

        private static async Task<(bool ok, string body)> HttpGetDirectAsync(string url, CancellationToken ct)
        {
            using var http = new System.Net.Http.HttpClient(
                new System.Net.Http.HttpClientHandler { UseProxy = false }
            )
            { Timeout = TimeSpan.FromSeconds(6) };

            try
            {
                var body = await http.GetStringAsync(url, ct);
                return (true, body);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }


        private static string GetRichTextBoxText(System.Windows.Controls.RichTextBox rtb)
        {
            var range = new System.Windows.Documents.TextRange(
                rtb.Document.ContentStart,
                rtb.Document.ContentEnd);

            // RichTextBox adds a trailing newline; trim if you want
            return range.Text.TrimEnd();
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
            sb.AppendLine(GetRichTextBoxText(SshLog));
            sb.AppendLine();
            sb.AppendLine("=== MONITOR LOG ===");
            sb.AppendLine(GetRichTextBoxText(MonitorLog));

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            AppendMonitor($"[{Now()}] Logs saved: {dlg.FileName}");
        }

        private void StartAll()
        {
            StopGatewayLoop();

            if (_cts != null)
            {
                AppendMonitor($"[{Now()}] Already running.");
                return;
            }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            // Start SSH tunnel
            try
            {
                StartSshTunnel();
                UpdateStatus("Running", ok: true);
            }
            catch (Exception ex)
            {
                AppendMonitor($"[{Now()}] FAIL - could not start SSH tunnel: {ex.Message}");
                UpdateStatus("Failed to start", ok: false);
                StopAll();
                return;
            }

            // Start monitor loop
            _monitorTask = Task.Run(() => MonitorLoopAsync(ct), ct);

            AppendMonitor($"[{Now()}] Started SSH tunnel (VPS).");
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
            AppendMonitor($"[{Now()}] Stopped SSH tunnel (VPS).");

            StartGatewayLoop();
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

            // NOTE: we don’t use -N? We DO use -N (no remote command).
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
                //BeepDisconnect();
                AudioPlayer.PlayNotify(_config);
            };

            AppendSsh($"[{Now()}] Starting SSH tunnel: {hostDest} (SOCKS 127.0.0.1:{_config.SocksPort})");
            _sshProc.Start();
            _sshProc.BeginOutputReadLine();
            _sshProc.BeginErrorReadLine();
        }

        private async Task MonitorLoopAsync(CancellationToken ct)
        {
            // Primary DNS-free check: http://<VPS_IP>/health (requires nginx /health)
            var healthUrl = $"http://{_config.VpsHost}{_config.HealthPath}";
            var ipinfoUrl = BuildIpinfoLiteUrl(_config.IpinfoToken);

            AppendMonitor($"[{Now()}] Primary check: {healthUrl}");
            AppendMonitor(string.IsNullOrWhiteSpace(_config.IpinfoToken)
                ? $"[{Now()}] Secondary country check: DISABLED"
                : $"[{Now()}] Secondary country check: (token=[hidden])");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 1) Primary: DNS-free health check via curl through SOCKS
                    var healthOk = await CurlHeadViaSocksAsync(healthUrl, _config.SocksPort, ct);

                    // 2) Secondary: optional country check via IPinfo Lite (token)
                    string country = "?";
                    string warn = "";

                    if (!string.IsNullOrWhiteSpace(_config.IpinfoToken))
                    {
                        // Lightweight request; do it at configurable cadence if you want later.
                        country = await IpinfoLiteCountryAsync(ipinfoUrl, _config.SocksPort, ct);

                        if (country != "GB")
                            warn = " WARN";
                    }

                    if (healthOk)
                    {
                        //string ukText = string.IsNullOrWhiteSpace(_config.IpinfoToken)
                        //    ? ""
                        //    : $" UK={country}";
                        string ukText = string.IsNullOrWhiteSpace(_config.IpinfoToken)
                            ? ""
                            : $" Country = {country}";

                        AppendMonitor($"[{Now()}] OK - Proxy Ping (VPS) {ukText}{warn}");
                    }
                    else
                    {
                        AppendMonitor($"[{Now()}] FAIL - Proxy path DOWN (VPS)");
                        BeepFail();
                        AudioPlayer.PlayNotify(_config);
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
                    AudioPlayer.PlayNotify(_config);
                }

                await Task.Delay(TimeSpan.FromSeconds(_config.MonitorIntervalSeconds), ct);
            }
        }

        private void GatewayAuto_Click(object sender, RoutedEventArgs e)
        {
            if (_gatewayCts != null)
            {
                StopGatewayLoop();
            }
            else
            {
                StartGatewayLoop();
            }
        }

        //private void StartGatewayLoop()
        //{
        //    _gatewayCts = new CancellationTokenSource();
        //    var ct = _gatewayCts.Token;

        //    AppendMonitor($"[{Now()}] Started Gateway Auto monitor.");

        //    _gatewayTask = Task.Run(async () =>
        //    {
        //        AppendMonitor($"[{Now()}] Primary check: https://ipinfo.io/country (token=[hidden])");

        //        while (!ct.IsCancellationRequested)
        //        {
        //            try
        //            {
        //                await RunManualGatewayCheckAsync();
        //            }
        //            catch (Exception ex)
        //            {
        //                AppendMonitor($"[{Now()}] Gateway loop exception: {ex.Message}");
        //            }

        //            await Task.Delay(TimeSpan.FromSeconds(_config.MonitorIntervalSeconds), ct);
        //        }
        //    }, ct);
        //}

        private void StopGatewayLoop()
        {
            try { _gatewayCts?.Cancel(); } catch { }
            _gatewayCts = null;
            _gatewayTask = null;

            SetGatewayStatus("Gateway: Stopped", ok: true);

            AppendMonitor($"[{Now()}] Stopped Gateway Auto monitor.");
        }



        private async Task RunManualGatewayCheckAsync()
        {
           // AppendMonitor($"{Now()} Gateway Auto check (manual) — verifying internet + country (DIRECT).");

            bool pingOk = await PingHostAsync("1.1.1.1");

            if (!pingOk)
            {
                AppendMonitor($"[{Now()}] Ping failed (1.1.1.1). Internet may be down.");
                return;
            }

         //   AppendMonitor($"{Now()} Ping OK");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var url = "https://ipinfo.io/country";
            var (ok, body) = await HttpGetAsync(url, cts.Token);

            if (!ok)
            {
                AppendMonitor($"[{Now()}] IPinfo DIRECT failed: {body}");
                return;
            }

            var cc = body.Trim().ToUpperInvariant();
          //  AppendMonitor($"{Now()} IPinfo DIRECT raw='{cc}'");

            //if (cc == "GB") 
            //    AppendMonitor($"{Now()} Ping OK Country = {cc} (OK)");

            //else 
              //  AppendMonitor($"[{Now()}] Ping OK Country = {cc}");
            AppendMonitor($"[{Now()}] OK - Gateway Ping (Auto) Country = {cc}");
        }


        //private async Task RunManualGatewayCheckAsync()
        //{
        //    var cfg = ConfigStore.LoadOrDefault();


        //    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        //    var url = "https://ipinfo.io/country";   // plain text "GB"
        //    var (exit, output) = await CurlGetAsync(url, 0, cts.Token); // DIRECT (no SOCKS)

        //    AppendMonitor($"IPinfo raw exit={exit} body='{Snip(output)}'");

        //    var cc = output.Trim().ToUpperInvariant();
        //    if (cc == "GB") AppendMonitor($"IPinfo Country = {cc} (OK)");
        //    else AppendMonitor($"IPinfo Country = {cc} (expected GB)");
        //    //string ipInfoUrl = "https://ipinfo.io/country";

        //    //AppendMonitor("Gateway Auto check (manual) — verifying internet + country (DIRECT)…");

        //    //bool pingOk = await PingHostAsync("1.1.1.1");
        //    //if (!pingOk)
        //    //{
        //    //    AppendMonitor("Ping failed (1.1.1.1). Internet may be down.");
        //    //    return;
        //    //}
        //    //AppendMonitor("Ping OK");

        //    //try
        //    //{
        //    //    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        //    //    var cc = await IpinfoLiteCountryAsync(ipInfoUrl, 0, cts.Token); // DIRECT (no SOCKS)

        //    //    if (cc == "GB") AppendMonitor($"IPinfo Country = {cc} (OK)");
        //    //    else AppendMonitor($"IPinfo Country = {cc} (expected GB)");
        //    //}
        //    //catch (Exception ex)
        //    //{
        //    //    AppendMonitor($"IPinfo check failed: {ex.Message}");
        //    //}
        //}

        private static async Task<(bool ok, string body)> HttpGetAsync(string url, CancellationToken ct)
        {
            using var http = new System.Net.Http.HttpClient(
                new System.Net.Http.HttpClientHandler { UseProxy = false }  // bypass system proxy
            )
            {
                Timeout = TimeSpan.FromSeconds(6)
            };

            try
            {
                var body = await http.GetStringAsync(url, ct);
                return (true, body);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task<(int exit, string output)> CurlGetAsync(string url, int socksPort, CancellationToken ct)
        {
            var curl = _config.CurlExePath;
            if (!File.Exists(curl))
                throw new FileNotFoundException("curl.exe not found", curl);

            string args =
                socksPort > 0
                    ? $"--silent --show-error --max-time 6 --socks5-hostname 127.0.0.1:{socksPort} \"{url}\""
                    : $"--silent --show-error --max-time 6 \"{url}\"";

            return await ProcessUtil.RunAsync(curl, args, ct);
        }

        private static string Snip(string s, int max = 200)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "\\r").Replace("\n", "\\n");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }



        //private async Task RunManualGatewayCheckAsync()
        //{
        //    // Use the same config values your monitor already uses
        //    var cfg = ConfigStore.LoadOrDefault(); // or ReadUiConfig() if you prefer live UI values


        //    int socksPort = cfg.SocksPort; // <-- change if your config name differs
        //    string ipInfoUrl = "https://ipinfo.io/country";// cfg.UkCheckUrl; // <-- change if your config name differs

        //    AppendMonitor("Gateway Auto check (manual) — verifying internet + country…");

        //    // quick connectivity ping (optional but useful)
        //    bool pingOk = await PingHostAsync("1.1.1.1");
        //    if (!pingOk)
        //    {
        //        AppendMonitor("Ping failed (1.1.1.1). Internet may be down.");
        //        return;
        //    }
        //    AppendMonitor("Ping OK");

        //    // Country check via your existing helper (SOCKS-aware)
        //    try
        //    {
        //        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        //        var cc = await IpinfoLiteCountryAsync(ipInfoUrl, 0, cts.Token);

        //        // Your method returns string; normalize logging
        //        if (string.IsNullOrWhiteSpace(cc))
        //        {
        //            AppendMonitor("IPinfo check returned empty country code.");
        //        }
        //        else if (cc.Equals("GB", StringComparison.OrdinalIgnoreCase))
        //        {
        //            AppendMonitor($"IPinfo Country = {cc} (OK)");
        //        }
        //        else
        //        {
        //            AppendMonitor($"IPinfo Country = {cc} (expected GB)");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        AppendMonitor($"IPinfo check failed: {ex.Message}");
        //    }
        //}


        //private async Task RunManualGatewayCheckAsync()
        //{
        //    // Use the same config values your monitor already uses
        //    var cfg = _cfg; // or ReadUiConfig() if you prefer live UI values
        //    int socksPort = cfg.SocksPort; // <-- change if your config name differs
        //    string ipInfoUrl = cfg.UkCheckUrl; // <-- change if your config name differs

        //    LogInfo("Gateway Auto check (manual) — verifying internet + country…");

        //    // quick connectivity ping (optional but useful)
        //    bool pingOk = await PingHostAsync("1.1.1.1");
        //    if (!pingOk)
        //    {
        //        LogFail("Ping failed (1.1.1.1). Internet may be down.");
        //        return;
        //    }
        //    LogOk("Ping OK");

        //    // Country check via your existing helper (SOCKS-aware)
        //    try
        //    {
        //        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        //        var cc = await IpinfoLiteCountryAsync(ipInfoUrl, socksPort, cts.Token);

        //        // Your method returns string; normalize logging
        //        if (string.IsNullOrWhiteSpace(cc))
        //        {
        //            LogWarn("IPinfo check returned empty country code.");
        //        }
        //        else if (cc.Equals("GB", StringComparison.OrdinalIgnoreCase))
        //        {
        //            LogOk($"IPinfo Country = {cc} (OK)");
        //        }
        //        else
        //        {
        //            LogWarn($"IPinfo Country = {cc} (expected GB)");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        LogFail($"IPinfo check failed: {ex.Message}");
        //    }
        //}


        private async Task<bool> PingHostAsync(string host)
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(host, 2000);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }



        private static string BuildIpinfoLiteUrl(string? token)
        {
            // IPinfo Lite endpoint: we only care about country code
            // We'll call: https://ipinfo.io/json?token=XYZ
            // (Lite returns minimal data including country)
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

            // If you want this check to be DIRECT (no tunnel), pass socksPort = 0
            string args =
                socksPort > 0
                    ? $"--silent --show-error --max-time 6 --socks5-hostname 127.0.0.1:{socksPort} \"{url}\""
                    : $"--silent --show-error --max-time 6 \"{url}\"";

            var (exit, output) = await ProcessUtil.RunAsync(curl, args, ct);

            if (exit != 0 || string.IsNullOrWhiteSpace(output))
                return "?";

            output = output.Trim();

            // Case 1: plain country response: "GB"
            if (output.Length <= 4 && output.IndexOf('{') < 0 && output.IndexOf('"') < 0)
                return output.ToUpperInvariant();

            // Case 2: JSON: look for "country": "GB"
            var idx = output.IndexOf("\"country\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "?";
            var colon = output.IndexOf(':', idx);
            if (colon < 0) return "?";
            var quote1 = output.IndexOf('"', colon + 1);
            if (quote1 < 0) return "?";
            var quote2 = output.IndexOf('"', quote1 + 1);
            if (quote2 < 0) return "?";
            var val = output.Substring(quote1 + 1, quote2 - quote1 - 1).Trim();

            return string.IsNullOrWhiteSpace(val) ? "?" : val.ToUpperInvariant();
        }


        //private async Task<string> IpinfoLiteCountryAsync(string url, int socksPort, CancellationToken ct)
        //{
        //    var curl = _config.CurlExePath;
        //    var args = $"--silent --show-error --max-time 6 --socks5-hostname 127.0.0.1:{socksPort} \"{url}\"";
        //    var (exit, output) = await ProcessUtil.RunAsync(curl, args, ct);

        //    if (exit != 0 || string.IsNullOrWhiteSpace(output))
        //        return "?";

        //    // Very simple parse: look for "country": "GB"
        //    // You can replace with System.Text.Json later.
        //    var idx = output.IndexOf("\"country\"", StringComparison.OrdinalIgnoreCase);
        //    if (idx < 0) return "?";
        //    var colon = output.IndexOf(':', idx);
        //    if (colon < 0) return "?";
        //    var quote1 = output.IndexOf('"', colon + 1);
        //    if (quote1 < 0) return "?";
        //    var quote2 = output.IndexOf('"', quote1 + 1);
        //    if (quote2 < 0) return "?";
        //    var val = output.Substring(quote1 + 1, quote2 - quote1 - 1).Trim();
        //    return string.IsNullOrWhiteSpace(val) ? "?" : val;
        //}

        private void AppendSsh(string line) => Dispatcher.Invoke(() =>
        {
            AppendColoredLine(SshLog, line, ClassifySsh(line));
        });

        private void AppendMonitor(string line) => Dispatcher.Invoke(() =>
        {
            AppendColoredLine(MonitorLog, line, ClassifyMonitor(line));
        });

        //private static Brush ClassifySsh(string line)
        //{
        //    // You already tag errors/warnings in your SSH loop as [SSH-ERR] / [SSH-WARN]
        //    if (line.Contains("[SSH-ERR]", StringComparison.OrdinalIgnoreCase)) return Brushes.OrangeRed; // bright red-ish
        //    if (line.Contains("[SSH-WARN]", StringComparison.OrdinalIgnoreCase)) return Brushes.Yellow;
        //    if (line.Contains("Connecting", StringComparison.OrdinalIgnoreCase)) return Brushes.LightGreen;
        //    return Brushes.Cyan;
        //}

        private static Brush ClassifySsh(string line)
        {
            string l = line.ToLowerInvariant();

            // hard errors → red
            if (l.Contains("open failed") ||
                l.Contains("connect failed") ||
                l.Contains("name or service not known") ||
                l.Contains("connection reset") ||
                l.Contains("connection closed") ||
                l.Contains("broken pipe") ||
                l.Contains("no route to host") ||
                l.Contains("timed out"))
            {
                return Brushes.OrangeRed;
            }

            // warnings → yellow
            if (l.Contains("warning") ||
                l.Contains("retry") ||
                l.Contains("reconnecting"))
            {
                return Brushes.Yellow;
            }

            // connection lifecycle → green
            if (l.Contains("connecting") ||
                l.Contains("connected"))
            {
                return Brushes.LightGreen;
            }

            // informational SSH noise → dark cyan
            if (l.Contains("debug") ||
                l.Contains("channel"))
            {
                return Brushes.DarkCyan;
            }

            // default console color
            return Brushes.Cyan;
        }


        private static Brush ClassifyMonitor(string line)
        {
            // Match the style you already output: OK / WARN / FAIL
            if (line.Contains(" FAIL", StringComparison.OrdinalIgnoreCase) || line.Contains("FAIL -", StringComparison.OrdinalIgnoreCase))
                return Brushes.OrangeRed;
            if (line.Contains(" WARN", StringComparison.OrdinalIgnoreCase) || line.Contains("WARN -", StringComparison.OrdinalIgnoreCase))
                return Brushes.Yellow;
            if (line.Contains(" OK", StringComparison.OrdinalIgnoreCase) || line.Contains("OK  -", StringComparison.OrdinalIgnoreCase))
                return Brushes.LightGreen;

            // Headings / info lines
            if (line.Contains("Primary check:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Secondary", StringComparison.OrdinalIgnoreCase) ||
              //  line.Contains("Auto", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Loaded config", StringComparison.OrdinalIgnoreCase))
                return Brushes.DarkCyan;

            return Brushes.Cyan;
        }

        private static void AppendColoredLine(RichTextBox box, string text, Brush color)
        {
            // Ensure document exists
            box.Document ??= new FlowDocument();

            var p = new Paragraph
            {
                Margin = new Thickness(0),
                LineHeight = 14.5 // optional; makes it feel more like console
            };
            p.Inlines.Add(new Run(text) { Foreground = color });

            box.Document.Blocks.Add(p);

            // Keep memory bounded (optional): last N lines
            const int maxLines = 2000;
            while (box.Document.Blocks.Count > maxLines)
                box.Document.Blocks.Remove(box.Document.Blocks.FirstBlock);

            box.ScrollToEnd();
        }


        private void UpdateStatus(string text, bool ok)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = text;
                StatusText.Foreground = ok ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.OrangeRed;
            });
        }

        private static string Now() => DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

        private static void BeepFail()
        {
            try 
            { 
                Console.Beep(800, 180); 
            } catch { }
        }

        private static void BeepDisconnect()
        {
            try 
            { 
                Console.Beep(900, 200); 
                Thread.Sleep(80); 
                Console.Beep(700, 250); 
            } catch { }
        }
    }
}
