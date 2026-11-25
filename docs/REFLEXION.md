# D1/D2/E1/E2/I2 – Reflexion und offene Integrationspunkte

## D1 – Überladungen

Natürlich und unterscheidbar sind `SeatId.Create(int row, char column)`, `Create(string text)` und `Create(ReadOnlySpan<char> text)`: die Eingabeform bestimmt eindeutig die Überladung. Sitzsperren sollten als `Block(SeatId, BlockReason)`, `Block(IEnumerable<SeatId>, BlockReason)`, `Block(byte row, char first, char last, BlockReason)` und `Block(byte firstRow, byte lastRow, BlockReason)` angeboten werden; benannte Bereichsmethoden wären noch eindeutiger, falls Aufrufargumente sonst mehrdeutig wären. `Describe(Passenger)`, `Describe(Seat)` und `Describe(CabinClass)` sind durch Parametertyp überladen.

| Ausdruck | Erwartete Überladung | Begründung |
|---|---|---|
| `Format(5)` | `int` | exakter Typ |
| `Format(5L)` | `long` | exakter Typ |
| `Format((byte)5)` | `int` | beste implizite numerische Konvertierung aus `byte` |
| `Format((short)5)` | `int` | beste implizite numerische Konvertierung aus `short` |
| `Format('5')` | `int` | `char` kann implizit zu `int` konvertiert werden |
| `Format(5u)` | abhängig von vorhandenem Überladungsset; mit `int/long/double/decimal` bevorzugt `long` | `uint` passt implizit zu mehreren Zahltypen; im realen Projekt per Compile-Test bestätigen |
| `Format(5.5f)` | `double` | implizite Erweiterung `float` → `double` |
| `Format(5.5m)` | `decimal` | exakter Typ |
| `Format(null)` | `string` | spezifischere Referenztyp-Überladung gegenüber `object` |
| `Format("5")` | `string` | exakter Referenztyp |
| `Format((object)5)` | `object` | statischer Argumenttyp ist `object` |

Rückgabetypen sind keine eigenständige Überladungsgrundlage, weil sie beim Auflösen eines gewöhnlichen Methodenaufrufs nicht als Auswahlkriterium dienen. Optionale Parameter und eine vorhandene parameterlose Überladung können dieselbe Aufrufsyntax anbieten und dadurch Mehrdeutigkeit erzeugen. Wird eine optionale Signatur in einer Bibliothek ergänzt, kann bereits kompilierter Clientcode weiterhin auf die alte Signatur gebunden sein; Neukompilierung kann eine andere Überladung wählen und somit einen binären oder semantischen Breaking Change verursachen.

## D2 – `out` gegenüber Rückgabe-Tupel

`out` ist bei einem etablierten Try-Pattern wie `TryParse` sinnvoll: Erfolg/Fehlschlag ist ein `bool`, das Ergebnis wird nur bei Erfolg genutzt und Nullable-Flow lässt sich mit Attributen beschreiben. Ein Tupel ist besser, wenn mehrere gleichberechtigte Ergebnisse ohne Fehlerzustand zurückgegeben werden oder die Call-Site sprechende Namen nutzen soll. Ein `ref`-Parameter ist gerechtfertigt, wenn ein vorhandener Aggregatwert in-place fortgeschrieben werden soll; `in` passt zu großen unveränderlichen Werten.

## E1 – Komposition, Vererbung und Liskov

Komposition ist vorzuziehen, wenn Merkmale unabhängig kombinierbar sind oder eine Basisklasse viele optionale Verhaltensvarianten bekäme. `ExitRowSeat : EconomySeat` ist in dieser Übungsdomäne vertretbar, weil ein Notausgangssitz ein Sitz der Economy-Kabine bleibt und das spezialisierte Eligibility-Verhalten die zentrale abstrahierte Vertragsmethode nicht aufhebt: die Methode liefert bei Regelverletzungen `false` statt einen normalen Sitzplatz vorzutäuschen. In einem größeren Produkt wäre ein allgemeines `Seat` plus unveränderliche Attribute/Policy-Komposition flexibler.

