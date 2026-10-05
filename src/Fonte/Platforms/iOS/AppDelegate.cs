using Foundation;
using UIKit;

namespace Fonte;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
	{
		// Selected tab in the accent colour. Only the tint is set, so the iOS 26 glass tab bar keeps its look.
		UITabBar.Appearance.TintColor = Fonte.Services.AccentTheme.TabTint();
		return base.FinishedLaunching(application, launchOptions);
	}
}
