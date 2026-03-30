using System;

namespace UkProxyMonitor
{
    public class AppConfig
    {
        public string VpsHost { get; set; } = "";
        public string VpsUser { get; set; } = "root";
        public string KeyPath { get; set; } = "";
        public int SocksPort { get; set; } = 1080;
        public bool MuteSounds { get; set; } = false;
        public float SoundVolume { get; set; } = 0.25f; // 0..1

        // nginx health endpoint path (on the VPS)
        public string HealthPath { get; set; } = "/health";

        // Optional IPinfo Lite token
        public string? IpinfoToken { get; set; } = null;

        // Monitor frequency
        public int MonitorIntervalSeconds { get; set; } = 10;

        // External tool paths
        public string SshExePath { get; set; } = @"C:\Windows\System32\OpenSSH\ssh.exe";
        public string CurlExePath { get; set; } = @"C:\Windows\System32\curl.exe";

        public enum GatewayProfileMode
        {
            Auto,
            Starlink,
            Rut956
        }

        public List<GatewayProfile> Gateways { get; set; } = new List<GatewayProfile>();

        public sealed class GatewayProfile
        {
            //public string Name { get; set; } = "Gateway";
            //public GatewayProfileMode Mode { get; set; } = GatewayProfileMode.Auto;

            public string Name { get; set; } = "Gateway";
            public string Type { get; set; } = GatewayProfileMode.Auto.ToString();  // "Starlink", "RUT", "Auto"

            // optional extra fields
            public string Host { get; set; } = String.Empty;
            public string Notes { get; set; } = String.Empty;
        }

        public static AppConfig Default()
        {
            return new AppConfig
            {
                VpsUser = "root",
                SocksPort = 1080,
                MonitorIntervalSeconds = 10,
                HealthPath = "/health",
            };
        }

        public AppConfig Clone()
        {
            return (AppConfig)MemberwiseClone();
        }
    }
}