## E2 – Gleichheit, Undo und Dispatch

`SeatId` ist wertgleich nach Reihe und Buchstabe; eine unveränderliche Identität eignet sich als Dictionary-Schlüssel. Mutable Schlüssel sind gefährlich: ändert sich ein Feld, aus dem `GetHashCode` berechnet wird, liegt der Eintrag in einem anderen Bucket, obwohl das Dictionary ihn im alten Bucket sucht. `Seat`-Gleichheit basiert entsprechend nur auf stabiler `Id`, niemals auf Status.

Die Undo-Invariante lautet: Für jede erfolgreiche Operation führt `Execute` gefolgt von `Undo` zu einem `CabinStateSnapshot`, das wertgleich dem Snapshot unmittelbar vor `Execute` ist. Auditblöcke werden dabei nicht entfernt; ein Undo ist eine neue, kompensierende Tatsache. Die vorliegende Snapshot-Basis bildet Sitzstatus, Sperren, Zuordnungsindizes und Berechtigungen ab. Upgrade-Warteschlange und vollständige Operationshistorie benötigen für die finale Abnahme noch eine erweiterte Snapshot-Grenze.

Bei `virtual/override` entscheidet der Laufzeittyp; ein `new`-Member verdeckt nur anhand des statischen Typs. Ein `sealed override` kann nicht weiter überschrieben werden. Die finale Spezifikation verlangt dafür noch ein dediziertes Dispatch-Beispiel mit Test.

## I2 – Hash-Kette und Grenzen

Änderungen an Payload oder Zeitstempel ohne Neuberechnung werden durch Hashvergleich erkannt. Löschen und Vertauschen werden durch Indexkontinuität und Vorgängerhash erkannt. Wird ein Block ersetzt und nur sein Hash neu berechnet, stimmen Folgelinks im Allgemeinen nicht mehr. Das vollständige Neuberechnen ab einem inneren Block ist ohne extern bekannten Kopfhash nicht erkennbar: Der Angreifer kann gültige Hashes und Verkettung wiederherstellen. Dasselbe gilt für einen ersetzten letzten Block, wenn nur lokale Integrität geprüft wird.

Ein organisatorischer Anker ist nötig, beispielsweise regelmäßige Veröffentlichung des Kopfhashes in einem getrennten, zugriffsgeschützten System oder ein signierter, extern archivierter Tagesabschluss. SHA-384 liefert 384 Bit (48 Byte), SHA-256 256 Bit (32 Byte); SHA-384 ist eine SHA-512-Familienvariante mit gekürzter Ausgabe. Beide sind SHA-2-Konstruktionen. Ein unkeyed Hash ist kein MAC; gegen Length-Extension-artige Fehlverwendung von `Hash(secret || message)` sollte HMAC statt eigener Verkettung verwendet werden. Die Audit-Kette schützt vor nachträglicher Manipulation ohne geheimen Schlüssel, aber nicht gegen einen Angreifer, der alle Kopien und externen Anker kontrolliert.

## Umfang der CLI in diesem Stand

`LOGIN`, `STAT`, einzelne Sitz-`BLK`, `REL`, `ASG`, `SWP`, `UPG`-Anfrage, `PROCESS UPGRADES`, `UNDO`, `REDO`, laufendes `VERIFY`, `IMPORT MANIFEST` und Testmodus-`CLOCK` sind als Basis vorhanden. Noch nicht vollständig: vollständige Bereichs-/Listen-`BLK`-Grammatik, Datei-`VERIFY`, alle Exportkommandos, vollständige Rollenprüfung für jede Parser-/Dateikante, auditierte Ablehnung jedes syntaktisch validen Befehls ohne Login sowie das PDF-Abnahmeszenario in kompletter Sequenz.
