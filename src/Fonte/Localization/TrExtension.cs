namespace Fonte.Localization;

/// <summary>
/// Binds a text to a translation that follows language changes: <c>Text="{l:Tr Main_Title}"</c>.
/// </summary>
[ContentProperty(nameof(Key))]
public sealed class TrExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]", BindingMode.OneWay, source: Localizer.Instance);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
