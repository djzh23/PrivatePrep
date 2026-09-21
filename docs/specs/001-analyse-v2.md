# Spec 001: Analyse v2 (Bericht)

**Status:** In Review (2026-09-21, zurückgesetzt: AC-1-Ansatz für Muss-Kriterien offen, siehe Open Questions)
**Date:** 2026-09-21

> Entstanden in acht Fragerunden. Alle Antworten des Entwicklers sind eingearbeitet. Abschnitte mit **(Entwurf)** sind Vorschläge von Claude auf Basis dieser Antworten und im Review zu korrigieren.

## Problem

**Ist-Zustand:** ein LLM-Call mit 4 Score-Dimensionen, Rollenzusammenfassung, Warnungen und höchstens 5 Bullet-Umformulierungen. Regelbasierte Gates: `SkillGapService`, `FactGateService`, PII-Scrubber.

**Erfahrung des Entwicklers:** Der Umfang der career-ops-Blöcke A–H hat **geringe Ergebnisse** geliefert. Er würde für ein solches Ergebnis selbst nicht bezahlen. Vier bestätigte Mängel:
1. Nichts Neues über die Anzeige selbst.
2. Zu allgemein, nicht auf das Berufsfeld zugeschnitten.
3. Nichts direkt Umsetzbares.
4. Unklare Struktur oder zu viel Text.

**Zielnutzer:** Einsteiger/Umsteiger **und** erfahrene Fachkräfte, gleich gewichtet. Knappe Kernaussage oben, Erklärungen aufklappbar.

**Hauptnutzen:**
1. Die Anzeige wirklich verstehen (Klartext zu Anforderungen, versteckten Erwartungen, Warnsignalen).
2. Gehalt und Verhandlung einordnen (angegebene Zahl wörtlich, 13. Gehalt, Tarifvertrag).

Nicht als Hauptnutzen gewählt, aber im Umfang: Score mit Empfehlung, Bullets, Profiltext.

### Berichtsanforderungen (aus den vier Mängeln)
- **R1 Erkenntnis:** Jede Aussage über die Anzeige trägt einen **Textbeleg** (Zitat aus der Anzeige). Der Bericht sagt, was hinter Formulierungen steckt und was die Anzeige verschweigt, nicht nur was sie wiederholt.
- **R2 Feldbezug:** Fachbegriffe und formale Anforderungen des Berufsfelds (z.B. Examen, Anerkennung, Führerschein, Tarif).
- **R3 Umsetzbar:** Konkrete Änderungen mit **CV-Bezug** und nächste Schritte statt allgemeiner Ratschläge.
- **R4 Klar trotz Tiefe:** **Kernaussage-Karte oben** (5 bis 6 Zeilen: Score, Band-Label, wichtigste Erkenntnisse, nächster Schritt), darunter **nummerierte, aufklappbare Abschnitte mit hartem Längenbudget**. Das Budget wird im Backend durchgesetzt, nicht nur im Prompt erbeten.

## Scope

