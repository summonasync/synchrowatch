using Shouldly;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class AssetManifestTests
{
    private static readonly string[] TrayStates = ["idle", "syncing", "conflict", "error", "pending"];
    private static readonly string[] TrayVariants = ["symbolic", "color"];
    private static readonly int[] TraySizes = [32, 24, 16];
    private static readonly int[] AppSizes = [256, 128, 64, 48, 32, 16];
    private static readonly string[] ToastKinds =
    [
        "automerged", "synccompleted", "conflict", "failure",
        "relaypackage", "relaywarning", "linkcopied", "linkpending"
    ];
    private static readonly int[] ToastSizes = [24, 16];
    private static readonly string[] MenuActions =
        ["sync-now", "pause", "open-files", "open-history", "settings", "exit"];
    private static readonly int[] FileTypeSizes = [64, 32, 16];
    private static readonly string[] Sigils =
    [
        "syncrowatch-full", "syncrowatch-bare", "synchro-full", "synchro-bare"
    ];
    private static readonly string[] WizardStepsShipped = ["token", "folder", "preferences", "completed"];
    private static readonly string[] WizardStepsNotDrawn = ["repository", "bootstrap"];

    public static TheoryData<string, int, int> ManifestPngs()
    {
        var cases = new TheoryData<string, int, int>();
        foreach (var state in TrayStates)
            foreach (var variant in TrayVariants)
                foreach (var size in TraySizes)
                    cases.Add($"tray/syncrowatch-tray-{state}-{variant}-{size}.png", size, size);
        foreach (var size in AppSizes)
            cases.Add($"app/syncrowatch-{size}.png", size, size);
        foreach (var kind in ToastKinds)
            foreach (var size in ToastSizes)
                cases.Add($"toasts/toast-{kind}-{size}.png", size, size);
        foreach (var action in MenuActions)
            cases.Add($"menu/menu-{action}-16.png", 16, 16);
        foreach (var size in FileTypeSizes)
            cases.Add($"filetype/synchro-file-{size}.png", size, size);
        foreach (var sigil in Sigils)
            cases.Add($"sigils/{sigil}.png", 0, 0);
        cases.Add("wizard/wizard-hero.png", 0, 0);
        foreach (var step in WizardStepsShipped)
            cases.Add($"wizard/wizard-step-{step}.png", 0, 0);
        cases.Add("empty/empty-files.png", 0, 0);
        cases.Add("empty/empty-history.png", 0, 0);
        return cases;
    }

    [Theory]
    [MemberData(nameof(ManifestPngs))]
    public void Manifest_AssetExistsUnderAppAssetsWithNamedSize(string relativePath, int width, int height)
    {
        var path = AssetPath(relativePath);

        File.Exists(path).ShouldBeTrue($"manifest asset {relativePath} is missing");

        var (actualWidth, actualHeight) = ReadPngSize(path);
        actualWidth.ShouldBeGreaterThan(0);
        actualHeight.ShouldBeGreaterThan(0);

        if (width > 0)
        {
            actualWidth.ShouldBe(width, $"{relativePath} width");
            actualHeight.ShouldBe(height, $"{relativePath} height");
        }
    }

    [Fact]
    public void Manifest_WindowIconIcoCarriesTheRequiredSizes()
    {
        var path = AssetPath("app/syncrowatch.ico");

        File.Exists(path).ShouldBeTrue("app/syncrowatch.ico is missing");

        var bytes = File.ReadAllBytes(path);
        bytes.Length.ShouldBeGreaterThan(6);
        bytes[0].ShouldBe((byte)0, "ico reserved word");
        bytes[1].ShouldBe((byte)0, "ico reserved word");
        bytes[2].ShouldBe((byte)1, "ico type must be icon");
        bytes[3].ShouldBe((byte)0, "ico type must be icon");

        var count = bytes[4] | (bytes[5] << 8);
        var widths = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var offset = 6 + i * 16;
            widths.Add(bytes[offset] == 0 ? 256 : bytes[offset]);
        }

        widths.ShouldBe([16, 24, 32, 48, 256], true);
    }

    [Theory]
    [InlineData("menu/menu-resume-16.png")]
    [InlineData("wizard/wizard-step-repository.png")]
    [InlineData("wizard/wizard-step-bootstrap.png")]
    public void Manifest_DocumentedGaps_AreNotImprovised(string relativePath)
    {
        File.Exists(AssetPath(relativePath)).ShouldBeFalse(
            $"{relativePath} has no drawn source; record a real asset instead of a placeholder");
    }

    private static string AssetPath(string relativePath)
    {
        var root = AppSourceRoot();
        return Path.Combine(root, "src", "Syncrowatch.App", "Assets", relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string AppSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Syncrowatch.App", "Syncrowatch.App.csproj")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("source root with src/Syncrowatch.App not found above test bin directory");

        return dir.FullName;
    }

    private static (int Width, int Height) ReadPngSize(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        var read = stream.ReadAtLeast(header, header.Length, false);
        read.ShouldBeGreaterThanOrEqualTo(24, $"{path} is not a full PNG header");

        header[0].ShouldBe((byte)0x89, "png signature");
        header[1].ShouldBe((byte)0x50, "png signature");
        header[2].ShouldBe((byte)0x4E, "png signature");
        header[3].ShouldBe((byte)0x47, "png signature");

        var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        return (width, height);
    }
}
