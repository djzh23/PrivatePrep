using PrivatePrep.Services.SkillGap;

namespace PrivatePrep.Tests.Services;

public class SkillGapServiceTests
{
    private readonly SkillGapService _sut = new();

    [Fact]
    public void Classify_CvHasCsharpAndDotnet_JdWantsAspNetCore_ExistingContainsStack()
    {
        var cv = """
            Kenntnisse
            C#, .NET

            Berufserfahrung
            Backend-Entwickler in einem Produktteam.
            """;
        var jd = PadJob("""
            Anforderungen
            C# und ASP.NET Core sind Voraussetzung für diese Rolle im Backend-Team.
            """);

        var report = _sut.Classify(cv, jd);

        Assert.Equal(SkillGapReasonCodes.Ok, report.ReasonCode);
        Assert.Contains("C#", report.Existing);
        Assert.Contains(".NET", report.Existing);
        Assert.Contains("ASP.NET Core", report.Existing);
        Assert.Empty(report.Gap);
    }

    [Fact]
    public void Classify_CvHasCsharpOnly_JdWantsDotnet_DotnetIsCoveredNotGap()
    {
        var cv = """
            Kenntnisse
            C#

            Berufserfahrung
            Backend-Entwickler in einem Produktteam.
            """;
        var jd = PadJob("""
            Anforderungen
            .NET-Kenntnisse sind für diese Rolle im Backend-Team erforderlich.
            """);

        var report = _sut.Classify(cv, jd);

        Assert.Equal(SkillGapReasonCodes.Ok, report.ReasonCode);
        Assert.Contains(".NET", report.Existing);
        Assert.DoesNotContain(".NET", report.Gap);
    }

    [Fact]
    public void Classify_CvWithoutDocker_JdRequiresDocker_ReportsGap()
    {
        var cv = """
            Kenntnisse
            C#, .NET

            Berufserfahrung
            Backend-Entwickler ohne Container-Betrieb.
            """;
        var jd = PadJob("""
            Anforderungen
            Docker ist für den Betrieb der Services zwingend erforderlich.
            """);

        var report = _sut.Classify(cv, jd);

        Assert.Equal(SkillGapReasonCodes.Ok, report.ReasonCode);
        Assert.Contains("Docker", report.Gap);
        Assert.DoesNotContain("Docker", report.Existing);
        Assert.DoesNotContain("Docker", report.SupportedByResume);
    }

    [Fact]
    public void Classify_DockerOnlyInProse_ReportsSupportedByResume()
    {
        var cv = """
            Kenntnisse
            C#, .NET

            Berufserfahrung
            Verwendete Docker in Projekt X für lokale Builds.
            """;
        var jd = PadJob("""
            Anforderungen
            Docker-Kenntnisse werden für den Betrieb erwartet.
            """);

        var report = _sut.Classify(cv, jd);

        Assert.Equal(SkillGapReasonCodes.Ok, report.ReasonCode);
        Assert.Contains("Docker", report.SupportedByResume);
        Assert.DoesNotContain("Docker", report.Existing);
        Assert.DoesNotContain("Docker", report.Gap);
    }

    [Fact]
    public void Classify_ShortJobDescription_ReturnsEmptyJd()
    {
        var report = _sut.Classify("Kenntnisse\nC#", "Anforderungen: C#");

        Assert.Equal(SkillGapReasonCodes.EmptyJd, report.ReasonCode);
        Assert.Empty(report.ExtractedJdSkills);
        Assert.Empty(report.Existing);
        Assert.Empty(report.Gap);
    }

    [Fact]
    public void Classify_JobWithoutRequirementsSection_ReturnsNoRequirementsSection()
    {
        var jd = """
            Wir sind ein Softwarehaus in Berlin und suchen Verstärkung für unser Produktteam.
            Die Rolle ist hybrid, das Büro liegt zentral, und die Zusammenarbeit ist kollegial.
            Du arbeitest an Kundenprojekten mit modernen Web-Technologien im Alltag.
            """;

        var report = _sut.Classify("Kenntnisse\nReact", jd);

        Assert.Equal(SkillGapReasonCodes.NoRequirementsSection, report.ReasonCode);
        Assert.Empty(report.ExtractedJdSkills);
    }

    [Fact]
    public void Classify_GermanSieBringenMit_ExtractsReact()
    {
        var cv = """
            Kenntnisse
            React, TypeScript

            Berufserfahrung
            Frontend-Entwicklung.
            """;
        var jd = PadJob("""
            Sie bringen mit: React
            Außerdem erwarten wir Teamfähigkeit und Eigeninitiative im Projektalltag.
            """);

        var report = _sut.Classify(cv, jd);

        Assert.Equal(SkillGapReasonCodes.Ok, report.ReasonCode);
        Assert.Contains("React", report.ExtractedJdSkills);
        Assert.Contains("React", report.Existing);
    }

    [Fact]
    public void Classify_Matching_IsCaseInsensitive()
    {
        var cv = """
            Kenntnisse
            DOCKER, kubernetes

            Berufserfahrung
            Plattform-Team.
            """;
        var jd = PadJob("""
            Requirements
            We need docker and Kubernetes experience for this platform role.
            """);

        var report = _sut.Classify(cv, jd);

        Assert.Equal(SkillGapReasonCodes.Ok, report.ReasonCode);
        Assert.Contains("Docker", report.Existing);
        Assert.Contains("Kubernetes", report.Existing);
    }

    [Fact]
    public void Classify_KubernetesOnlyInEmployerClusterProse_IsSupportedNotGap()
    {
        var cv = """
            Kenntnisse
            Linux, Git

            Berufserfahrung
            Deploy in einem Kubernetes-Cluster meines Arbeitgebers, ohne die Plattform selbst zu bauen.
            """;
        var jd = PadJob("""
            Anforderungen
            Erfahrung mit Kubernetes ist für den Betrieb der Plattform erforderlich.
            """);

        var report = _sut.Classify(cv, jd);

        Assert.Equal(SkillGapReasonCodes.Ok, report.ReasonCode);
        Assert.Contains("Kubernetes", report.SupportedByResume);
        Assert.DoesNotContain("Kubernetes", report.Gap);
        Assert.DoesNotContain("Kubernetes", report.Existing);
    }

    private static string PadJob(string body)
    {
        var filler = " Die Stelle ist unbefristet, das Team sitzt in Deutschland, Remote-Anteil ist möglich.";
        var text = body.Trim();
        while (text.Length < SkillGapService.MinimumJobDescriptionLength)
            text += filler;
        return text;
    }
}
