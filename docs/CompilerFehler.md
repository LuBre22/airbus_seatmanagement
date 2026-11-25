# D2/E1 – Compilerfehler und absichtliche Negativbeispiele

Die folgenden Snippets sind **Dokumentation**, nicht Teil der kompilierbaren Projekte.

| Beispiel | Erwarteter Fehler | Ursache |
|---|---|---|
| `static bool Parse(out int value) { return true; }` | CS0177 | Ein `out`-Parameter muss auf jedem Rückgabepfad zugewiesen sein. |
| `int value; Use(ref value);` | CS0165 | Die lokale Variable ist vor der Übergabe per `ref` nicht definitiv zugewiesen. |
| `static void Change(in int value) { value = 2; }` | CS8331 | Ein `in`-Parameter ist schreibgeschützt. |
| `static ref int Get(Counter c) => ref c.Value;` bei normaler Property | CS0206 | Eine gewöhnliche Property ist keine ref-return-fähige Speicherstelle. |
| `static void Set(ref int value) { }` Aufruf `Set(ref 5)` | CS1510 | `ref`/`out` benötigt eine zuweisbare Variable, kein Literal. |
| `new Seat(...)` bei abstrakter Basisklasse | CS0144 | Abstrakte Klassen sind nicht instanziierbar. |

`in` verhindert eine Wertkopie nicht in jeder möglichen Situation: Bei einem nicht-`readonly struct` kann ein Aufruf eines nicht-readonly Members eine defensive Kopie erfordern. `readonly struct` und readonly Member vermeiden solche versteckten Kopien.
