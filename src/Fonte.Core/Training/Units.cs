namespace Fonte.Core.Training;

/// <summary>Loads are stored in kilograms; who prefers pounds sees and types pounds.</summary>
public static class Units
{
    public const double KgPerLb = 0.45359237;

    public static double ToPounds(double kilograms) => kilograms / KgPerLb;

    public static double ToKilograms(double pounds) => pounds * KgPerLb;
}
