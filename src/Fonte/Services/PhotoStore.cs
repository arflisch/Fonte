namespace Fonte.Services;

/// <summary>Progress photos, as files in the app's private storage (never in the phone's photo library).</summary>
public sealed class PhotoStore
{
    public static string Folder => Path.Combine(FileSystem.AppDataDirectory, "photos");

    public static string PathOf(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>Takes a photo with the camera, or picks one from the library; returns the saved file name.</summary>
    public async Task<string?> AddAsync(bool camera)
    {
        FileResult? photo;
        if (camera)
        {
            if (!MediaPicker.Default.IsCaptureSupported)
                return null;
            photo = await MediaPicker.Default.CapturePhotoAsync();
        }
        else
        {
            photo = (await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 1 }))?.FirstOrDefault();
        }
        if (photo is null)
            return null;

        Directory.CreateDirectory(Folder);
        var extension = Path.GetExtension(photo.FileName) is { Length: > 0 } ext ? ext.ToLowerInvariant() : ".jpg";
        var fileName = $"{Guid.NewGuid():N}{extension}";
        await using var source = await photo.OpenReadAsync();
        await using var target = File.Create(PathOf(fileName));
        await source.CopyToAsync(target);
        return fileName;
    }

    public void Delete(string fileName)
    {
        try
        {
            File.Delete(PathOf(fileName));
        }
        catch (IOException)
        {
            // Already gone.
        }
    }
}
