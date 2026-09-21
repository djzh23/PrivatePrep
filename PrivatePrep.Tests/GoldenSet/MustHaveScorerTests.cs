namespace PrivatePrep.Tests.GoldenSet;

public class MustHaveScorerTests
{
    private static ExpectedMustHave Item(string kind, string quote, string status = "met") => new(kind, quote, status);

    [Fact]
    public void IdenticalLists_ScoreFullAccuracyWithoutFalsePositives()
    {
        var expected = new[] { Item("abschluss", "Abgeschlossene Ausbildung"), Item("fuehrerschein", "Führerschein Klasse B") };

        var score = MustHaveScorer.Score(expected, expected);

        Assert.Equal(2, score.Expected);
        Assert.Equal(2, score.Correct);
        Assert.Equal(0, score.FalsePositives);
        Assert.Equal(1.0, score.Accuracy);
    }

    [Fact]
    public void MissingItem_LowersAccuracy()
    {
        var expected = new[] { Item("abschluss", "Abgeschlossene Ausbildung"), Item("fuehrerschein", "Führerschein Klasse B") };
        var actual = new[] { Item("abschluss", "Abgeschlossene Ausbildung") };

        var score = MustHaveScorer.Score(expected, actual);

        Assert.Equal(1, score.Found);
        Assert.Equal(1, score.Correct);
        Assert.Equal(0.5, score.Accuracy);
    }

    [Fact]
    public void RightRequirementWithWrongStatus_IsFoundButNotCorrect()
    {
        var expected = new[] { Item("sprache", "Deutsch C1", "met") };
        var actual = new[] { Item("sprache", "Deutsch C1", "notMet") };

        var score = MustHaveScorer.Score(expected, actual);

        Assert.Equal(1, score.Found);
        Assert.Equal(0, score.Correct);
        Assert.Equal(0.0, score.Accuracy);
    }

    [Fact]
    public void ExtraItem_CountsAsFalsePositiveWithoutHurtingAccuracy()
    {
        var expected = new[] { Item("abschluss", "Abgeschlossene Ausbildung") };
        var actual = new[] { Item("abschluss", "Abgeschlossene Ausbildung"), Item("zertifikat", "Zertifikat von Vorteil") };

        var score = MustHaveScorer.Score(expected, actual);

        Assert.Equal(1, score.FalsePositives);
        Assert.Equal(1.0, score.Accuracy);
    }

    [Fact]
    public void QuoteMatchingIgnoresCaseWhitespaceAndSurroundingPunctuation()
    {
        var expected = new[] { Item("berufserfahrung", "mindestens 3 Jahre Erfahrung in der Personalsachbearbeitung") };
        var actual = new[] { Item("berufserfahrung", "  Mindestens 3 Jahre  Erfahrung in der Personalsachbearbeitung, ") };

        var score = MustHaveScorer.Score(expected, actual);

        Assert.Equal(1.0, score.Accuracy);
    }

    [Fact]
    public void ActualQuoteContainingTheExpectedQuote_StillMatches()
    {
        var expected = new[] { Item("schicht", "Bereitschaft zum Schichtdienst") };
        var actual = new[] { Item("schicht", "Bereitschaft zum Schichtdienst inklusive Wochenenden") };

        var score = MustHaveScorer.Score(expected, actual);

        Assert.Equal(1.0, score.Accuracy);
    }

    [Fact]
    public void SameKindButUnrelatedQuote_DoesNotMatch()
    {
        var expected = new[] { Item("abschluss", "Abgeschlossene Ausbildung als Elektroniker") };
        var actual = new[] { Item("abschluss", "Hochschulstudium der Informatik") };

        var score = MustHaveScorer.Score(expected, actual);

        Assert.Equal(0, score.Found);
        Assert.Equal(1, score.FalsePositives);
    }

    [Fact]
    public void DifferentKindWithSameQuote_DoesNotMatch()
    {
        var expected = new[] { Item("sprache", "Deutsch C1") };
        var actual = new[] { Item("zertifikat", "Deutsch C1") };

        var score = MustHaveScorer.Score(expected, actual);

        Assert.Equal(0, score.Found);
    }

    [Fact]
    public void OneActualItemCannotSatisfyTwoExpectedItems()
    {
        var expected = new[] { Item("sprache", "Deutsch C1"), Item("sprache", "Deutsch C1 oder besser") };
        var actual = new[] { Item("sprache", "Deutsch C1 oder besser") };

        var score = MustHaveScorer.Score(expected, actual);

        Assert.Equal(1, score.Found);
        Assert.Equal(0.5, score.Accuracy);
    }

    [Fact]
    public void NoExpectations_ScoreFullAccuracyAndCountEveryActualAsFalsePositive()
    {
        var actual = new[] { Item("abschluss", "Irgendwas") };

        var score = MustHaveScorer.Score([], actual);

        Assert.Equal(1.0, score.Accuracy);
        Assert.Equal(1, score.FalsePositives);
    }

    [Fact]
    public void Aggregate_SumsAcrossCasesBeforeComputingAccuracy()
    {
        var first = new MustHaveScore(Expected: 10, Found: 10, Correct: 10, FalsePositives: 0);
        var second = new MustHaveScore(Expected: 2, Found: 1, Correct: 0, FalsePositives: 3);

        var total = MustHaveScorer.Aggregate([first, second]);

        Assert.Equal(12, total.Expected);
        Assert.Equal(11, total.Found);
        Assert.Equal(10, total.Correct);
        Assert.Equal(3, total.FalsePositives);
        Assert.Equal(10.0 / 12.0, total.Accuracy, precision: 6);
    }
}