### In
- Formalerer, ausführlicherer, klar auf den Punkt kommender Analysebericht. "Formaler" = **Ton und Struktur**: neutrale, sachliche Sprache **ohne Anrede** ("Die Anzeige verlangt ..."; kein "du", kein "Sie") und feste, nummerierte Abschnitte. Bricht bewusst mit dem "du" der Landing Page.
- Feldübergreifend korrekt: Pflege, Verwaltung, Vertrieb, Handwerk, Bildung, IT. **Alle sechs gleich gewichtet.**
- Eigene Prompts. career-ops (MIT) ist Lernquelle, kein 1:1-Kopieren des Wortlauts.
- Erzeugte Texte: **Bullet-Umformulierungen** und **Profiltext** (Kurzprofil für den CV), beide faktengeprüft.
- **Score** prominent oben mit **Band-Label** (Schwellen wie im Frontend `scoreBadge`: unter 2,5 "Schwache Passung", unter 3,5 "Eingeschränkte Passung", unter 4,5 "Brauchbare Passung", sonst "Gute Passung"). Der Score ist zugleich der Free-Teaser.
- **Gehaltsdaten:** Zahl aus der Anzeige **wörtlich** (oder `null`, nie geschätzt) plus **Tariftabellen TVöD und TV-L** mit Entgeltgruppen als feste App-Daten mit **"Stand"-Datum**, plus Hinweise zu 13. Gehalt, Bonus, Tarifbindung. Für andere Tarife nur ein Hinweis bei erkannter Bindung. Ohne Quelle ehrlich "keine Marktdaten". **Keine KI-Marktschätzung, keine Websuche.**
- **Zweite Achse "Seriosität der Anzeige"**, nur aus dem Anzeigentext: **(1)** widersprüchliche oder unrealistische Anforderungen, **(2)** Warnsprache ("wir sind eine Familie", "hohe Belastbarkeit") als Einordnung, nie als Vorwurf, **(3)** Scheinselbstständigkeit und reine Provision, **(4)** fehlende Angaben (Gehalt, Firma/Adresse, Ansprechpartner) als Hinweis, nie als Vorwurf. Dazu: Anweisungen an KI-Systeme in der Anzeige werden gemeldet. Nicht geprüfte Punkte: `not_evaluated`, nie "grün". Die Seriosität ändert den Fit-Score nicht.
- **Sprache:** Anzeige und CV Deutsch **oder** Englisch, Bericht immer Deutsch.
- **Premium unbegrenzt, Free ein Versuch pro Tag**, mit Missbrauchsschutz.
- **Modellwechsel per Konfiguration** (Anbieter und Modell).
- Backend zuerst. Cursor arbeitet parallel am Frontend-Design.

### Out
- **Interview-Plan (Block F):** eigenes späteres Feature.
- **Anschreiben-Entwurf** und **Antworten auf Formularfragen (Block H)**.
- KI-Marktschätzung für Gehälter, Websuche.
- Tarif-spezifisches Modell (Free gegen Premium) in dieser Version.
- Netzwerk-/Geräte-Limit und Telefonbestätigung.
- Auto-Submit von Bewerbungen (nie).

## Domain Model

Grundsatz: **Nichts wird gespeichert.** Der Bericht lebt nur im Browser. Der Server hält weder CV noch Bericht. Ausnahme bleibt die bestehende CV-Kennung (Hash) und Zähler.

**Bericht v2 (Entwurf).** Jeder Abschnitt hat `id`, `status` (`ok`, `not_evaluated`, `locked`), Längenbudget:

| Nr. | Abschnitt | Inhalt | Free | Premium |
|---|---|---|---|---|
| 1 | Kernaussage | Score, Band-Label, bis zu 3 wichtigste Erkenntnisse mit Zitat, nächster Schritt | ja | ja |
| 2 | Rollenprofil | Berufsfeld, Seniorität, Arbeitszeit, Ort/Remote, Team | ja | ja |
| 3 | Muss-Kriterien | regelbasiert: Abschluss, Erfahrung, Führerschein, Schicht, Sprache, Zertifikat, je mit Zitat und Status (erfüllt, nicht erfüllt, unklar) | ja | ja |
| 4 | Skill-Abgleich | vorhanden, durch Lebenslauf gestützt, Lücke (bestehender `SkillGapService`) | ja | ja |
| 5 | Klartext zur Anzeige | Aussage, Zitat, Erklärung | 3 stärkste | alle |
| 6 | Seriosität der Anzeige | Signale (1)–(4), KI-Anweisungen, Status je Signal | nein | ja |
| 7 | Gehalt und Verhandlung | wörtliche Zahl oder `null`, Hinweise mit Zitat, Tarif und Entgeltgruppe mit "Stand", Verhandlungshinweise nur aus vorliegenden Daten | nein | ja |
| 8 | CV-Anpassungsplan | Abschnitt, aktueller CV-Wortlaut (Zitat), Vorschlag, Grund | nein | ja |
| 9 | Bullet-Umformulierungen | bestehend | nein | ja |
| 10 | Profiltext | Kurzprofil für den CV, faktengeprüft | nein | ja |
| 11 | Hinweise | Culture-Cap, Kürzung der Anzeige, Faktenprüfung | ja | ja |

