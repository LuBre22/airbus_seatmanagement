# A1 – Datentypen

| Feld | C#-Typ | Begründung |
|---|---|---|
| Sitzreihe | `byte` | 1–99; ein Byte reicht und der Wert wird als Teil des kleinen unveränderlichen `SeatId` geführt. Konstruktor begrenzt auf 1–99. |
| Sitzbuchstabe | `char` | genau ein UTF-16-Codeelement; der erlaubte ASCII-Bereich wird separat validiert. |
| Gepäckmasse | `decimal?` im Importmodell bzw. `decimal` in `Passenger` | Zehntelkilogramm sollen exakt summierbar sein. Der Import-Text kann fehlen, der gültige Passagier dagegen nicht. `decimal` ist ein Werttyp. |
| Vielfliegermeilen | `long` | ganzzahlig, nicht negativ, mit Reserve bis in den Milliardenbereich; 8 Byte. |
| Upgrade-Gebühr | `decimal` | Dezimaldarstellung ist für Cent-Beträge geeignet; Rundung erfolgt ausdrücklich mit `AwayFromZero`. |
| Auslastung | `decimal` | Verhältnis mit expliziter Prozentformatierung; vermeidet unnötige binäre Rundungsartefakte im Report. |
| Passagierzahl | `int` | kleine Zählwerte, keine Null-Semantik im gültigen Report. |
| Abflugzeitpunkt | `DateTimeOffset` | Werttyp mit UTC-Offset, eindeutiger Zeitpunkt; Zeitzonenregeln stehen zusätzlich in `TimeZoneInfo`. |
| Geburtsdatum | `DateOnly` | Datum ohne Uhrzeit oder Zeitzone. |
| Check-in-Tageszeit | `TimeOnly` | Tageszeit ohne Datum, wenn sie isoliert verarbeitet wird. |
| Belegungsflag | `bool` | genau zwei Zustände, Werttyp. Im Aggregat ist der Status als typsicheres Enum modelliert. |
| PNR | `string` | sechs Zeichen mit Validierung; Referenztyp, nicht `null` in einem gültigen Passagier. |
| SHA-384-Hash | intern `byte[]` oder `string` | 48 Bytes als Hashwert; für festes Auditformat wird Kleinbuchstaben-Hex mit 96 Zeichen gespeichert. Keine öffentliche mutable Bytearray-Referenz. |
| Blockindex | `long` | fortlaufend, mit großem Wertebereich. |
| Operations-ID | `Guid` | global eindeutiger 128-Bit-Wert; wertartig und nicht vom Zählerzustand abhängig. |

## Typwahl und Genauigkeit

`float` und `double` sind für Geld ungeeignet, weil viele Dezimalbrüche in binärer Gleitkommadarstellung nicht exakt repräsentierbar sind. Wiederholtes Addieren kann dadurch Cent-Abweichungen erzeugen. `decimal` ist für Gebühren und Dezimalmassen besser geeignet; fachliche Rundungsregeln müssen dennoch explizit angewendet werden. Eine Summe von 1.000 mal 0,1 mit `double` kann abhängig von der Auswertungsreihenfolge einen Näherungswert ergeben, während `decimal` den Zehntelwert dezimal exakt darstellt.

Boxing entsteht, wenn ein Werttyp in `object` oder ein Interface dieses Wertes konvertiert wird. Dabei wird der Wert in ein Objekt auf dem Heap kopiert. In einem Hot-Path wie einer Sitzplatzsuche erhöht wiederholtes Boxing Allokationen und GC-Druck; generische Collections und typisierte Vergleicher vermeiden das meist.

`SeatId` ist ein `readonly struct`: kleine, unveränderliche Identität mit Wertsemantik. `Passenger` ist eine versiegelte Referenzklasse: ein fachlich reiches, langlebiges Manifestobjekt mit stabiler Identität über seine PNR.

Die Ordnung von `LoyaltyTier` ist über die expliziten Enumwerte garantiert. Für eine ordinale Größenprüfung wird in C# der zugrundeliegende Integerwert verglichen; Enum-Namen selbst sind keine frei definierbaren relationalen Operatoren.
