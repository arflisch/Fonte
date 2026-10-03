using Foundation;

namespace Fonte;

/// <summary>
/// Apps built with the iOS 27 SDK only launch if they adopt the scene lifecycle: MAUI's scene delegate creates
/// the window (declared under UIApplicationSceneManifest in Info.plist).
/// </summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}
