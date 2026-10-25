#if IOS || MACCATALYST
using AVFoundation;
using CoreFoundation;
using CoreGraphics;
using Foundation;
using UIKit;
#endif
using Fonte.Localization;

namespace Fonte.Services;

/// <summary>Reads the barcode of a packaged food with the camera (iPhone and iPad).</summary>
public sealed class BarcodeScanner
{
    public static bool IsSupported => OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst();

    /// <summary>The barcode read, or null when cancelled or the camera can't be used.</summary>
    public async Task<string?> ScanAsync()
    {
#if IOS || MACCATALYST
        if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
            return null;
        await PresentationGuard.WaitUntilSettledAsync();
        var top = Platform.GetCurrentUIViewController();
        if (top is null || AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video) is null)
            return null;
        var result = new TaskCompletionSource<string?>();
        var scanner = new ScannerController(result) { ModalPresentationStyle = UIModalPresentationStyle.FullScreen };
        top.PresentViewController(scanner, true, null);
        return await result.Task;
#else
        await Task.CompletedTask;
        return null;
#endif
    }

#if IOS || MACCATALYST
    private sealed class ScannerController(TaskCompletionSource<string?> result) : UIViewController
    {
        private readonly AVCaptureSession _session = new();
        private AVCaptureVideoPreviewLayer? _preview;
        private bool _done;

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View!.BackgroundColor = UIColor.Black;

            var device = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
            var input = device is null ? null : AVCaptureDeviceInput.FromDevice(device);
            var output = new AVCaptureMetadataOutput();
            if (input is null || !_session.CanAddInput(input) || !_session.CanAddOutput(output))
            {
                Finish(null);
                return;
            }
            _session.AddInput(input);
            _session.AddOutput(output);
            output.SetDelegate(new Reader(Finish), DispatchQueue.MainQueue);
            output.MetadataObjectTypes = AVMetadataObjectType.EAN13Code | AVMetadataObjectType.EAN8Code | AVMetadataObjectType.UPCECode;

            _preview = new AVCaptureVideoPreviewLayer(_session) { VideoGravity = AVLayerVideoGravity.ResizeAspectFill };
            View.Layer.AddSublayer(_preview);

            var frame = new UIView { TranslatesAutoresizingMaskIntoConstraints = false, UserInteractionEnabled = false };
            frame.Layer.BorderColor = UIColor.White.CGColor;
            frame.Layer.BorderWidth = 3;
            frame.Layer.CornerRadius = 18;
            View.AddSubview(frame);

            var hint = new UILabel
            {
                Text = Loc.Get("Scan_Hint"),
                TextColor = UIColor.White,
                TextAlignment = UITextAlignment.Center,
                Lines = 2,
                Font = UIFont.SystemFontOfSize(17, UIFontWeight.Semibold)!,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            View.AddSubview(hint);

            var cancel = UIButton.FromType(UIButtonType.System);
            cancel.SetTitle(Loc.Get("Common_Cancel"), UIControlState.Normal);
            cancel.SetTitleColor(UIColor.White, UIControlState.Normal);
            cancel.TitleLabel!.Font = UIFont.SystemFontOfSize(18, UIFontWeight.Semibold)!;
            cancel.TranslatesAutoresizingMaskIntoConstraints = false;
            cancel.TouchUpInside += (_, _) => Finish(null);
            View.AddSubview(cancel);

            NSLayoutConstraint.ActivateConstraints(
            [
                frame.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor),
                frame.CenterYAnchor.ConstraintEqualTo(View.CenterYAnchor),
                frame.WidthAnchor.ConstraintEqualTo(View.WidthAnchor, 0.78f),
                frame.HeightAnchor.ConstraintEqualTo(170),
                hint.TopAnchor.ConstraintEqualTo(frame.BottomAnchor, 28),
                hint.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor, 24),
                hint.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor, -24),
                cancel.TopAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.TopAnchor, 12),
                cancel.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor, 22),
            ]);
        }

        public override void ViewDidLayoutSubviews()
        {
            base.ViewDidLayoutSubviews();
            if (_preview is not null)
                _preview.Frame = View!.Bounds;
        }

        public override void ViewDidAppear(bool animated)
        {
            base.ViewDidAppear(animated);
            DispatchQueue.GetGlobalQueue(DispatchQueuePriority.High).DispatchAsync(_session.StartRunning);
        }

        private void Finish(string? code)
        {
            if (_done)
                return;
            _done = true;
            if (code is not null)
                new UINotificationFeedbackGenerator().NotificationOccurred(UINotificationFeedbackType.Success);
            _session.StopRunning();
            DismissViewController(true, () => result.TrySetResult(code));
        }

        private sealed class Reader(Action<string?> found) : AVCaptureMetadataOutputObjectsDelegate
        {
            public override void DidOutputMetadataObjects(AVCaptureMetadataOutput captureOutput, AVMetadataObject[] metadataObjects, AVCaptureConnection connection)
            {
                if (metadataObjects.OfType<AVMetadataMachineReadableCodeObject>().FirstOrDefault()?.StringValue is { Length: > 0 } code)
                    found(code);
            }
        }
    }
#endif
}
