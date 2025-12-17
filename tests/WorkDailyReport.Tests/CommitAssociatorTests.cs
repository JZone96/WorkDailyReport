using WorkDailyReport.ETL;
using Xunit;

namespace WorkDailyReport.Tests;

public class CommitAssociatorTests
{
    [Fact]
    public void Associate_MatchesCommitWithinWindow()
    {
        var start = DateTimeOffset.Parse("2025-08-28T09:00:00+02:00");
        var editor = new NormalizedEvent(
            start,
            start.AddMinutes(30),
            "ActivityWatch",
            "Window",
            "Code",
            "WorkDailyReport - Program.cs",
            null,
            isCoding: true);

        var commit = new CommitEvent(
            RepoName: "WorkDailyReport",
            RepoPath: "D:\\gitProjects\\WorkDailyReport",
            Hash: "abc123",
            Author: "Dev",
            Timestamp: start.AddMinutes(10),
            Message: "feat: add tests");

        var associations = CommitAssociator.Associate(
            new[] { commit },
            new[] { editor },
            windowMinutes: 15);

        Assert.Single(associations);
        Assert.Same(editor, associations[0].EditorEvent);
        Assert.Single(editor.LinkedCommits);
        Assert.Equal(commit.Hash, editor.LinkedCommits[0].Hash);
    }
}