Weitere Felder: `schemaVersion: 2`, `tier`, `truncated` (Anzeige gekürzt), erkannte Sprache. **Längenbudgets (Entwurf):** Kernaussage 6 Zeilen; Klartext-Punkt 2 Sätze plus Zitat bis 200 Zeichen; Abschnitt bis 900 Zeichen; Profiltext bis 600 Zeichen; Bullet bis 220 Zeichen. Überlänge wird im Backend gekürzt oder der Abschnitt wird neu angefordert.

**Freischaltung:** Free bekommt einen **serverseitig gekürzten** Bericht (die Premium-Blöcke werden gar nicht gesendet). **Upgrade = neue Analyse.** Kein Unscharf-Schalten im Client.

## API Contract

- **Neue Route parallel (Entwurf):** `POST /api/agent/analyze/v2`. Die bestehende Route `POST /api/agent/analyze` bleibt unverändert, bis das neue Frontend live ist. Kein erzwungener gleichzeitiger Deploy von Vercel und Render.
- **Anfrage:** wie bisher `jobDescription`, `cvText`, `cvContentHash`.
- **Antwort: ein Request, Streaming (SSE).** Ereignisse (Entwurf), in dieser Reihenfolge:

| Ereignis | Inhalt | Quelle |
|---|---|---|
| `precheck` | Ist Stellenanzeige, Sprache, gekürzt ja/nein | regelbasiert |
| `must_haves` | Abschnitt 3 | regelbasiert |
| `skill_gap` | Abschnitt 4 | regelbasiert |
| `score` | Score, Band-Label, Kernaussage. **Ab hier zählt der Free-Versuch.** | KI |
| `section` | ein Abschnitt (5 bis 11) mit `status` | KI |
| `done` | Liste der Abschnitte mit `not_evaluated`, `retryToken` (Entwurf: signiert, kurzlebig, ohne Serverzustand) | Server |
| `error` | `code`, `message`, `retryable` | Server |

- **Einzelabschnitt nachladen (Entwurf):** `POST /api/agent/analyze/v2/sections/{id}` mit denselben Eingaben und dem `retryToken`. Zählt nicht erneut. Ungültiger oder abgelaufener Token wird abgelehnt.
- **Fehlercodes (Entwurf):** `jd_not_a_posting` (422), `jd_too_short` (422), `profile_incomplete` (422), `free_limit_reached` (429), `cv_already_used_today` (409), `analysis_in_progress` (409), `rate_limited` (429, mit `Retry-After`), `llm_unavailable` (503).

## Authorization

- **Anmeldung erforderlich** wie bisher. Anonyme Nutzer können nicht analysieren.
- **Free:** 1 Analyse pro Tag. Sieht Abschnitte 1 bis 5 (Klartext nur die drei stärksten) und 11. Der Server sendet die übrigen nicht.
- **Premium:** **unbegrenzt** (ändert `UsageService.GetDailyLimit`, derzeit 30). **Unsichtbares Rate-Limit pro Minute und Stunde** gegen Automatisierung, mit Fair-Use-Hinweis in den Bedingungen.
- **Missbrauchsschutz gegen Mehrfach-Konten:** **E-Mail bestätigen und Wegwerf-Adressen sperren** (Clerk, im Dashboard zu prüfen) **und** **gleicher CV pro Tag nur einmal frei** (vorhandene CV-Kennung, gilt für Free).
- **Hinweis:** Es gibt keine Forwarded-Header-Konfiguration. Ein späteres Netzwerk-Limit setzt eine Prüfung der echten Nutzeradresse hinter dem Render-Proxy voraus.

