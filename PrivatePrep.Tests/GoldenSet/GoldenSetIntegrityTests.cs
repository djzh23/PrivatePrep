using PrivatePrep.Services.Agent;

namespace PrivatePrep.Tests.GoldenSet;

/// <summary>
/// Guards the golden set itself: a set that is too small, skewed or sloppy would make the
/// 95 % must-have gate (AC-1) meaningless.
/// </summary>
public class GoldenSetIntegrityTests
{
    private static readonly IReadOnlyList<GoldenCase> Cases = GoldenSetLoader.LoadAll();

    private static IEnumerable<GoldenCase> Postings => Cases.Where(c => c.IsJobPosting);

    [Fact]
    public void EveryFieldHasAtLeastTwoJobPostings()
    {
        foreach (var field in GoldenVocabulary.Fields)
        {
            var count = Postings.Count(c => c.Field == field);
            Assert.True(count >= 2, $"Field '{field}' has {count} postings, need at least 2.");
        }
    }

    [Fact]
    public void CaseIdsAreUnique()
    {
        var duplicates = Cases.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void CasesUseOnlyKnownVocabulary()
    {
        foreach (var c in Cases)
        {
            Assert.Contains(c.Field, GoldenVocabulary.Fields);
            Assert.Contains(c.Language, GoldenVocabulary.Languages);
            Assert.Contains(c.Origin, GoldenVocabulary.Origins);
            foreach (var m in c.Expected.MustHaves)
            {
                Assert.Contains(m.Kind, GoldenVocabulary.Kinds);
                Assert.Contains(m.Status, GoldenVocabulary.Statuses);
            }

            foreach (var s in c.Expected.Signals)
                Assert.Contains(s, GoldenVocabulary.Signals);

            if (c.Expected.Tariff is { } tariff)
                Assert.Contains(tariff.Agreement, GoldenVocabulary.Agreements);
        }
    }

    [Fact]
    public void EveryExpectedQuoteAppearsVerbatimInThePosting()
    {
        foreach (var c in Postings)
        {
            foreach (var m in c.Expected.MustHaves)
                Assert.True(
                    c.JobDescription.Contains(m.Quote, StringComparison.Ordinal),
                    $"[{c.Id}] quote not found verbatim in the posting: \"{m.Quote}\"");
        }
    }

    [Fact]
    public void ExpectedSalaryAppearsVerbatimInThePosting()
    {
        foreach (var c in Postings.Where(c => c.Expected.SalaryVerbatim is not null))
            Assert.True(
                c.JobDescription.Contains(c.Expected.SalaryVerbatim!, StringComparison.Ordinal),
                $"[{c.Id}] salary text not found verbatim in the posting.");
    }

    [Fact]
    public void PostingsMeetTheMinimumLengthAndHaveACv()
    {
        foreach (var c in Postings)
        {
            Assert.True(
                c.JobDescription.Length >= AnalyzeService.MinimumJobDescriptionLength,
                $"[{c.Id}] posting shorter than the analysis minimum.");
            Assert.False(string.IsNullOrWhiteSpace(c.CvText), $"[{c.Id}] CV text is empty.");
        }
    }

    [Fact]
    public void NonPostingsExistAndExpectNothing()
    {
        var nonPostings = Cases.Where(c => !c.IsJobPosting).ToList();

        Assert.True(nonPostings.Count >= 2, "Need at least two inputs that are not job postings.");
        foreach (var c in nonPostings)
        {
            Assert.Empty(c.Expected.MustHaves);
            Assert.Empty(c.Expected.Signals);
            Assert.Null(c.Expected.SalaryVerbatim);
            Assert.Null(c.Expected.Tariff);
        }
    }

    [Fact]
    public void BothLanguagesAreRepresentedInPostings()
    {
        Assert.True(Postings.Count(c => c.Language == "en") >= 2, "Need at least two English postings.");
        Assert.True(Postings.Count(c => c.Language == "de") >= 8, "Need at least eight German postings.");
    }

    [Fact]
    public void ExpectationsExerciseEveryKindStatusSignalAndTariffAgreement()
    {
        var mustHaves = Postings.SelectMany(c => c.Expected.MustHaves).ToList();

        Assert.Equal(GoldenVocabulary.Kinds.Order(), mustHaves.Select(m => m.Kind).Distinct().Order());
        Assert.Equal(GoldenVocabulary.Statuses.Order(), mustHaves.Select(m => m.Status).Distinct().Order());
        Assert.Equal(GoldenVocabulary.Signals.Order(), Postings.SelectMany(c => c.Expected.Signals).Distinct().Order());
        Assert.Equal(
            GoldenVocabulary.Agreements.Order(),
            Postings.Where(c => c.Expected.Tariff is not null).Select(c => c.Expected.Tariff!.Agreement).Distinct().Order());
    }

    [Fact]
    public void SomePostingsAdvertiseASalaryAndSomeDoNot()
    {
        Assert.Contains(Postings, c => c.Expected.SalaryVerbatim is not null);
        Assert.Contains(Postings, c => c.Expected.SalaryVerbatim is null);
    }
}
