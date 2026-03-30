using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UkProxyMonitor
{
    public static class ConfigStore
    {
        private static readonly string BaseDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UKProxyMonitor");

        private static readonly string ConfigPath = Path.Combine(BaseDir, "config.dat");

        public static AppConfig LoadOrDefault()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return AppConfig.Default();

                var protectedBytes = File.ReadAllBytes(ConfigPath);
                var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(bytes);

                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                return cfg ?? AppConfig.Default();
            }
            catch
            {
                // If config is corrupted or user moved machines, fall back safely
                return AppConfig.Default();
            }
        }

        public static void Save(AppConfig config)
        {
            Directory.CreateDirectory(BaseDir);

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            var bytes = Encoding.UTF8.GetBytes(json);
            var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(ConfigPath, protectedBytes);
        }
    }
}