## Edge Cases & Failure Modes

- **Zählung des Free-Versuchs:** zählt **erst mit dem `score`-Ereignis**. Ausfall oder Timeout davor zählt nicht. Danach zählt er auch bei Abbruch.
- **Eingabe ist keine Stellenanzeige:** **abgelehnt** mit klarer Meldung, **zählt nicht** (Vorprüfung ohne KI).
- **Zu lange Anzeige:** **gekürzt mit sichtbarem Hinweis** im Bericht (`truncated`). Aussage: spätere Anforderungen sind nicht berücksichtigt.
- **Parallele Anfragen:** die **zweite wird abgewiesen**, solange eine läuft (`analysis_in_progress`). Der Sperrzustand liegt im Speicher pro Nutzer (Entwurf, gilt für eine einzelne Serverinstanz).
- **Prompt-Injection in der Anzeige:** **ignoriert** (Anzeige ist Daten, nie Anweisung) und als Auffälligkeit in der Seriosität gemeldet.
- **Ausfall nach dem Score:** fehlende Abschnitte als `not_evaluated`, **einzeln nachladbar ohne Zusatzzählung**.
- **Gleicher CV am selben Tag über mehrere Konten (Free):** zweites Konto wird mit `cv_already_used_today` abgewiesen. Premium ist nicht betroffen.

## Non-Functional Requirements

- **Volumen:** unter 100 Analysen pro Tag in den ersten drei Monaten. Kostenloses Groq-Kontingent reicht voraussichtlich.
- **Latenz:** erste Inhalte **unter 2 Sekunden**, vollständiger Bericht **unter 30 Sekunden** (entspricht dem bestehenden Timeout).
- **Modellwechsel:** **Anbieter und Modell per Konfiguration** über eine gemeinsame Schnittstelle (viele Anbieter sprechen das OpenAI-Format). Kein Code-Umbau im Analysepfad. Ein Adapter pro neuem Anbieter mit eigenem Format.
- **Datenschutz:** CV und Bericht werden nicht gespeichert und nicht protokolliert. Nur Hash und Zähler.

## Integrations

- Groq (aktuell `openai/gpt-oss-120b`), später ein stärkeres Modell per Konfiguration.
- Clerk: E-Mail-Bestätigung und Sperre für Wegwerf-Adressen.
- TVöD- und TV-L-Tabellen als versionierte Daten im Repository.

## Acceptance Criteria

**Qualität und Korrektheit**
- **AC-1** Given ein Golden Set mit mindestens vier Anzeigen je Berufsfeld (sechs Felder), when die Analyse läuft, then werden Muss-Kriterien in mindestens **95 %** der Fälle richtig erkannt. Das Golden Set ist **synthetisch nach dem Vorbild von career-ops (`evals/golden`)**: eine JSON-Datei pro Fall, Grenzfälle bevorzugt (Stichpunkte, Fließtext, Großbuchstaben, "wünschenswert" gegen "erforderlich", Verneinungen). Der Lebenslauf gehört nur zu den Fällen, die den Abgleich prüfen. Echte anonymisierte Fälle sind optional. Änderung vom 2026-09-21 auf Wunsch des Entwicklers (vorher: "echte anonymisierte und synthetische").
- **AC-2** Given das Golden Set, when der Bericht erzeugt wird, then enthält er **null erfundene Zahlen, Skills oder Abschlüsse**. Ein Verstoß lässt den Test fehlschlagen.
- **AC-3** Given eine Aussage im Klartext-Abschnitt, when sie ausgegeben wird, then enthält sie ein **Zitat aus der Anzeige**. Aussagen ohne Beleg werden verworfen.
- **AC-4** Given ein erzeugter Text, when er geprüft wird, then enthält er **kein "du" und kein "Sie"** als Anrede.
- **AC-5** Given ein Abschnitt über seinem Längenbudget, when er ausgeliefert wird, then wird er gekürzt oder neu angefordert. Kein Abschnitt überschreitet sein Budget.
- **AC-6** Given ein Culture-Screen "fail", when der Score berechnet wird, then ist die Culture-Dimension höchstens 2,0 und der Gesamt-Score höchstens 3,5, angewendet **im Code** und mit Hinweis.

