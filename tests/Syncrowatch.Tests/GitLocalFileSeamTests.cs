using Shouldly;
using Synchro.Core;
using Syncrowatch.Core;
using Syncrowatch.Core.Transport;
using Xunit;

namespace Syncrowatch.Tests;

public class GitLocalFileSeamTests
{
    [Fact]
    public async Task ListTrackedFiles_MatchesGitLsFiles()
    {
        using var repo = new LocalRepo();
        repo.Write("notes.txt", "hello", "add notes");
        repo.Write("docs/readme.md", "# docs", "add readme");
        File.WriteAllText(Path.Combine(repo.Work, "scratch.tmp"), "never staged");

        var outcome = await repo.Local.ListTrackedFiles(repo.Work, CancellationToken.None);

        var files = outcome.ShouldBeOfType<Result<IReadOnlyList<string>>.Ok>().Value;
        var listed = GitCli.Run(repo.Work, "ls-files").Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        files.ShouldBe(listed);
    }

    [Fact]
    public async Task DirtyPaths_ReadsLiveWorktreeDirtIncludingRenameSides()
    {
        using var repo = new LocalRepo();
        repo.Write("plain.txt", "one", "seed plain");
        repo.Write("mover/old.txt", "two", "seed mover");
        File.WriteAllText(Path.Combine(repo.Work, "plain.txt"), "one edited");
        File.WriteAllText(Path.Combine(repo.Work, "fresh untracked.txt"), "three");
        File.WriteAllText(Path.Combine(repo.Work, "skip.tmp"), "unfiltered dirt");
        GitCli.Ensure(repo.Work, "mv", "mover/old.txt", "mover/renamed.txt");

        var outcome = await repo.Local.DirtyPaths(repo.Work, CancellationToken.None);

        var dirty = outcome.ShouldBeOfType<Result<IReadOnlyList<string>>.Ok>().Value;
        dirty.Count.ShouldBe(5);
        dirty.ShouldContain("plain.txt");
        dirty.ShouldContain("fresh untracked.txt");
        dirty.ShouldContain("mover/old.txt");
        dirty.ShouldContain("mover/renamed.txt");
        dirty.ShouldContain("skip.tmp");
    }

    [Theory]
    [InlineData("folder with spaces/notes here.txt")]
    [InlineData("юникод-папка/отчёт.txt")]
    public async Task HostileShapePaths_FlowThroughListLogAndChangedPaths(string relativePath)
    {
        using var repo = new LocalRepo();
        repo.Write(relativePath, "payload", "seed a hostile path");
        var baseSha = GitCli.Head(repo.Work);
        repo.Write(relativePath, "payload v2", "touch the hostile path");

        var listed = await repo.Local.ListTrackedFiles(repo.Work, CancellationToken.None);
        var reference = GitCli.Run(repo.Work, "ls-files", "-z")
            .Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        listed.ShouldBeOfType<Result<IReadOnlyList<string>>.Ok>().Value.ShouldBe(reference);

        var log = await repo.Local.LogPath(repo.Work, relativePath, 10, 0, CancellationToken.None);
        log.ShouldBeOfType<Result<IReadOnlyList<GitLogEntry>>.Ok>().Value
            .Select(entry => entry.Subject).ShouldBe(["touch the hostile path", "seed a hostile path"]);

        var changed = await repo.Local.ChangedPaths(repo.Work, baseSha, "HEAD", CancellationToken.None);
        changed.ShouldBeOfType<Result<IReadOnlyList<string>>.Ok>().Value.ShouldBe([relativePath]);
    }

