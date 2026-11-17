using System.Xml.Linq;
using Avalonia.Media;
using Shouldly;
using Syncrowatch.App;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class BrandBrushTests
{
    [Theory]
    [InlineData("BrandInkBrush", "#0B0D0C")]
    [InlineData("BrandInk2Brush", "#121614")]
    [InlineData("BrandParchmentBrush", "#E8E4D9")]
    [InlineData("BrandAshBrush", "#8A9089")]
    [InlineData("BrandEmberBrush", "#FF6B35")]
    [InlineData("BrandLineBrush", "#232A26")]
    public void Brand_ResolvesTokenBrushWithLockedColor(string key, string expectedColor)
    {
        var brand = new Brand();

        var resolved = brand.TryGetResource(key, null, out var value);

        resolved.ShouldBeTrue($"key {key} must be present in Brand.axaml");
        var brush = value.ShouldBeOfType<SolidColorBrush>();
        brush.Color.ShouldBe(Color.Parse(expectedColor));
    }

    [Fact]
    public void App_MergesBrandDictionaryAfterFluentTheme()
    {
        var app = XDocument.Load(AppSourcePath("App.axaml")).Root.ShouldNotBeNull();
        var resources = app.Element(XName.Get("Application.Resources", app.Name.NamespaceName)).ShouldNotBeNull();

        resources
            .Descendants(XName.Get("ResourceInclude", app.Name.NamespaceName))
            .Select(i => i.Attribute("Source")?.Value)
            .ShouldContain("avares://syncrowatch/Brand.axaml");

        var styles = app.Element(XName.Get("Application.Styles", app.Name.NamespaceName)).ShouldNotBeNull();
        styles.Elements(XName.Get("FluentTheme", app.Name.NamespaceName)).ShouldNotBeEmpty();

        var propertyNames = app.Elements().Select(e => e.Name.LocalName).ToArray();
        propertyNames.IndexOf("Application.Styles")
            .ShouldBeLessThan(propertyNames.IndexOf("Application.Resources"));
    }

    private static string AppSourcePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Syncrowatch.App", fileName)))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull($"source file src/Syncrowatch.App/{fileName} not found above test bin directory");

        return Path.Combine(dir.FullName, "src", "Syncrowatch.App", fileName);
    }
}
