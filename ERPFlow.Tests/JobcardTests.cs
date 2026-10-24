using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using Xunit;

namespace ERPFlow.Tests;

public sealed class JobcardTests
{
    [Fact]
    public void AddPartIsAllowedWhileWorkIsInProgressAndAdvancesRevision()
    {
        var jobcard = CreateJobcard();
        var firstProduct = CreateProduct("FIRST");
        var secondProduct = CreateProduct("SECOND");

        jobcard.AddPart(firstProduct, 1);
        var revisionAfterFirstPart = jobcard.Revision;
        jobcard.StartWork();
        jobcard.AddPart(secondProduct, 2);

        Assert.Equal(JobcardStatus.InProgress, jobcard.Status);
        Assert.Equal(2, jobcard.Items.Count);
        Assert.Equal(revisionAfterFirstPart + 2, jobcard.Revision);
    }

    [Fact]
    public void StartWorkRejectsRepeatedAndTerminalTransitions()
    {
        var jobcard = CreateJobcard();

        jobcard.StartWork();

        Assert.Throws<InvalidOperationException>(jobcard.StartWork);
        jobcard.Complete("Finished", 10);
        Assert.Throws<InvalidOperationException>(jobcard.StartWork);
    }

    [Fact]
    public void CompletedJobcardRejectsPartsAndRepeatedCompletionWithoutOverwritingResult()
    {
        var jobcard = CreateJobcard();
        var product = CreateProduct("PART");
        jobcard.Complete("Original notes", 12);
        var completedAt = jobcard.CompletedAt;
        var revision = jobcard.Revision;

        Assert.Throws<InvalidOperationException>(() => jobcard.AddPart(product, 1));
        Assert.Throws<InvalidOperationException>(() => jobcard.Complete("Replacement notes", 20));

        Assert.Equal("Original notes", jobcard.TechnicianNotes);
        Assert.Equal(12, jobcard.FinalMeterReading);
        Assert.Equal(completedAt, jobcard.CompletedAt);
        Assert.Equal(revision, jobcard.Revision);
    }

    [Fact]
    public void CompletionRejectsNegativeMeterReadingBeforeMutation()
    {
        var jobcard = CreateJobcard();

        Assert.Throws<ArgumentOutOfRangeException>(() => jobcard.Complete("Invalid", -1));

        Assert.Equal(JobcardStatus.Open, jobcard.Status);
        Assert.Null(jobcard.CompletedAt);
        Assert.Equal(0, jobcard.Revision);
    }

    private static Jobcard CreateJobcard() =>
        new("SRV-TEST", Guid.NewGuid(), null, "Technician", "Reported fault");

    private static Product CreateProduct(string sku) => new("Part", sku, 10m, 10);
}
