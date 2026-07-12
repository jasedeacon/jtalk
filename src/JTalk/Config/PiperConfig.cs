namespace JTalk.Config;

public sealed record PiperConfig
{
    public string Exe { get; set; } = @"%APPDATA%\jtalk\piper\venv\Scripts\piper.exe";
    public string VoicesDir { get; set; } = @"%APPDATA%\jtalk\piper\voices";
}