**Tarife und Gehalt**
- **AC-7** Given eine Anzeige mit Gehaltsangabe, when Abschnitt 7 erzeugt wird, then steht die Zahl **wörtlich** dort. Given keine Angabe, then ist der Wert `null` und der Abschnitt sagt ehrlich "keine Marktdaten".
- **AC-8** Given eine Anzeige mit TVöD- oder TV-L-Bezug, when die Entgeltgruppe erkannt wird, then zeigt der Bericht Tabelle und **"Stand"-Datum**. Given Daten älter als 12 Monate, then erscheint ein Hinweis.

**Seriosität**
- **AC-9** Given eine Anzeige mit einem der vier Signale, when Abschnitt 6 erzeugt wird, then steht das Signal mit Zitat und **ohne Vorwurf** dort. Given kein Basisdatum, then `not_evaluated`. Der Fit-Score bleibt unverändert.
- **AC-10** Given eine Anzeige mit Anweisungen an KI-Systeme, when analysiert wird, then bleibt der Inhalt des Berichts unbeeinflusst und der Fund wird in Abschnitt 6 gemeldet.

**Free, Premium und Zählung**
- **AC-11** Given ein Free-Nutzer, when der Bericht gestreamt wird, then enthält die Antwort **keine** Abschnitte 6 bis 10 und Abschnitt 5 nur mit drei Punkten.
- **AC-12** Given ein Free-Nutzer mit bereits gezählter Analyse heute, when er eine weitere startet, then `429 free_limit_reached`.
- **AC-13** Given ein Ausfall vor dem `score`-Ereignis, when der Fehler gemeldet wird, then bleibt der Tageszähler unverändert. Given ein Abbruch nach `score`, then ist der Versuch gezählt.
- **AC-14** Given ein Premium-Nutzer über 5 Anfragen pro Minute oder 30 pro Stunde, when er weitere sendet, then `429 rate_limited` mit `Retry-After`. Darunter keine Begrenzung.
- **AC-15** Given ein zweites Free-Konto mit demselben CV-Hash am selben Tag, when es startet, then `409 cv_already_used_today`. Premium ist nicht betroffen.
- **AC-16** Given eine laufende Analyse desselben Nutzers, when eine zweite startet, then `409 analysis_in_progress`.

**Eingaben und Fehler**
- **AC-17** Given eine Eingabe ohne Anzeigenstruktur, when sie gesendet wird, then `422 jd_not_a_posting` **ohne** Zählung und ohne KI-Aufruf.
- **AC-18** Given eine Anzeige über dem Limit, when analysiert wird, then ist `truncated` gesetzt und ein Hinweis nennt, dass spätere Anforderungen fehlen.
- **AC-19** Given ein Ausfall nach dem Score, when `done` eintrifft, then sind fehlende Abschnitte `not_evaluated`, und ein gültiger `retryToken` lädt sie **ohne Zusatzzählung** nach. Ungültiger oder abgelaufener Token wird abgelehnt.
- **AC-20** Given eine englische Anzeige und ein englischer CV, when analysiert wird, then ist der Bericht deutsch und die Muss-Kriterien werden erkannt.

