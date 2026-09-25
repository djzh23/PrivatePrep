# PrivatePrep

KI-gestützte Bewerbungsanalyse für den deutschen Arbeitsmarkt.

[![Build, Test & Deploy](https://github.com/djzh23/PrivatePrep/actions/workflows/deploy.yml/badge.svg)](https://github.com/djzh23/PrivatePrep/actions/workflows/deploy.yml)

> Status: die App ist deployed und technisch erreichbar, aber noch nicht
> beworben oder öffentlich angekündigt. Stripe läuft im Testmodus, es werden
> keine echten Zahlungen verarbeitet. Das ist ein Pre-Launch-Stand, den der
> Autor selbst wie ein normaler Nutzer testet, kein fertig gelauntes
> öffentliches Produkt.

## Über das Projekt

Ein Nutzer legt ein Kurzprofil an, lädt einen Lebenslauf hoch und fügt eine
Stellenausschreibung ein. Das Backend liefert daraus einen strukturierten
Match-Report:

- Einen Match-Score von 1.0 bis 5.0 über vier Dimensionen (CV-Match,
  Rollen-Passung, Culture-Screen, Red Flags)
- Eine Skill-Gap-Analyse: welche geforderten Skills sind im CV vorhanden,
  welche sind indirekt belegt, welche fehlen
- Konkrete Formulierungsvorschläge für bestehende CV-Punkte, passend zur
  Ausschreibung, ohne erfundene Angaben

Zwei deterministische Prüfschritte laufen um den KI-Aufruf herum: eine
Skill-Klassifizierung vor dem Aufruf und eine Fakten-Prüfung danach, die den
Report zurückhält, sobald der generierte Text Skills oder Zahlen enthält, die
nicht im CV oder im Profil stehen.

## Screenshots

Dieses Repo ist die Backend-API, es gibt hier keine Oberfläche. Screenshots
der Web-App siehe
[privateprep-frontend](https://github.com/djzh23/privateprep-frontend).

## Tech Stack

| Bereich | Technologie |
|---|---|
| Runtime | .NET 9, C# 13, ASP.NET Core |
| Datenbank | PostgreSQL (Supabase), Entity Framework Core |
| Auth | Clerk |
| Payments | Stripe |
| KI-Inferenz | Groq (gpt-oss-120b) |
| CV-Parsing | UglyToad.PdfPig |
| Tests | xUnit |
| CI | GitHub Actions |
| Deployment | Render |

## Repository-Struktur

```
PrivatePrep/
  Controllers/    HTTP-Endpunkte (Agent, CV, Inbox, Jobs, Onboarding, Profil, Reports, Stripe)
  Services/       Fachlogik, gruppiert nach Bereich (Agent, FactGate, Groq, Inbox,
                   Profile, Reports, SkillGap, Stripe, Tariffs, ...)
  Models/         EF-Core-Entities und Enums
  Data/           DbContext und EF-Konfiguration
  Migrations/     EF-Core-Migrationen
  Security/       Auth- und Autorisierungs-Hilfsfunktionen
  Middleware/     Middleware der Request-Pipeline
  Configuration/  Typisierte Settings
  wwwroot/        Statische Dateien, die die API ausliefert
docs/             Architektur-Notizen, Deployment-Checklisten, Branch-Policy
```

## Lokale Entwicklung

Voraussetzung: .NET 9 SDK sowie Zugriff auf einen Groq API Key und eine
Postgres-Instanz. Secrets werden lokal über .NET User Secrets verwaltet, nicht
in Dateien abgelegt.

```bash
git clone https://github.com/djzh23/PrivatePrep.git
cd PrivatePrep
dotnet run --project PrivatePrep
```

Für das Frontend siehe das separate
[privateprep-frontend](https://github.com/djzh23/privateprep-frontend) Repo.

## Tests

```bash
dotnet test
```

## Features

- Karriereprofil und CV-Upload, mit Textextraktion via PdfPig
- Aufnahme von Stellenausschreibungen, unter anderem über eine
  Browser-Erweiterung
  ([privateprep-extension](https://github.com/djzh23/privateprep-extension)),
  die Anzeigen direkt von LinkedIn, StepStone und anderen Seiten erfasst
- Match-Report-Generierung: Score, Skill-Gap, Formulierungsvorschläge
- Free- und Premium-Tarif, mit Stripe-basierter Abo-Verwaltung
- Deterministischer Skill-Gap-Vorfilter und Fakten-Prüfung nach dem
  KI-Aufruf, damit der Report an dem bleibt, was der CV tatsächlich hergibt

## Architektur-Notizen

Die Analyse-Pipeline trennt bewusst deterministische und KI-gestützte
Schritte: der Skill-Abgleich gegen feldspezifische Taxonomien läuft im Code
vor dem KI-Aufruf, eine Fakten-Prüfung nach der Generierung vergleicht den
erzeugten Text mit CV und Profil, bevor ein Report angezeigt wird. Beides
dient dazu, den KI-Schritt nachvollziehbar zu halten und erfundene Angaben
abzufangen, statt der Modellausgabe blind zu vertrauen.

Scoring-Rubrik und Prompt-Struktur sind angelehnt an
[career-ops](https://github.com/santifer/career-ops) (MIT License). Details
zur Attribution siehe [NOTICE.md](NOTICE.md).

## Deploy-Prozess

Render deployt automatisch von `main` bei jedem Push. Siehe
[docs/deployment/RELEASE-CHECKLIST.md](docs/deployment/RELEASE-CHECKLIST.md)
für die Checkliste vor dem Deploy und [docs/BRANCHES.md](docs/BRANCHES.md)
für die Branch-Policy.

## Verwandte Repos

- Frontend: [privateprep-frontend](https://github.com/djzh23/privateprep-frontend)
- Browser-Erweiterung: [privateprep-extension](https://github.com/djzh23/privateprep-extension)

## License

Proprietäre Software. Alle Rechte vorbehalten. Attribution für Drittanbieter
siehe [NOTICE.md](NOTICE.md).

## Kontakt

zn.connec.team@gmail.com
