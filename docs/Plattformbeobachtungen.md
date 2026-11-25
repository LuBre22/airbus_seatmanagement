# B3/C3/G3 – Plattformbeobachtungen

## Messstatus

Die Spezifikation verlangt konkrete Ausgaben für de-DE, en-SG, Invariant, en-US, th-TH und sv-SE. In der Erstellungssandbox ist kein .NET-SDK verfügbar, daher sind diese Ausgaben **nicht gemessen**. Es wäre irreführend, ICU-Version oder tatsächliche Unicode-Leerzeichen zu behaupten. Vor Abgabe auf der Zielplattform bitte folgende Werte mit `CultureInfo.GetCultureInfo(...)` ausgeben und hier ergänzen:

- Dezimal- und Tausendertrennzeichen von `de-DE`, `en-SG`, invariant
- Prozentzeichen und etwaige U+00A0/U+202F-Abstände
- Eurozeichen-Platzierung und Leerzeichen von `de-DE`, `en-SG`, invariant
- Kurze Wochentags- und Monatsnamen von `de-DE` sowie AM/PM-Schreibweise von `en-SG`
- Namensreihenfolge für `Ordinal`, `OrdinalIgnoreCase`, `InvariantCulture`, `de-DE`, `sv-SE`

## Schutz gegen Unterschiede

Audit-Zeit und Dateinamen benutzen explizit `InvariantCulture`, Zeitstempel basieren auf gregorianischem ISO/Jahrformat. `NumberFormatter` erhält die Kultur explizit; Tests sollten geschützte Leerzeichen gezielt normalisieren, aber Dezimaltrenner, Reihenfolge und Wertigkeit weiter prüfen. Für normierte Manifestnamen ist `StringComparer.Ordinal` die reproduzierbare Sortierregel.

## Kalenderhinweis

Ein unbedachtes Formatieren mit `CurrentCulture` kann unter `th-TH` den Buddhistischen Kalender und damit ein anderes Jahr liefern. Zeitstempel und Dateinamen müssen daher stets mit `InvariantCulture` erzeugt werden. Für die vollständige Plattformabnahme sind `CurrentCulture` und `CurrentUICulture` je Testfall auf de-DE, en-US und th-TH zu setzen und anschließend zurückzustellen.
