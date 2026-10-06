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
    public const string Calendar = "M5 5h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2zM3 10h18M8 3v4M16 3v4";
    public const string Chart = "M4 20h16M7 16v-4M12 16V8M17 16V5";
    public const string Lock = "M6 11h12a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1v-8a1 1 0 0 1 1-1zM8 11V8a4 4 0 0 1 8 0v3";
    public const string Eye = "M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12zM12 9a3 3 0 1 1 0 6 3 3 0 0 1 0-6z";
    public const string EyeOff = "M3 3l18 18M10.6 5.1A10 10 0 0 1 12 5c6.4 0 10 7 10 7a17 17 0 0 1-3.2 4.1M6.6 6.6C3.7 8.4 2 12 2 12s3.6 7 10 7a9.6 9.6 0 0 0 5.4-1.6M9.9 9.9a3 3 0 0 0 4.2 4.2";
    public const string Upload = "M12 15V4M7.5 8.5L12 4l4.5 4.5M5 14v4a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-4";
    public const string Download = "M12 4v11M7.5 10.5L12 15l4.5-4.5M5 14v4a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-4";
    public const string Camera = "M4 8h3l2-3h6l2 3h3a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1zM12 10a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7z";
    public const string Image = "M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM3 16l5-5 4 4 3-3 6 6M15.5 7.5a1.5 1.5 0 1 1 0 3 1.5 1.5 0 0 1 0-3z";
    public const string ArrowUp = "M12 19V5M6 11l6-6 6 6";
    public const string ArrowDown = "M12 5v14M6 13l6 6 6-6";
    public const string Link = "M10 14a4 4 0 0 0 5.66 0l3-3a4 4 0 0 0-5.66-5.66l-1 1M14 10a4 4 0 0 0-5.66 0l-3 3a4 4 0 0 0 5.66 5.66l1-1";
    public const string Play = "M8 5l11 7-11 7z";
    public const string Plate = "M12 3a9 9 0 1 1 0 18 9 9 0 0 1 0-18zM12 10a2 2 0 1 1 0 4 2 2 0 0 1 0-4z";
    public const string Scale = "M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2zM8 11a4 4 0 0 1 8 0M12 11l1.5-2.5";
    public const string Ruler = "M3 15L15 3l6 6L9 21zM7 11l2 2M10 8l2 2M13 5l2 2";
    public const string Flame = "M12 3c1 4 5 5.5 5 10a5 5 0 0 1-10 0c0-2.5 1.5-4 2.5-5 0.3 2 1.3 3 2.5 3.5C12 9 11 6 12 3z";
    public const string Warning = "M12 4l9 16H3zM12 10v4M12 17h0.01";
    public const string Palette = "M12 3a9 9 0 1 0 0 18c1 0 1.5-0.8 1.5-1.5 0-0.4-0.2-0.8-0.4-1.1-0.3-0.3-0.4-0.6-0.4-1.1 0-0.8 0.7-1.5 1.5-1.5H16a5 5 0 0 0 5-5c0-4.4-4-7.8-9-7.8zM7.5 12.5h0.01M9.5 8h0.01M14.5 8h0.01M17 11.5h0.01";
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
