using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>A progress photo shown full screen, to look at or delete.</summary>
public sealed partial class PhotoViewModel(FonteStore store, PhotoStore photos, IDialogService dialogs)
    : ObservableObject, IQueryAttributable, ISheetViewModel
{
    private BodyPhoto? _photo;

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    [ObservableProperty]
    private ImageSource? _image;

    [ObservableProperty]
    private string _dayText = string.Empty;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("id", out var id) || !int.TryParse(id?.ToString(), out var photoId))
            return;
        _photo = (await store.GetPhotosAsync()).FirstOrDefault(p => p.Id == photoId);
        if (_photo is null)
            return;
        Image = ImageSource.FromFile(PhotoStore.PathOf(_photo.FileName));
        DayText = Loc.Date(_photo.Date, "dddd d MMMM yyyy");
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_photo is not { } photo)
            return;
        var confirmed = await dialogs.ConfirmAsync(Loc.Get("Photo_DeleteTitle"), Loc.Get("Photo_DeleteText"), Loc.Get("Photo_Delete"));
        if (!confirmed)
            return;
        if (await store.DeletePhotoAsync(photo.Id) is { } fileName)
            photos.Delete(fileName);
        Palette.Haptic();
        await Shell.Current.GoToAsync("..");
    }
}

/// <summary>Two progress photos side by side, the first one and the latest by default.</summary>
public sealed partial class ComparePhotosViewModel(FonteStore store) : ObservableObject, ISheetViewModel
{
    private IReadOnlyList<BodyPhoto> _photos = [];
    private int _before;
    private int _after;

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    [ObservableProperty]
    private ImageSource? _beforeImage;

    [ObservableProperty]
    private ImageSource? _afterImage;

    [ObservableProperty]
    private string _beforeText = string.Empty;

    [ObservableProperty]
    private string _afterText = string.Empty;

    [ObservableProperty]
    private string _gapText = string.Empty;

    public async Task LoadAsync()
    {
        // Oldest first.
        _photos = (await store.GetPhotosAsync()).Reverse().ToList();
        if (_photos.Count == 0)
            return;
        _before = 0;
        _after = _photos.Count - 1;
        Show();
    }

    private void Show()
    {
        var before = _photos[_before];
        var after = _photos[_after];
        BeforeImage = ImageSource.FromFile(PhotoStore.PathOf(before.FileName));
        AfterImage = ImageSource.FromFile(PhotoStore.PathOf(after.FileName));
        BeforeText = before.Date.ToString("d MMM yyyy", Loc.Culture);
        AfterText = after.Date.ToString("d MMM yyyy", Loc.Culture);
        var days = Math.Abs((after.Date.Date - before.Date.Date).TotalDays);
        GapText = days >= 14 ? Loc.Count((int)Math.Round(days / 7), "Week") : Loc.Count((int)days, "Day");
    }

    [RelayCommand]
    private void PreviousBefore() => Move(ref _before, -1);

    [RelayCommand]
    private void NextBefore() => Move(ref _before, 1);

    [RelayCommand]
    private void PreviousAfter() => Move(ref _after, -1);

    [RelayCommand]
    private void NextAfter() => Move(ref _after, 1);

    private void Move(ref int index, int delta)
    {
        if (_photos.Count == 0)
            return;
        index = (index + delta + _photos.Count) % _photos.Count;
        Palette.Haptic();
        Show();
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");
}
