// 19 Vergleich: Rust — `r"…"` / `r#"…"#` (Raw, Zaun aus `#`), mehrzeilige Literale sind einfach
// Zeilenumbrüche im String (`\` am Zeilenende schluckt Einzug); Swift hat `"""` mit der
// Einzug-Regel der schließenden `"""` — die der Lyric-Vorschlag übernimmt. Kotlin: `"""…"""`
// + `.trimIndent()` (Einzug-Entfernung zur LAUFZEIT, was Swift/der Vorschlag zur Lexzeit tun).
fn main() {
    // (1) Raw mit Zaun: Anführungszeichen im Inhalt
    let doc: serde_json::Value =
        serde_json::from_str(r#"{"name": "aria", "level": 3, "tags": ["a", "b"]}"#).unwrap();
    println!("{}", if doc.is_object() { "json ok" } else { "json bad" });

    // (2) mehrzeilig: `\` am Zeilenende entfernt Umbruch + folgenden Einzug
    let usage = "usage: tool [options] <file>\n\
                   -v        verbose\n\
                   -o FILE   write output to FILE\n\
                   --dry     do not write anything\n\
                 examples:\n\
                   tool -v in.txt";
    println!("{usage}");

    // (3) Raw-Pfad
    let pattern = r"C:\Users\ada\*.txt";
    println!("{pattern}");
}
// Ausgabe: wie ist.lyr

// Swift (die Einzug-Regel des Vorschlags):
//   let usage = """
//       usage: tool [options] <file>
//         -v        verbose
//       """                       // Einzug dieser Zeile wird von allen Zeilen entfernt
