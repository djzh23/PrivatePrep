# PrivatePrep

> Status: geschlossene Beta. Live-URL nicht öffentlich verlinkt. Für Zugang:
> zn.connec.team@gmail.com

Deutsche KI-gestützte Bewerbungsanalyse: Lebenslauf plus Stellenausschreibung
ergeben einen strukturierten Match-Report mit konkreten Bullet-Point-Vorschlägen.

[![Build, Test & Deploy](https://github.com/djzh23/PrivatePrep/actions/workflows/deploy.yml/badge.svg)](https://github.com/djzh23/PrivatePrep/actions/workflows/deploy.yml)

## Über das Projekt

PrivatePrep unterstützt die strukturierte Bewerbungsvorbereitung für den
deutschen Arbeitsmarkt. Nutzer legen ein Kurzprofil und eine persönliche Story
an, laden einen Lebenslauf hoch und fügen eine konkrete Stellenausschreibung
ein. Daraus entsteht:

- Ein Match-Score von 1.0 bis 5.0 mit vier Bewertungsdimensionen (CV-Match,
  Rollen-Passung, Culture-Screen, Red Flags)
- Eine Skill-Gap-Analyse: welche geforderten Skills sind im CV vorhanden,
  welche sind indirekt belegt, welche fehlen
- Konkrete Formulierungsvorschläge für bestehende CV-Punkte, passend zur
  Ausschreibung, ohne erfundene Angaben

Zwei zusätzliche Prüfschritte laufen um den KI-Aufruf herum: eine
Skill-Klassifizierung vor dem Aufruf und eine Fakten-Prüfung danach, die den
Report zurückhält, sobald der generierte Text Skills oder Zahlen enthält, die
nicht im CV oder in der Story stehen.

## Tech Stack

### Backend
- ASP.NET Core 9 (C# 13)
- PostgreSQL via Supabase, Entity Framework Core
- Clerk für Authentication
- Stripe für Payments
- Groq für die KI-Analyse
- Deployment: Render

### Frontend
- React, TypeScript, Vite, TailwindCSS
- Deployment: Vercel
- Separates Repository: [SmartAssist-react](https://github.com/djzh23/SmartAssist-react)

### Tooling
- xUnit für Tests
- GitHub Actions für CI

## Live-Zugang

Aktuell nur für eingeladene Beta-Tester über [betweenatna.com](https://betweenatna.com).
Anfragen für Beta-Zugang: zn.connec.team@gmail.com

## Lokale Entwicklung

Voraussetzung: .NET 9 SDK sowie Zugriff auf Groq und eine Postgres-Instanz.
Secrets werden lokal über .NET User Secrets verwaltet, nicht in Dateien abgelegt.

```bash
git clone https://github.com/djzh23/PrivatePrep.git
cd PrivatePrep
dotnet run --project PrivatePrep
```

Für das Frontend siehe das separate Repo
[SmartAssist-react](https://github.com/djzh23/SmartAssist-react).

## Tests

```bash
dotnet test
```

## Deploy-Prozess

Vor jedem Deploy wird die Release-Checkliste durchgegangen. Siehe
[docs/deployment/RELEASE-CHECKLIST.md](docs/deployment/RELEASE-CHECKLIST.md).
Branch-Mapping: [docs/BRANCHES.md](docs/BRANCHES.md).

## Attribution

Die Scoring- und Prompt-Struktur ist inspiriert von
[career-ops](https://github.com/santifer/career-ops) (MIT License, Copyright
Santiago Fernández de Valderrama). Details siehe [NOTICE.md](NOTICE.md).

## License

Proprietäre Software. Alle Rechte vorbehalten. Kontakt für Beta-Zugang:
zn.connec.team@gmail.com.