**Technik**
- **AC-21** Given ein Testmodell, when Anbieter und Modell per Konfiguration gewechselt werden, then läuft die Analyse **ohne Codeänderung** im Analysepfad.
- **AC-22** Given die bestehende Route, when sie aufgerufen wird, then ist das Antwortformat **unverändert** (Vertragstest).
- **AC-23** Given ein normaler Lauf, when er endet, then sind **weder CV noch Bericht** serverseitig gespeichert oder protokolliert. Nur Hash und Zähler.
- **AC-24** Given ein Testmodell mit fester Antwortzeit, when gestreamt wird, then erscheint das erste Ereignis **unter 2 Sekunden**. Der vollständige Bericht liegt mit dem echten Modell **unter 30 Sekunden**, geprüft im Golden-Set-Lauf.

## Deferred Decisions

| Entscheidung | Gewählter Fallback | Revisit-Trigger |
|---|---|---|
| Interview-Plan | eigenes Feature, später | nach Analyse v2 |
| Rate-Limit-Werte Premium | 5 pro Minute, 30 pro Stunde pro Konto | nach den ersten echten Nutzungsdaten |
| Clerk-Dashboard-Prüfung (E-Mail bestätigen, Wegwerf-Adressen sperren) | Entwickler prüft, ob der Plan das erlaubt. Sonst nur CV-Kennung als Schutz | vor dem Launch |
| Berufsfeld-Reihenfolge | alle sechs gleich | wenn ein Feld im Golden Set deutlich schlechter abschneidet |
| Netzwerk-/Geräte-Limit | nicht in dieser Version | wenn Missbrauch in den Nutzungszahlen sichtbar wird |
| Pflege der Tariftabellen | Entwickler aktualisiert bei Tarifrunden, Bericht warnt bei Daten über 12 Monate | bei jeder Tarifrunde |
| Längenbudgets (Zahlen im Entwurf oben) | Werte wie im Domain Model | nach der ersten Lesbarkeitsprüfung des Golden Sets |
| Form von `retryToken` | signierter, kurzlebiger Token ohne Serverzustand | wenn mehrere Serverinstanzen laufen |
| Sperre für parallele Anfragen | Speicher pro Nutzer in einer Instanz | wenn mehrere Serverinstanzen laufen |
| Floskel-Test | nicht in dieser Version, Textbeleg (AC-3) wirkt indirekt | wenn das Golden Set Floskeln zeigt |
| Golden-Set-Läufe für KI-Teile | wie career-ops: **Replay** (aufgezeichnete Modellausgaben, offline, deterministisch, in der Test-Suite) und **Live** (echtes Modell, nur auf Abruf). Erwartungen aus Referenzlabels, später ersetzbar durch handgeprüfte | ab Schritt 9 (Prompts) |
| Fremde Stellenanzeigen aus dem Netz | nicht im Repository (Urheberrecht der Arbeitgeber ungeklärt). Nur als Stilvorlage für eigene Formulierungen | wenn ein Datensatz mit klarer Lizenz gefunden wird |

## Open Questions

- **Erkennung der Muss-Kriterien (AC-1):** Der Spec nimmt an, dass regelbasierte Erkennung 95 % erreicht. Gemessen auf Anzeigen, die nach den Regeln geschrieben wurden (Schritt 3): **50 %, 64 %, 68 %** in drei Blind-Runden (76 % nach einer letzten Regeländerung, nicht mehr rein blind), auf den abgestimmten Fällen jeweils 95 % bis 100 %. Die Lücke bleibt bei neuen Formulierungen. Entscheidung offen: **(A)** KI schlägt Muss-Kriterien mit wörtlichem Zitat vor, Code prüft Zitat, Art und Status (die Regeln dienen als Gegenprobe), **(B)** Regeln weiter ausbauen, **(C)** Ziel für regelbasierte Erkennung senken und KI-Ergänzung nur in Premium.

Entwurfsdetails (Berichtsfelder, Ereignisnamen, Fehlercodes, Längenbudgets) sind oben als **(Entwurf)** gekennzeichnet und werden im Review korrigiert.
