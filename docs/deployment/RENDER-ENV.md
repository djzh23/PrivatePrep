# Render Environment-Variablen nach Beta-Ende

Die App läuft ab jetzt öffentlich. Setz folgende Werte im Render Dashboard
für den Backend-Service.

Alte Werte, die entfernt oder auf false gesetzt werden:

- `BETA_MODE` (auf false, oder entfernen)
- `BetaMode__Enabled` (auf false, oder entfernen)
- `BetaMode__AllowedEmails` (kann entfernt werden)
- `BETA_ALLOWED_EMAILS` (kann entfernt werden; das liest `Program.cs` zusätzlich)

Alle anderen Environment-Variablen bleiben wie sie sind.

Nach dem Ändern: Manual Deploy triggern oder auf den nächsten Push warten.

Hinweis: `BetaMode.Enabled` in `appsettings.json` ist bereits `false`. Solange
Render `BETA_MODE=true` setzt, überschreibt das die Datei. Deshalb muss der
Wert im Dashboard auf false, sonst bleibt das Whitelist-Gate live.
