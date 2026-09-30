using Fonte.Core.Models;

namespace Fonte.Controls;

/// <summary>Stroke icons drawn on a 24×24 grid (SVG path syntax), rendered by <see cref="IconView"/>.</summary>
public static class Icons
{
    public const string Plus = "M12 5v14M5 12h14";
    public const string Close = "M6 6l12 12M18 6L6 18";
    public const string Check = "M5 12.5l4.5 4.5L19 7";
    public const string ChevronLeft = "M15 5l-7 7 7 7";
    public const string ChevronRight = "M9 5l7 7-7 7";
    public const string ChevronDown = "M5 9l7 7 7-7";
    public const string More = "M6 12h0.01M12 12h0.01M18 12h0.01";
    public const string Pencil = "M15.5 4.5l4 4L8 20H4v-4L15.5 4.5zM13 7l4 4";
    public const string Trash = "M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3";
    public const string Search = "M11 4a7 7 0 1 1 0 14 7 7 0 0 1 0-14zM20 20l-4-4";
    public const string Settings = "M4 7h10M18 7h2M4 17h2M10 17h10M16 4.5a2.5 2.5 0 1 1 0 5 2.5 2.5 0 0 1 0-5zM8 14.5a2.5 2.5 0 1 1 0 5 2.5 2.5 0 0 1 0-5z";
    public const string Globe = "M12 3a9 9 0 1 1 0 18 9 9 0 0 1 0-18zM3 12h18M12 3c2.4 2.5 3.6 5.5 3.6 9s-1.2 6.5-3.6 9c-2.4-2.5-3.6-5.5-3.6-9S9.6 5.5 12 3z";
    public const string Bell = "M6 16v-5a6 6 0 1 1 12 0v5l1.5 2.5h-15zM10 21a2.2 2.2 0 0 0 4 0";
    public const string Timer = "M12 8v5l3 2M9 2h6M12 21a8 8 0 1 0 0-16 8 8 0 0 0 0 16z";
    public const string Trophy = "M7 4h10v5a5 5 0 0 1-10 0zM7 6H4v1a3 3 0 0 0 3 3M17 6h3v1a3 3 0 0 1-3 3M12 14v6M8 20h8";

    // Equipment
    public const string Barbell = "M2 12h20M5 8v8M8 6v12M16 6v12M19 8v8";
    public const string Dumbbell = "M7 12h10M4 9v6M7 7v10M17 7v10M20 9v6";
    public const string Machine = "M6 3v18M18 3v18M6 7h12M9 12h6v5H9z";
    public const string Cable = "M5 3h14M12 3v9M9 12h6v4H9zM12 16v5";
    public const string Bodyweight = "M12 4a2 2 0 1 1 0 4 2 2 0 0 1 0-4zM12 8v7M8 11h8M9 21l3-6 3 6";
    public const string Kettlebell = "M9 8a3 3 0 0 1 6 0v1M7 10h10a2 2 0 0 1 1.9 2.6l-1.5 5A2 2 0 0 1 15.5 19h-7a2 2 0 0 1-1.9-1.4l-1.5-5A2 2 0 0 1 7 10z";
    public const string Target = "M12 3a9 9 0 1 1 0 18 9 9 0 0 1 0-18zM12 8a4 4 0 1 1 0 8 4 4 0 0 1 0-8z";

    public static string For(Equipment equipment) => equipment switch
    {
        Equipment.Barbell => Barbell,
        Equipment.Dumbbell => Dumbbell,
        Equipment.Machine => Machine,
        Equipment.Cable => Cable,
        Equipment.Bodyweight => Bodyweight,
        Equipment.Kettlebell => Kettlebell,
        _ => Target,
    };
}
