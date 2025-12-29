using WorkDailyReport.ETL;
using Xunit;

namespace WorkDailyReport.Tests;

public class CommitAssociatorTests
{
    [Fact]
    public void Associate_MatchesCommitWithinWindow()
    {
        var start = DateTimeOffset.Parse("2025-08-28T09:00:00+02:00");
        var coding = new NormalizedEvent(
            start,
            start.AddMinutes(30),
            "ActivityWatch",
            "Window",
            "Code",
            "WorkDailyReport - Program.cs",
            null,
            isCoding: true);

        var browser = new NormalizedEvent(
            start.AddMinutes(-30),
            start.AddMinutes(-10),
            "ActivityWatch",
            "Window",
            "chrome.exe",
            "WorkDailyReport - GitHub",
            "https://github.com/JZone96/WorkDailyReport",
            isCoding: false);
        browser.AssignProject("WorkDailyReport");

        var commit = new CommitEvent(
            RepoName: "WorkDailyReport",
            RepoPath: "D:\\gitProjects\\WorkDailyReport",
            Hash: "abc123",
            Author: "Dev",
            Timestamp: start.AddMinutes(10),
            Message: "feat: add tests");

        var associations = CommitAssociator.Associate(
            new[] { commit },
            new[] { coding, browser },
            windowMinutes: 15);

        Assert.Single(associations);
        var assoc = associations[0];
        Assert.Equal(2, assoc.EditorEvents.Count);
        Assert.Contains(coding, assoc.EditorEvents);
        Assert.Contains(browser, assoc.EditorEvents);
        Assert.Single(coding.LinkedCommits);
        Assert.Equal(commit.Hash, coding.LinkedCommits[0].Hash);
        Assert.Single(browser.LinkedCommits);
        Assert.Equal(commit.Hash, browser.LinkedCommits[0].Hash);
    }

    [Fact]
    public void Associate_CollectsAllDailyEventsBeforeCommit()
    {
        var dayStart = DateTimeOffset.Parse("2025-12-29T09:00:00+01:00");
        var first = new NormalizedEvent(
            dayStart.AddMinutes(10),
            dayStart.AddMinutes(20),
            "ActivityWatch",
            "Window",
            "Code",
            "WorkDailyReport - Program.cs",
            null,
            isCoding: true);
        first.AssignProject("WorkDailyReport");

        var second = new NormalizedEvent(
            dayStart.AddMinutes(30),
            dayStart.AddMinutes(45),
            "ActivityWatch",
            "Window",
            "Code",
            "WorkDailyReport - README.md",
            null,
            isCoding: true);
        second.AssignProject("WorkDailyReport");

        var later = new NormalizedEvent(
            dayStart.AddMinutes(80),
            dayStart.AddMinutes(100),
            "ActivityWatch",
            "Window",
            "Code",
            "AltProject - notes",
            null,
            isCoding: true);
        later.AssignProject("AltProject");

        var commit = new CommitEvent(
            RepoName: "WorkDailyReport",
            RepoPath: "D:\\gitProjects\\WorkDailyReport",
            Hash: "abc999",
            Author: "Dev",
            Timestamp: dayStart.AddMinutes(60),
            Message: "feat: implement daily summary");

        var associations = CommitAssociator.Associate(
            new[] { commit },
            new[] { first, second, later },
            windowMinutes: 15);

        Assert.Single(associations);
        var assoc = associations[0];
        Assert.Equal(2, assoc.EditorEvents.Count);
        Assert.Contains(first, assoc.EditorEvents);
        Assert.Contains(second, assoc.EditorEvents);
        Assert.DoesNotContain(later, assoc.EditorEvents);
        Assert.Single(first.LinkedCommits);
        Assert.Single(second.LinkedCommits);
        Assert.Empty(later.LinkedCommits);
    }

    [Fact]
    public void Associate_FallbacksToCodingWindowWhenNoMatches()
    {
        var start = DateTimeOffset.Parse("2025-12-29T09:00:00+01:00");
        var coding = new NormalizedEvent(
            start.AddMinutes(10),
            start.AddMinutes(20),
            "ActivityWatch",
            "Window",
            "Code",
            "OtherProject - Program.cs",
            null,
            isCoding: true);

        var commit = new CommitEvent(
            RepoName: "WorkDailyReport",
            RepoPath: "D:\\gitProjects\\WorkDailyReport",
            Hash: "zzz",
            Author: "Dev",
            Timestamp: start.AddMinutes(12),
            Message: "fix: fallback");

        var associations = CommitAssociator.Associate(
            new[] { commit },
            new[] { coding },
            windowMinutes: 15);

        Assert.Single(associations);
        var assoc = associations[0];
        Assert.Single(assoc.EditorEvents);
        Assert.Contains(coding, assoc.EditorEvents);
        Assert.Single(coding.LinkedCommits);
    }
}
