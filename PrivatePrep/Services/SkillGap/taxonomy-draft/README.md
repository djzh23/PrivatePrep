# Skill-Listen pro Berufsfeld

Deutsche Quelle für `SkillTaxonomyCatalog`. Beim Start werden alle `*.json`
in diesem Ordner geladen und gleiche `name`-Eintraege zusammengefuehrt.
`skill-taxonomy.en.json` ergaenzt englische Aliase.

- `name`: mit korrekten Umlauten, so wie in deutschen Stellenanzeigen
- `aliases`: andere Schreibweisen, Abkuerzungen und ae/oe/ue-Varianten
- `job_titles`: Rollen und Abschluesse (MFA, Außendienst). Nicht in `skills`.
- `skills`: harte Faehigkeiten, Software, Methoden

`implies` und `coveredBy` bleiben in den Feld-Dateien weg, bis alle Felder
sie einheitlich nutzen. Die englische Datei darf sie fuer IT-Ueberlappungen
weiter tragen.

Jeder Skill hat ein Heimatfeld. Beim Laden gleiche `name`-Eintraege
zusammenfuehren, nicht doppelt zaehlen.

Heimatfelder (nicht in anderen Dateien wiederholen):

- Excel, Microsoft Office, Reisekostenabrechnung: `office.json`
- Power BI, HTML, CSS, Scrum, Kanban: `it.json`
- SPS, Siemens TIA, Gabelstapler, Arbeitssicherheit, Schichtarbeit: `trades.json`
- Technische Zeichnung, Qualitaetsmanagement, Stueckliste: `engineering.json`
- Adobe Photoshop, Adobe InDesign, Figma, Canva, Corporate Design: `design.json`
- Arbeitsrecht: `legal.json`
- Lohnbuchhaltung / DATEV Lohn: `finance.json`
- AEVO: `education.json`
- HubSpot: gleicher Name in `sales.json` und `marketing.json` (zusammenfuehren)

Software und Methode getrennt halten (ANSYS nicht als Alias von FEM, EPLAN
nicht als Alias von Elektrokonstruktion).

Keine echten Lebenslaeufe hier ablegen. Nur Woerter, die Anzeigen und CVs teilen.

Gewicht (`weight`) kommt erst spaeter, nicht in diesen Dateien.
