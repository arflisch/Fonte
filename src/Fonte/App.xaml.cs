using CommunityToolkit.Mvvm.Messaging;
using Fonte.Core.Data;
using Fonte.Services;
using Fonte.ViewModels;

namespace Fonte;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly IPreferences _preferences;

    public App(AppShell shell, FonteStore store, AppSettings settings, ProService pro, IPreferences preferences)
    {
        InitializeComponent();
        // The colours other than violet come with Fonte Pro.
        AccentTheme.Apply(pro.IsUnlocked ? settings.Accent : AccentTheme.All[0]);
        pro.Changed += (_, _) =>
        {
            AccentTheme.Apply(pro.IsUnlocked ? settings.Accent : AccentTheme.All[0]);
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        };
        _shell = shell;
        _preferences = preferences;

        // Every screen listens (weakly) to this single message instead of holding on to the store.
        store.Changed += (_, _) => WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        settings.Changed += (_, _) => WeakReferenceMessenger.Default.Send(new DataChangedMessage());
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell) { Title = "Fonte" };
        DesktopWindow.Configure(window, _preferences);
        // The progress photos lock again as soon as the app leaves the screen.
        window.Stopped += (_, _) => WeakReferenceMessenger.Default.Send(new AppBackgroundedMessage());
        return window;
    }
}
