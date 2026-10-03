using Fonte.Core.Models;

namespace Fonte.Core.Data;

public sealed partial class FonteStore
{
    /// <summary>Records the weight of a day, replacing an earlier one the same day.</summary>
    public async Task<BodyWeight> SaveWeightAsync(DateTime date, double kilograms)
    {
        if (kilograms is < 20 or > 400)
            throw new FonteException(FonteError.InvalidBodyValue, $"{kilograms} kg is not a body weight.");
        var db = await GetConnectionAsync();
        var day = date.Date;
        var entry = await db.Table<BodyWeight>().Where(w => w.Date == day).FirstOrDefaultAsync() ?? new BodyWeight { Date = day };
        entry.Kilograms = Math.Round(kilograms, 1);
        if (entry.Id == 0)
            await db.InsertAsync(entry);
        else
            await db.UpdateAsync(entry);
        OnChanged();
        return entry;
    }

    /// <summary>Every weighing, oldest first.</summary>
    public async Task<IReadOnlyList<BodyWeight>> GetWeightsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<BodyWeight>().OrderBy(w => w.Date).ToListAsync();
    }

    public async Task DeleteWeightAsync(int id)
    {
        var db = await GetConnectionAsync();
        await db.DeleteAsync<BodyWeight>(id);
        OnChanged();
    }

    /// <summary>Records a measurement of a day, replacing an earlier one of the same kind that day.</summary>
    public async Task<BodyMeasurement> SaveMeasurementAsync(DateTime date, MeasurementKind kind, double centimetres)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (centimetres is < 10 or > 300)
            throw new FonteException(FonteError.InvalidBodyValue, $"{centimetres} cm is not a body measurement.");
        var db = await GetConnectionAsync();
        var day = date.Date;
        var entry = await db.Table<BodyMeasurement>().Where(m => m.Date == day && m.Kind == kind).FirstOrDefaultAsync()
            ?? new BodyMeasurement { Date = day, Kind = kind };
        entry.Centimetres = Math.Round(centimetres, 1);
        if (entry.Id == 0)
            await db.InsertAsync(entry);
        else
            await db.UpdateAsync(entry);
        OnChanged();
        return entry;
    }

    /// <summary>Every measurement, oldest first.</summary>
    public async Task<IReadOnlyList<BodyMeasurement>> GetMeasurementsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<BodyMeasurement>().OrderBy(m => m.Date).ToListAsync();
    }

    public async Task DeleteMeasurementAsync(int id)
    {
        var db = await GetConnectionAsync();
        await db.DeleteAsync<BodyMeasurement>(id);
        OnChanged();
    }

    public async Task<BodyPhoto> AddPhotoAsync(DateTime date, string fileName)
    {
        var db = await GetConnectionAsync();
        var photo = new BodyPhoto { Date = date, FileName = fileName };
        await db.InsertAsync(photo);
        OnChanged();
        return photo;
    }

    /// <summary>Progress photos, most recent first.</summary>
    public async Task<IReadOnlyList<BodyPhoto>> GetPhotosAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<BodyPhoto>().OrderByDescending(p => p.Date).ToListAsync();
    }

    /// <summary>Forgets a photo and returns its file name, for the app to delete the picture.</summary>
    public async Task<string?> DeletePhotoAsync(int id)
    {
        var db = await GetConnectionAsync();
        var photo = await db.FindAsync<BodyPhoto>(id);
        if (photo is null)
            return null;
        await db.DeleteAsync(photo);
        OnChanged();
        return photo.FileName;
    }
}
