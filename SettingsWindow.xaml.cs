using System.Windows;
using Microsoft.Win32;

namespace UkProxyMonitor
{
    public partial class SettingsWindow : Window
    {
        public AppConfig Config { get; private set; }

        public SettingsWindow(AppConfig current)
        {
            InitializeComponent();
            Config = current.Clone();

            VpsHostBox.Text = Config.VpsHost;
            VpsUserBox.Text = Config.VpsUser;
            KeyPathBox.Text = Config.KeyPath;
            SocksPortBox.Text = Config.SocksPort.ToString();
            IpinfoTokenBox.Text = Config.IpinfoToken ?? "";
            IntervalBox.Text = Config.MonitorIntervalSeconds.ToString();
        }

        private void BrowseKey_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select SSH Private Key",
                Filter = "SSH private key (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true)
                KeyPathBox.Text = dlg.FileName;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Config.VpsHost = VpsHostBox.Text.Trim();
            Config.VpsUser = VpsUserBox.Text.Trim();
            Config.KeyPath = KeyPathBox.Text.Trim();

            if (!int.TryParse(SocksPortBox.Text.Trim(), out var port)) port = 1080;
            Config.SocksPort = port;

            Config.IpinfoToken = string.IsNullOrWhiteSpace(IpinfoTokenBox.Text) ? null : IpinfoTokenBox.Text.Trim();

            if (!int.TryParse(IntervalBox.Text.Trim(), out var sec)) sec = 10;
            Config.MonitorIntervalSeconds = sec;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
