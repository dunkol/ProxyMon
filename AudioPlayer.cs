using NAudio.Wave;
using UkProxyMonitor;

public static class AudioPlayer
{
    private static IWavePlayer? _output;
    private static AudioFileReader? _reader;

    public static void PlayNotify(AppConfig cfg)
    {
        if (cfg.MuteSounds) return;

        try
        {
            Stop();

            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "ringtone-1-46486.mp3");

            _reader = new AudioFileReader(path)
            {
                Volume = Math.Clamp(cfg.SoundVolume, 0f, 1f)
            };

            _output = new WaveOutEvent();
            _output.Init(_reader);
            _output.Play();
        }
        catch { }
    }


    public static void Stop()
    {
        try
        {
            _output?.Stop();
            _output?.Dispose();
            _reader?.Dispose();
        }
        catch { }

        _output = null;
        _reader = null;
    }
}