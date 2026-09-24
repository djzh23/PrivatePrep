using PrivatePrep.Services.SkillGap;

namespace PrivatePrep.Tests.Services;

public class SkillTaxonomyCatalogTests
{
    private readonly SkillTaxonomyCatalog _catalog = SkillTaxonomyCatalog.LoadEmbedded();

    [Fact]
    public void LoadEmbedded_HasOneEntryPerCanonicalName()
    {
        var duplicates = _catalog.Skills
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.Empty(duplicates);
        Assert.True(_catalog.Skills.Count >= 200);
    }

    [Theory]
    [InlineData("Excel")]
    [InlineData("Power BI")]
    [InlineData("Reisekostenabrechnung")]
    [InlineData("HubSpot")]
    [InlineData("DATEV")]
    [InlineData("Jira")]
    public void LoadEmbedded_HomeFieldSkills_AreSingleEntries(string name)
    {
        Assert.Single(_catalog.Skills, s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Außendienst")]
    [InlineData("MFA")]
    [InlineData("Schreiner")]
    [InlineData("Maler und Lackierer")]
    public void LoadEmbedded_JobTitles_AreNotSkills(string name)
    {
        Assert.False(_catalog.TryGet(name, out _));
    }

    [Theory]
    [InlineData("Buchhaltung", "fibu")]
    [InlineData("Umsatzsteuer", "mwst")]
    [InlineData("Debitorenbuchhaltung", "debi")]
    [InlineData("WIG-Schweißen", "wig")]
    [InlineData("SHK", "sanitaer heizung klima")]
    [InlineData("Wundversorgung", "wundmanagement")]
    [InlineData("Qualitätsmanagement", "iso 9001")]
    [InlineData("Erprobung", "versuchswesen")]
    [InlineData("Bauausschreibung", "leistungsverzeichnis")]
    public void LoadEmbedded_AliasMapsToCanonicalName(string name, string alias)
    {
        Assert.True(_catalog.TryGet(name, out var skill));
        Assert.Contains(alias, skill.MatchTokens, StringComparer.OrdinalIgnoreCase);
    }
}
