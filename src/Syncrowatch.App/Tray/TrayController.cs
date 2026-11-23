using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Syncrowatch.Core;
using Syncrowatch.App.Notifications;
using Syncrowatch.App.Settings;
using Syncrowatch.App.Wizard;
using Syncrowatch.Core.Config;
using Syncrowatch.Core.Engine;
using Syncrowatch.Core.History;
using Syncrowatch.Core.Relay;

namespace Syncrowatch.App.Tray;

public sealed class TrayController : IDisposable
{
    private const string MenuSettingsIconPath = "avares://syncrowatch/Assets/menu/menu-settings-16.png";

    private readonly IServiceProvider _services;
    private readonly TrayIcon _icon;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly TrayStateTracker _tracker;
    private readonly TrayIconCache _icons;
    private readonly AsyncCommand _syncCommand;
    private readonly NativeMenuItem _pauseItem;
    private readonly NativeMenuItem _autoItem;
    private readonly NativeMenuItem _manualItem;
    private readonly NativeMenuItem _semiAutoItem;
    private readonly ISettingsRouter _settingsRouter;
    private ModeController? _controller;
    private RelayPollDriver _relayDriver;
    private readonly DetectionPromptController _detection;
    private readonly SyncNowRunner _syncNow;
    private int _disposed;

    public TrayController(IServiceProvider services, TrayIcon icon, IClassicDesktopStyleApplicationLifetime desktop)
    {
        _services = services;
        _icon = icon;
        _desktop = desktop;
        _tracker = services.GetRequiredService<TrayStateTracker>();
        _tracker.PropertyChanged += OnTrayStateChanged;
        _icons = services.GetRequiredService<TrayIconCache>();
        _syncNow = services.GetRequiredService<SyncNowRunner>();
        _services.GetRequiredService<ToastService>();
        _services.GetRequiredService<TrayStateWatcher>();
        _relayDriver = _services.GetRequiredService<RelayPollDriver>();
        _relayDriver.Start();
        _syncCommand = new AsyncCommand(RunSyncNowAsync, ReadyForSync, HandleCommandFailure);
        _detection = new DetectionPromptController(
            _services.GetRequiredService<ISyncEventBus>(),
            new DetectionPrompt(),
            () => _syncCommand.ExecuteAsync(null),
            HandleCommandFailure);
        _settingsRouter = services.GetRequiredService<ISettingsRouter>();
        _settingsRouter.WizardClosed += OnWizardClosed;
        _pauseItem = MenuActionItem("Pause", TogglePause);
        _autoItem = ModeItem("Auto", SyncRunMode.Auto);
        _manualItem = ModeItem("Manual", SyncRunMode.Manual);
        _semiAutoItem = ModeItem("Semi-auto", SyncRunMode.SemiAuto);
        _icon.Menu = BuildMenu(
            new NativeMenuItem("Sync now") { Command = _syncCommand },
            _pauseItem,
            _autoItem,
            _manualItem,
            _semiAutoItem,
            MenuActionItem("Open history", OpenHistory),
            MenuActionItem("Open files", OpenFiles),
            SettingsItem(),
            MenuActionItem("Exit", Exit));
    }

    internal static NativeMenu BuildMenu(
        NativeMenuItem syncItem,
        NativeMenuItem pauseItem,
        NativeMenuItem autoItem,
        NativeMenuItem manualItem,
        NativeMenuItem semiAutoItem,
        NativeMenuItem historyItem,
        NativeMenuItem filesItem,
        NativeMenuItem settingsItem,
        NativeMenuItem exitItem) => new()
    {
        syncItem,
        pauseItem,
        new NativeMenuItemSeparator(),
        autoItem,
        manualItem,
        semiAutoItem,
        new NativeMenuItemSeparator(),
        historyItem,
        filesItem,
        settingsItem,
        new NativeMenuItemSeparator(),
        exitItem
    };

