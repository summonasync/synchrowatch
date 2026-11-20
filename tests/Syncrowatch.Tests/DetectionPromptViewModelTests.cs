using Shouldly;
using Syncrowatch.App.Tray;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class DetectionPromptViewModelTests
{
    [Fact]
    public void Message_NamesTheSingleChangedFile()
    {
        var model = new DetectionPromptViewModel(["notes.txt"]);

        model.Message.ShouldBe("\"notes.txt\" changed locally. Sync it to the repository now?");
    }

    [Fact]
    public void Message_CountsMultipleChangedFiles()
    {
        var model = new DetectionPromptViewModel(["notes.txt", "docs/spec.md", "readme.md"]);

        model.Message.ShouldBe("3 files changed locally. Sync them to the repository now?");
    }

    [Fact]
    public void Message_TrimsLongPathsToTheirTail()
    {
        var model = new DetectionPromptViewModel(["very/deep/" + new string('x', 80) + "/file.txt"]);

        model.Message.ShouldStartWith("\"…");
        model.Message.ShouldEndWith("file.txt\" changed locally. Sync it to the repository now?");
    }

    [Fact]
    public void SyncNowCommand_RaisesSyncNowRequestedOnly()
    {
        var model = new DetectionPromptViewModel(["notes.txt"]);
        var syncNow = false;
        var dismissed = false;
        model.SyncNowRequested += (_, _) => syncNow = true;
        model.Dismissed += (_, _) => dismissed = true;

        model.SyncNowCommand.Execute(null);

        syncNow.ShouldBeTrue();
        dismissed.ShouldBeFalse();
    }

    [Fact]
    public void LaterCommand_RaisesDismissedOnly()
    {
        var model = new DetectionPromptViewModel(["notes.txt"]);
        var syncNow = false;
        var dismissed = false;
        model.SyncNowRequested += (_, _) => syncNow = true;
        model.Dismissed += (_, _) => dismissed = true;

        model.LaterCommand.Execute(null);

        dismissed.ShouldBeTrue();
        syncNow.ShouldBeFalse();
    }
}
