using System.Text.RegularExpressions;
using System.Xml.Linq;
using Fonte.Core.Catalog;
using Fonte.Core.Models;

namespace Fonte.Core.Tests;

/// <summary>Guards the app's .resx translations: same keys everywhere, and every key used in code exists.</summary>
public sealed partial class TranslationTests
{
    private static readonly string AppDirectory = Path.Combine(FindRepositoryRoot(), "src", "Fonte");
    private static readonly string StringsDirectory = Path.Combine(AppDirectory, "Resources", "Strings");

    /// <summary>Keys built at run time from a catalog key or an enum name: checked by the tests below instead.</summary>
    private static readonly string[] DynamicPrefixes = ["Error_", "Ex_", "Muscle_", "Equipment_", "Tracking_"];

    public static TheoryData<string> Languages => new() { "fr", "nl" };

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_language_has_exactly_the_english_keys(string language)
    {
        var english = Load("AppResources.resx");
        var translated = Load($"AppResources.{language}.resx");

        Assert.Empty(english.Keys.Except(translated.Keys));
        Assert.Empty(translated.Keys.Except(english.Keys));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Translations_keep_the_same_placeholders(string language)
    {
        var english = Load("AppResources.resx");
        var translated = Load($"AppResources.{language}.resx");

        var mismatches = english
            .Where(e => translated.TryGetValue(e.Key, out var t) && Placeholders(e.Value) != Placeholders(t))
            .Select(e => e.Key);
        Assert.Empty(mismatches);
        Assert.DoesNotContain(translated, t => string.IsNullOrWhiteSpace(t.Value));
    }

    [Fact]
    public void Every_key_used_in_the_app_exists()
    {
        var keys = Load("AppResources.resx").Keys.ToHashSet();
        var missing = UsedKeys().Where(k => !keys.Contains(k)).Distinct().Order();
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_business_error_is_translated()
    {
        var keys = Load("AppResources.resx").Keys.ToHashSet();
        var missing = Enum.GetNames<FonteError>().Select(e => $"Error_{e}").Where(k => !keys.Contains(k));
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_built_in_exercise_has_a_name_and_no_other()
    {
        var keys = Load("AppResources.resx").Keys.ToHashSet();
        var expected = ExerciseCatalog.All.Select(e => $"Ex_{e.Key}").ToHashSet();

        Assert.Empty(expected.Except(keys));
        Assert.Empty(keys.Where(k => k.StartsWith("Ex_", StringComparison.Ordinal)).Except(expected));
    }

    [Fact]
    public void Every_muscle_group_equipment_and_tracking_is_translated()
    {
        var keys = Load("AppResources.resx").Keys.ToHashSet();
        var expected = Enum.GetNames<MuscleGroup>().Select(n => $"Muscle_{n}")
            .Concat(Enum.GetNames<Equipment>().Select(n => $"Equipment_{n}"))
            .Concat(Enum.GetNames<Tracking>().Select(n => $"Tracking_{n}"));

        Assert.Empty(expected.Except(keys));
    }

    [Fact]
    public void No_translation_is_left_unused()
    {
        var used = UsedKeys().ToHashSet();
        var unused = Load("AppResources.resx").Keys
            .Where(k => !DynamicPrefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal)) && !used.Contains(k))
            .Order();
        Assert.Empty(unused);
    }

    private static IEnumerable<string> UsedKeys()
    {
        foreach (var file in SourceFiles("*.xaml"))
        foreach (Match m in XamlKey().Matches(File.ReadAllText(file)))
            yield return m.Groups[1].Value;

        foreach (var file in SourceFiles("*.cs"))
        {
            var code = File.ReadAllText(file);
            foreach (Match m in CodeKey().Matches(code))
                yield return m.Groups[1].Value;
            foreach (Match m in NounKey().Matches(code))
            {
                yield return m.Groups[1].Value + "_One";
                yield return m.Groups[1].Value + "_Many";
            }
        }
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(AppDirectory, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static Dictionary<string, string> Load(string fileName) =>
        XDocument.Load(Path.Combine(StringsDirectory, fileName))
            .Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);

    private static string Placeholders(string text) =>
        string.Join(",", Placeholder().Matches(text).Select(m => m.Value).Distinct().Order());

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Fonte.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Fonte.slnx not found above the test output.");
    }

    [GeneratedRegex(@"\{l:Tr (\w+)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"(?:Loc\.(?:Get|Format)\(|\bL\[)\s*""(\w+)""")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"Loc\.(?:Count|Noun)\([^,]+,\s*""(\w+)""\)")]
    private static partial Regex NounKey();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();
}
