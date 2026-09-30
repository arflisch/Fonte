using System.Runtime.CompilerServices;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Fonte.ViewModels;
using UIKit;

namespace Fonte;

/// <summary>Mac keyboard: Escape closes the current sheet.</summary>
/// <remarks>
/// The key commands live on the app delegate, at the end of the responder chain. A chain only exists while
/// something has the keyboard focus, which nothing has once a sheet opens, an alert closes or a text field is
/// left: each sheet therefore gets an invisible view that takes the focus back whenever it is lost.
/// </remarks>
internal static class KeyboardShortcuts
{
    public static readonly Selector Action = new("fonteKey:");

    /// <summary>Implemented by the app delegate: sending it succeeds only if something has the focus.</summary>
    public static readonly Selector Probe = new("fonteProbe:");

    private static readonly ConditionalWeakTable<Page, FocusHolder> Holders = new();

    private static readonly UIKeyCommand[] SheetKeys = [Command(UIKeyCommand.Escape)];

    /// <summary>The keys to listen to right now: none while an alert or a file panel is in front.</summary>
    public static UIKeyCommand[] Current => TopSheet() is null ? [] : SheetKeys;

    public static void Initialize() => NSTimer.CreateRepeatingScheduledTimer(0.5, _ => KeepFocus());

    /// <summary>Makes <paramref name="page"/> (a sheet) receive the keyboard once it is on screen.</summary>
    public static void Attach(Page page)
    {
        page.Loaded += (_, _) =>
        {
            if (page.Handler?.PlatformView is not UIView root)
                return;
            var holder = Holders.GetValue(page, _ => new FocusHolder());
            if (holder.Superview != root)
                root.AddSubview(holder);
            holder.BecomeFirstResponder();
        };
    }

    public static void Handle(UIKeyCommand command)
    {
        if (command.Input == UIKeyCommand.Escape)
            TopSheet()?.DismissCommand.Execute(null);
    }

    /// <summary>Gives the focus back to the sheet in front when nothing has it (a text field keeps it).</summary>
    private static void KeepFocus()
    {
        if (TopSheet() is null || UIApplication.SharedApplication.SendAction(Probe, null, null, null))
            return;
        if (TopSheetPage() is { } page && Holders.TryGetValue(page, out var holder) && holder.Window is not null)
            holder.BecomeFirstResponder();
    }

    private static UIKeyCommand Command(string input)
    {
        var command = UIKeyCommand.Create((NSString)input, 0, Action);
        command.WantsPriorityOverSystemBehavior = true;
        return command;
    }

    private static Page? TopSheetPage() =>
        Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation.ModalStack.LastOrDefault();

    /// <summary>The sheet in front, unless something else (an alert, a file panel) is presented over it.</summary>
    private static ISheetViewModel? TopSheet() =>
        Platform.GetCurrentUIViewController() is UIAlertController or UIDocumentPickerViewController
            ? null
            : TopSheetPage()?.BindingContext as ISheetViewModel;

    /// <summary>Invisible view that can hold the keyboard focus.</summary>
    private sealed class FocusHolder() : UIView(CGRect.Empty)
    {
        public override bool CanBecomeFirstResponder => true;
    }
}
