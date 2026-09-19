using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using TouchControl.Game;
using TouchControl.Input;
using TouchControl.UI;

namespace TouchControl;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    internal static Configuration Config { get; private set; } = null!;
    internal static Plugin? Instance { get; private set; }

    private const string CommandName = "/touch";

    private readonly WindowSystem windowSystem = new("TouchControl");
    private readonly ConfigWindow configWindow;
    private readonly GameActions game = new();

    internal KeySender Keys { get; }
    internal TouchInput Touch { get; }
    internal OverlayRenderer Overlay { get; }

    public Plugin()
    {
        Instance = this;

        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (!Config.LayoutInitialized)
        {
            Config.ResetLayout();
            Config.Save();
        }

        Keys = new KeySender { Backend = Config.KeyBackend };
        Touch = new TouchInput();
        var sheets = new SheetCache();

        Overlay = new OverlayRenderer(game, Keys, Touch, sheets, ToggleConfig, HideOverlay);
        configWindow = new ConfigWindow(sheets);
        windowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Touch Control settings. /touch on|off|edit|reset",
        });

        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfig;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfig;
        Framework.Update += OnFrameworkUpdate;

        Log.Information("Touch Control loaded. /touch opens the settings.");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfig;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfig;
        CommandManager.RemoveHandler(CommandName);

        windowSystem.RemoveAllWindows();
        Overlay.ReleaseEverything();
        Touch.Dispose();
        Keys.Dispose();

        Instance = null;
    }

    private void OnDraw()
    {
        Overlay.Draw();
        windowSystem.Draw();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // The WndProc subclass has to be installed from the thread that owns the game window, which is this one.
        if (!Touch.Installed && Touch.InstallError == null) Touch.TryInstall(Keys.GameWindow);

        Overlay.OnFrameworkUpdate();
        game.Drain();
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "on":
                Config.Enabled = true;
                Config.Save();
                break;
            case "off":
                HideOverlay();
                break;
            case "edit":
                Config.EditMode = !Config.EditMode;
                Config.Save();
                break;
            case "reset":
                Config.ResetLayout();
                Config.Save();
                break;
            default:
                ToggleConfig();
                break;
        }
    }

    private void ToggleConfig() => configWindow.Toggle();

    private void HideOverlay()
    {
        Config.Enabled = false;
        Config.Save();
        Overlay.ReleaseEverything();
    }
}
