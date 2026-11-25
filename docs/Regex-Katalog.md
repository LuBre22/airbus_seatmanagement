# G2 – Regex-Katalog

Alle produktiven Muster sind verankert, verwenden Kulturinvarianz und sind für diese kurzen Kennungen nicht-backtracking. In der vollständigen Parserabnahme müssen zusätzlich `Regex.MatchTimeout`-Grenzfälle dokumentiert werden.

| Zweck | Muster | Begründung | Gültige Beispiele | Ungültige Beispiele |
|---|---|---|---|---|
| PNR | `\A[A-HJ-NP-Z2-9]{6}\z` | Nur erlaubte ASCII-Buchstaben ohne I/O und Ziffern 2–9; `\A`/`\z` verhindern Zeilenende-Sonderfälle. | `K7M4QZ`, `R2D9TX`, `B5N8WH` | `Q0X7LM`, `AB12I3`, `ABC` |
| Ticketnummer | `\A[0-9]{3}-[0-9]{10}\z` | ASCII-Ziffern; genau Airline-Code, Bindestrich und zehn Ziffern. | `350-1234567890`, `001-0000000000`, `999-9876543210` | `350-123456789`, `350-12345678901`, `٣٥٠-1234567890` |
| Sitzsyntax | `\A(?:[1-9][0-9]?)[ABCDEGHJK]\z` | Keine führende Null, Reihe 1–99 (Layout-Existenz separat prüfen), Großbuchstaben aus dem Sitzalphabet. | `1A`, `12K`, `99J` | `01A`, `100A`, `31F` |
| Flugnummer | `\A[A-Za-z]{2}[ -]?[0-9]{1,4}\z` | Erlaubt optionale Trennung und 1–4 ASCII-Ziffern; Zahlenbereich 1–9999 separat prüfen. | `SX 0350`, `sx350`, `SX-0350` | `S0350`, `SX0`, `SX10000` |

Warum `\z` statt `$`: `$` kann auch vor einem abschließenden Zeilenumbruch matchen, dadurch kann unerwünschter Text passieren. Warum `[0-9]` statt `\d`: `\d` kann in .NET Unicode-Dezimalziffern einschließen, etwa arabisch-indische Ziffern; die Fachregel verlangt ASCII. Catastrophic Backtracking wird durch lineare Muster ohne verschachtelte, mehrdeutige Quantifizierer vermieden.
