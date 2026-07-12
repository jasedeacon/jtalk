using System.Drawing.Drawing2D;
using JTalk.Config;
using JTalk.Logging;
using JTalk.Speech;

namespace JTalk.Daemon;

/// <summary>
/// System tray surface for the daemon. Menu handlers only mutate config (the daemon
/// reacts via hot reload) or post to the queue — no daemon logic lives here.
/// </summary>
public sealed class TrayAppContext : ApplicationContext
{
    private readonly ConfigService _config;
    private readonly SpeechQueue _queue;
    private readonly Action _shutdown;
    private readonly Control _marshal; // hidden control for cross-thread marshalling
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _iconNormal;
    private readonly Icon _iconMuted;

    private readonly ToolStripMenuItem _muteItem;
    private readonly ToolStripMenuItem _volumeMenu;
    private readonly ToolStripMenuItem _engineMenu;
    private readonly ToolStripMenuItem _claudeVoiceMenu;
    private readonly ToolStripMenuItem _codexVoiceMenu;

    public TrayAppContext(ConfigService config, SpeechQueue queue, Action shutdown)
    {
        _config = config;
        _queue = queue;
        _shutdown = shutdown;

        _marshal = new Control();
        _marshal.CreateControl();

        _iconNormal = CreateIcon(muted: false);
        _iconMuted = CreateIcon(muted: true);

        _muteItem = new ToolStripMenuItem("Mute", null, (_, _) =>
            _config.Update(c => c with { Muted = !c.Muted }));

        _volumeMenu = new ToolStripMenuItem("Volume");
        foreach (var volume in new[] { 25, 50, 75, 100 })
        {
            _volumeMenu.DropDownItems.Add(new ToolStripMenuItem($"{volume}%", null, (_, _) =>
                _config.Update(c => c with { Volume = volume }))
            {
                Tag = volume,
            });
        }

        _engineMenu = new ToolStripMenuItem("Engine");
        foreach (var engine in new[] { "windows", "piper", "openai" })
        {
            _engineMenu.DropDownItems.Add(new ToolStripMenuItem(engine, null, (_, _) =>
                _config.Update(c => c with { Engine = engine }))
            {
                Tag = engine,
            });
        }

        _claudeVoiceMenu = new ToolStripMenuItem("Claude voice");
        _codexVoiceMenu = new ToolStripMenuItem("Codex voice");

        var menu = _menu = new ContextMenuStrip();
        menu.Items.Add(_muteItem);
        menu.Items.Add(_volumeMenu);
        menu.Items.Add(_engineMenu);
        menu.Items.Add(_claudeVoiceMenu);
        menu.Items.Add(_codexVoiceMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Test voices", null, (_, _) => TestVoices()));
        menu.Items.Add(new ToolStripMenuItem("Open config", null, (_, _) => OpenPath(ConfigService.ConfigPath)));
        menu.Items.Add(new ToolStripMenuItem("Open logs", null, (_, _) => OpenPath(Log.LogsDir)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => _shutdown()));
        menu.Opening += (_, _) => SyncMenu();

        _icon = new NotifyIcon
        {
            Icon = config.Current.Muted ? _iconMuted : _iconNormal,
            Text = "jtalk",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _config.Changed += cfg => RunOnUi(() => SyncIcon(cfg));
        SyncIcon(config.Current);
    }

    /// <summary>Thread-safe daemon shutdown: hides the icon and ends the message loop.</summary>
    public void Shutdown() => RunOnUi(() =>
    {
        _icon.Visible = false;
        _icon.Dispose();
        ExitThread();
    });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Dispose(); // idempotent; Shutdown() usually got here first
            _menu.Dispose();
            _marshal.Dispose();
            _iconNormal.Dispose();
            _iconMuted.Dispose();
        }
        base.Dispose(disposing);
    }

    private void RunOnUi(Action action)
    {
        try
        {
            if (_marshal.InvokeRequired) _marshal.BeginInvoke(action);
            else action();
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
    }

    private void SyncIcon(JTalkConfig cfg)
    {
        _icon.Icon = cfg.Muted ? _iconMuted : _iconNormal;
        _icon.Text = cfg.Muted ? "jtalk (muted)" : $"jtalk — {cfg.Engine}, vol {cfg.Volume}%";
    }

    private void SyncMenu()
    {
        var cfg = _config.Current;
        _muteItem.Checked = cfg.Muted;

        foreach (ToolStripMenuItem item in _volumeMenu.DropDownItems)
            item.Checked = (int)item.Tag! == cfg.Volume;
        foreach (ToolStripMenuItem item in _engineMenu.DropDownItems)
            item.Checked = (string)item.Tag! == cfg.Engine;

        RebuildVoiceMenu(_claudeVoiceMenu, "claude", cfg);
        RebuildVoiceMenu(_codexVoiceMenu, "codex", cfg);
    }

    private void RebuildVoiceMenu(ToolStripMenuItem menu, string tool, JTalkConfig cfg)
    {
        menu.DropDownItems.Clear();
        var current = CurrentVoice(cfg, tool);
        var voices = SpeechEngineFactory.CatalogVoices(cfg).Where(v => v.Engine == cfg.Engine).ToList();
        if (voices.Count == 0)
        {
            menu.DropDownItems.Add(new ToolStripMenuItem($"(no {cfg.Engine} voices found)") { Enabled = false });
            return;
        }
        foreach (var voice in voices)
        {
            var name = voice.Name;
            menu.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => SetVoice(tool, name))
            {
                Checked = current.Length > 0 && name.Contains(current, StringComparison.OrdinalIgnoreCase),
            });
        }
    }

    private static string CurrentVoice(JTalkConfig cfg, string tool)
    {
        var toolConfig = cfg.ToolFor(tool);
        return cfg.Engine switch
        {
            "piper" => toolConfig.PiperVoice,
            "openai" => toolConfig.OpenAIVoice,
            _ => toolConfig.WindowsVoice,
        };
    }

    private void SetVoice(string tool, string name) =>
        _config.Update(c =>
        {
            var tools = new Dictionary<string, ToolConfig>(c.Tools, StringComparer.OrdinalIgnoreCase);
            var toolConfig = c.ToolFor(tool);
            tools[tool] = c.Engine switch
            {
                "piper" => toolConfig with { PiperVoice = name },
                "openai" => toolConfig with { OpenAIVoice = name },
                _ => toolConfig with { WindowsVoice = name },
            };
            return c with { Tools = tools };
        });

    private void TestVoices()
    {
        _queue.Enqueue(new SpeechItem("claude", "say", Task.FromResult("This is the Claude voice.")));
        _queue.Enqueue(new SpeechItem("codex", "say", Task.FromResult("And this is the Codex voice.")));
    }

    private static void OpenPath(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"could not open '{path}': {ex.Message}");
        }
    }

    private static Icon CreateIcon(bool muted)
    {
        // Drawn at runtime so no .ico asset is needed. GetHicon() copies the bitmap into an
        // unowned HICON that lives for the process lifetime (Icon.FromHandle does not own it).
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(muted ? Color.FromArgb(130, 130, 130) : Color.FromArgb(46, 139, 87));
            g.FillEllipse(brush, 2, 2, 28, 28);
            using var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString("J", font);
            g.DrawString("J", font, Brushes.White, (32 - size.Width) / 2f, (32 - size.Height) / 2f);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
