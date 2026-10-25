using SQLite;

namespace Fonte.Core.Models;

/// <summary>Where a food comes from.</summary>
public enum FoodSource
{
    /// <summary>The ANSES CIQUAL table shipped with the app (raw and generic foods).</summary>
    Catalog = 0,

    /// <summary>A packaged product found by its barcode on Open Food Facts.</summary>
    Product = 1,

    /// <summary>Typed in by the user.</summary>
    Custom = 2,

    /// <summary>A dish made of other foods.</summary>
    Recipe = 3,
}

public enum Meal
{
    Breakfast = 0,
    Lunch = 1,
    Dinner = 2,
    Snack = 3,
}

/// <summary>A food and its nutrients per 100 g.</summary>
[Table("foods")]
public sealed class Food
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public FoodSource Source { get; set; } = FoodSource.Custom;

    /// <summary>The CIQUAL code of a catalog food, or the barcode of a product.</summary>
    [Indexed]
    public string? Code { get; set; }

    /// <summary>The name, in French for catalog foods (see <see cref="NameEn"/>).</summary>
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary>English name of a catalog food.</summary>
    [MaxLength(160)]
    public string? NameEn { get; set; }

    [MaxLength(80)]
    public string? Brand { get; set; }

    /// <summary>Food group of a catalog food (CIQUAL group code), 0 otherwise.</summary>
    public int Group { get; set; }

    /// <summary>Every name and the brand, normalized (<see cref="TextSearch.Normalize"/>), for searching.</summary>
    public string SearchText { get; set; } = string.Empty;

    public double Kcal { get; set; }

    public double Protein { get; set; }

    public double Carbs { get; set; }

    public double Fat { get; set; }

    public double? Sugars { get; set; }

    public double? Fiber { get; set; }

    public double? Salt { get; set; }

    /// <summary>A usual portion in grams ("1 pot", "1 portion"…), if there is one.</summary>
    public double? ServingGrams { get; set; }

    [MaxLength(40)]
    public string? ServingName { get; set; }

    /// <summary>A recipe's weight once cooked; its nutrients per 100 g are computed against it.</summary>
    public double? TotalGrams { get; set; }

    public bool IsFavorite { get; set; }

    /// <summary>Removed by the user, but kept for the meals already logged with it.</summary>
    public bool IsArchived { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Ignore]
    public bool IsOwn => Source != FoodSource.Catalog;
}

/// <summary>A food eaten on a day, at a meal. Nutrients are kept as they were when it was logged.</summary>
[Table("food_entries")]
public sealed class FoodEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public DateTime Date { get; set; }

    public Meal Meal { get; set; }

    [Indexed]
    public int FoodId { get; set; }

    public double Grams { get; set; }

    public double Kcal { get; set; }

    public double Protein { get; set; }

    public double Carbs { get; set; }

    public double Fat { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

[Table("recipe_ingredients")]
public sealed class RecipeIngredient
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int RecipeId { get; set; }

    public int FoodId { get; set; }

    public double Grams { get; set; }

    public int Position { get; set; }
}

/// <summary>A glass (or any amount) of water drunk.</summary>
[Table("water")]
public sealed class WaterEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public DateTime Date { get; set; }

    public int Milliliters { get; set; }

    public DateTime At { get; set; } = DateTime.Now;
}
