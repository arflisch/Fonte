using Foundation;
using UIKit;

namespace Fonte;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
	{
		// Selected tab in the brand violet. Only the tint is set, so the iOS 26 glass tab bar keeps its look.
		UITabBar.Appearance.TintColor = new UIColor(traits =>
			traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark
				? UIColor.FromRGB(0xAF, 0xA9, 0xEC)
				: UIColor.FromRGB(0x53, 0x4A, 0xB7));
		return base.FinishedLaunching(application, launchOptions);
	}
}
