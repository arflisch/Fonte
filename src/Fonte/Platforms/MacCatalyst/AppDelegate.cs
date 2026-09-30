using Foundation;
using UIKit;

namespace Fonte;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override UIKeyCommand[] KeyCommands => KeyboardShortcuts.Current;

	[Export("fonteKey:")]
	private void OnKey(UIKeyCommand command) => KeyboardShortcuts.Handle(command);

	[Export("fonteProbe:")]
	private void OnProbe(NSObject? sender)
	{
	}

	public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
	{
		KeyboardShortcuts.Initialize();

		// Selected tab in the brand violet, as on iPhone.
		UITabBar.Appearance.TintColor = new UIColor(traits =>
			traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark
				? UIColor.FromRGB(0xAF, 0xA9, 0xEC)
				: UIColor.FromRGB(0x53, 0x4A, 0xB7));
		return base.FinishedLaunching(application, launchOptions);
	}
}