    [Fact]
    public async Task ListTrackedFiles_MissingRepositoryRoot_FailsAsNotFound()
    {
        var runner = new FakeGitRunner();
        var missingRoot = Path.Combine(TempDir.Create(), "nope");

        var outcome = await new GitLocal(runner).ListTrackedFiles(missingRoot, CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<string>>.Fail>().Error.Code.ShouldBe("not_found");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task LogPath_FollowsRenameIntoPriorHistory()
    {
        using var repo = new LocalRepo();
        repo.Write("origin.txt", "draft line\n", "seed the draft");
        GitCli.Ensure(repo.Work, "mv", "origin.txt", "moved.txt");
        File.WriteAllText(Path.Combine(repo.Work, "moved.txt"), "draft line\nmore\n");
        GitCli.CommitAll(repo.Work, "rename the draft");

        var outcome = await repo.Local.LogPath(repo.Work, "moved.txt", 10, 0, CancellationToken.None);

        var history = outcome.ShouldBeOfType<Result<IReadOnlyList<GitLogEntry>>.Ok>().Value;
        history.Select(entry => entry.Subject).ShouldBe(["rename the draft", "seed the draft"]);
        history[0].AuthorName.ShouldBe("Test Driver");
        history[0].AuthorEmail.ShouldBe("driver@test.local");
        history[0].Sha.ShouldNotBe(history[1].Sha);
        history[0].WhenUtc.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task LogPath_LimitAndSkip_PageThroughHistory()
    {
        using var repo = new LocalRepo();
        repo.Write("ledger.txt", "one", "first entry");
        repo.Write("ledger.txt", "two", "second entry");
        repo.Write("ledger.txt", "three", "third entry");

        var paged = await repo.Local.LogPath(repo.Work, "ledger.txt", 1, 1, CancellationToken.None);
        var latest = await repo.Local.LogPath(repo.Work, "ledger.txt", 2, 0, CancellationToken.None);

        paged.ShouldBeOfType<Result<IReadOnlyList<GitLogEntry>>.Ok>().Value
            .Select(entry => entry.Subject).ShouldBe(["second entry"]);
        latest.ShouldBeOfType<Result<IReadOnlyList<GitLogEntry>>.Ok>().Value
            .Select(entry => entry.Subject).ShouldBe(["third entry", "second entry"]);
    }

    [Fact]
    public async Task LogPath_NeverTrackedPath_YieldsEmptyHistory()
    {
        using var repo = new LocalRepo();
        repo.Write("real.txt", "content", "seed");

        var outcome = await repo.Local.LogPath(repo.Work, "ghost.txt", 5, 0, CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<GitLogEntry>>.Ok>().Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task LogPath_EmptySubjectCommit_IsDroppedFromTheTimeline()
    {
        using var repo = new LocalRepo();
        repo.Write("ledger.txt", "one", "seed the ledger");
        File.WriteAllText(Path.Combine(repo.Work, "ledger.txt"), "two");
        GitCli.Ensure(repo.Work, "commit", "-a", "--allow-empty-message", "-m", "");

        var outcome = await repo.Local.LogPath(repo.Work, "ledger.txt", 10, 0, CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<GitLogEntry>>.Ok>().Value
            .Select(entry => entry.Subject).ShouldBe(["seed the ledger"]);
    }

    [Theory]
    [InlineData("-flag.txt")]
    [InlineData("../outside.txt")]
    public async Task LogPath_UnusablePath_RejectedBeforeAnyGitCall(string relativePath)
    {
        var runner = new FakeGitRunner();

        var outcome = await new GitLocal(runner).LogPath(TempDir.Create(), relativePath, 10, 0, CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<GitLogEntry>>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-3, 2)]
    [InlineData(5, -1)]
    public async Task LogPath_InvalidPaging_RejectedBeforeAnyGitCall(int limit, int skip)
    {
        var runner = new FakeGitRunner();

        var outcome = await new GitLocal(runner).LogPath(TempDir.Create(), "ledger.txt", limit, skip, CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<GitLogEntry>>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task LogPath_CancellationKillsGitAndRethrows()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var stubPath = WriteStub("#!/bin/sh\nsleep 30\n");
        var root = TempDir.Create();
        try
        {
            using var cancellation = new CancellationTokenSource(200);
            var local = new GitLocal(new GitProcess(stubPath));

            await Should.ThrowAsync<OperationCanceledException>(() => local.LogPath(
                root, "ledger.txt", 5, 0, cancellation.Token));
        }
        finally
        {
            Directory.Delete(root, true);
            File.Delete(stubPath);
        }
    }

    [Fact]
    public async Task ChangedPaths_NameOnlyDiffBetweenRevisions()
    {
        using var repo = new LocalRepo();
        repo.Write("a.txt", "first", "seed");
        var baseSha = GitCli.Head(repo.Work);
        File.WriteAllText(Path.Combine(repo.Work, "a.txt"), "changed");
        File.WriteAllText(Path.Combine(repo.Work, "b.txt"), "new file");
        GitCli.CommitAll(repo.Work, "two paths change");

        var outcome = await repo.Local.ChangedPaths(repo.Work, baseSha, "HEAD", CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<string>>.Ok>().Value.ShouldBe(["a.txt", "b.txt"]);
    }

    [Fact]
    public async Task ChangedPaths_RevisionOrder_PinsTheDiffDirection()
    {
        using var repo = new LocalRepo();
        repo.Write("origin.txt", "first", "seed the origin");
        var baseSha = GitCli.Head(repo.Work);
        GitCli.Ensure(repo.Work, "mv", "origin.txt", "renamed.txt");
        GitCli.CommitAll(repo.Work, "rename the origin");
        var headSha = GitCli.Head(repo.Work);

        var forward = await repo.Local.ChangedPaths(repo.Work, baseSha, headSha, CancellationToken.None);
        var backward = await repo.Local.ChangedPaths(repo.Work, headSha, baseSha, CancellationToken.None);

        forward.ShouldBeOfType<Result<IReadOnlyList<string>>.Ok>().Value.ShouldBe(["renamed.txt"]);
        backward.ShouldBeOfType<Result<IReadOnlyList<string>>.Ok>().Value.ShouldBe(["origin.txt"]);
    }

    [Theory]
    [InlineData("-upstream", "HEAD")]
    [InlineData("HEAD", "bad:rev")]
    public async Task ChangedPaths_UnusableRevision_RejectedBeforeAnyGitCall(string fromRevision, string toRevision)
    {
        var runner = new FakeGitRunner();

        var outcome = await new GitLocal(runner).ChangedPaths(TempDir.Create(), fromRevision, toRevision, CancellationToken.None);

        outcome.ShouldBeOfType<Result<IReadOnlyList<string>>.Fail>().Error.Code.ShouldBe("invalid");
        runner.Invocations.ShouldBeEmpty();
    }

    private static string WriteStub(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), "syncrowatch-stub-git-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, body);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    private sealed class LocalRepo : IDisposable
    {
        public LocalRepo()
        {
            Root = TempDir.Create();
            Work = Path.Combine(Root, "work");
            Directory.CreateDirectory(Work);
            GitCli.Ensure(Root, "init", "-b", "main", "work");
            GitCli.ConfigureIdentity(Work);
            Local = new GitLocal(new GitProcess());
        }

        public string Root { get; }

        public string Work { get; }

        public GitLocal Local { get; }

        public void Write(string relativePath, string content, string message)
        {
            var fullPath = Path.Combine(Work, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            GitCli.CommitAll(Work, message);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