    public async Task LaunchAsync()
    {
        RefreshTrayVisuals();
        try
        {
            await RebuildAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            _icon.ToolTipText = "Syncrowatch — failed to start";
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (_controller is not null)
        {
            _controller.PropertyChanged -= OnControllerPropertyChanged;
            _controller.Dispose();
        }

        _relayDriver.Dispose();
        _detection.Dispose();
        _settingsRouter.WizardClosed -= OnWizardClosed;
        _tracker.PropertyChanged -= OnTrayStateChanged;
        _tracker.Dispose();
    }

    private async Task RebuildAsync()
    {
        try
        {
            if (_controller is not null)
            {
                _controller.PropertyChanged -= OnControllerPropertyChanged;
                _controller.Dispose();
                _controller = null;
            }

            var configStore = _services.GetRequiredService<ISyncConfigStore>();
            var loaded = await configStore.Load(CancellationToken.None).ConfigureAwait(true);
            if (loaded is not Result<SyncConfig>.Ok ready)
            {
                RefreshTray();
                return;
            }

            var config = ready.Value;
            _controller = new ModeController(
                _services.GetRequiredService<Func<SyncConfig, ISyncEngine>>()(config),
                configStore,
                _services.GetRequiredService<Func<SyncConfig, IFileWatching>>()(config),
                _services.GetRequiredService<ISyncEventBus>(),
                pendingPaths: _services.GetRequiredService<IPendingPathsSource>());
            _controller.PropertyChanged += OnControllerPropertyChanged;
            await _controller.LaunchAsync().ConfigureAwait(true);
            RefreshTray();
        }
        catch (Exception)
        {
            _icon.ToolTipText = "Syncrowatch — failed to reload";
        }
    }

    private async Task RunSyncNowAsync()
    {
        if (_controller is not { } controller)
        {
            ShowHint("Syncrowatch — not configured: finish Settings first");
            return;
        }

        await _syncNow.RunAsync(() => controller.SyncNowAsync(CancellationToken.None)).ConfigureAwait(true);
    }

    private async Task ChangeModeAsync(SyncRunMode mode)
    {
        if (_controller is not { } controller)
        {
            ShowHint("Syncrowatch — not configured: finish Settings first");
            return;
        }

        await controller.SetModeAsync(mode).ConfigureAwait(true);
        RefreshMenu();
    }

    private void TogglePause()
    {
        if (_controller is not { IsConfigured: true } controller)
        {
            ShowHint("Syncrowatch — not configured: finish Settings first");
            return;
        }

        if (controller.IsPaused)
        {
            controller.Resume();
        }
        else
        {
            controller.Pause();
        }

        _tracker.SetPaused(controller.IsPaused);
        RefreshMenu();
    }

    private void OpenHistory() => _services.GetRequiredService<IHistoryView>().Show();

    private void OpenFiles() => _services.GetRequiredService<IFilesView>().Show();

    private void OpenSettings() => _ = _settingsRouter.OpenAsync();

    private NativeMenuItem SettingsItem()
    {
        var item = MenuActionItem("Settings", OpenSettings);
        item.Icon = LoadMenuIcon();
        return item;
    }

    private Bitmap? LoadMenuIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(MenuSettingsIconPath, UriKind.Absolute));
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnWizardClosed(object? sender, EventArgs e) => _ = RebuildAsync();

    private void Exit()
    {
        Dispose();
        _desktop.Shutdown();
    }

    private bool ReadyForSync() => _controller is { IsConfigured: true, IsPaused: false };

    private void HandleCommandFailure(Exception exception)
    {
        _icon.ToolTipText = $"Syncrowatch — error ({exception.Message})";
    }

    private void ShowHint(string text) => _icon.ToolTipText = text;

    private static NativeMenuItem MenuActionItem(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    private NativeMenuItem ModeItem(string header, SyncRunMode mode)
    {
        var item = new NativeMenuItem(header) { ToggleType = NativeMenuItemToggleType.CheckBox };
        item.Click += (_, _) => _ = ChangeModeAsync(mode);
        return item;
    }

    private void RefreshTray()
    {
        RefreshMenu();
        RefreshTooltip();
        _syncCommand.RaiseCanExecuteChanged();
    }

    private void RefreshMenu()
    {
        if (_controller is not { } controller)
        {
            _pauseItem.IsEnabled = false;
            _autoItem.IsChecked = false;
            _manualItem.IsChecked = false;
            _semiAutoItem.IsChecked = false;
            return;
        }

        _pauseItem.IsEnabled = true;
        _pauseItem.Header = controller.IsPaused ? "Resume" : "Pause";
        _autoItem.IsChecked = controller.Mode == SyncRunMode.Auto;
        _manualItem.IsChecked = controller.Mode == SyncRunMode.Manual;
        _semiAutoItem.IsChecked = controller.Mode == SyncRunMode.SemiAuto;
    }

    private void RefreshTooltip()
    {
        _icon.ToolTipText = _controller is null ? "Syncrowatch — not configured" : _tracker.ToolTipText;
    }

    private void RefreshTrayVisuals()
    {
        SwapIcon();
        RefreshTooltip();
    }

    private void SwapIcon()
    {
        var icon = _icons.IconFor(_tracker.State, _tracker.PushPending);
        if (icon is not null && !ReferenceEquals(_icon.Icon, icon))
        {
            _icon.Icon = icon;
        }
    }

    private void OnTrayStateChanged(object? sender, PropertyChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RefreshTrayVisuals);

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RefreshTray);
}
