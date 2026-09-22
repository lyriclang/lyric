// 20 Vergleich: Rust — Struct-Update `Config { port, ..base }` (Rest aus base, base wird
// bewegt/kopiert) und Tupel-Index `t.1`. Die Rust-Falle `t.0.1` (lext als `t` `.` `0.1`) wurde
// im Compiler mit einer Sonderregel gelöst; der Lyric-Vorschlag umgeht sie mit "nur eine Ebene".
// Kotlin: `data class` + `base.copy(port = 8080)`; C#: `base with { Port = 8080 }`, `t.Item2`.
#[derive(Clone, Debug)]
struct Config { host: String, port: i64, secure: bool, retries: i64, timeout_ms: i64, label: String }

fn with_port(base: &Config, port: i64) -> Config {
    Config { port, ..base.clone() }
}

fn main() {
    let base = Config { host: "h".into(), port: 80, secure: false, retries: 3, timeout_ms: 1000, label: "x".into() };
    let a = with_port(&base, 8080);
    let b = Config { port: 9090, label: "y".into(), ..base.clone() };
    println!("{} {} {}", a.port, b.port, base.port);

    let triple = (1, "two", 3.0);
    println!("{}", triple.1);

    let pairs = [(1, "a"), (2, "b")];
    println!("{}", pairs[1].1);
}
// Ausgabe:
// 8080 9090 80
// two
// b
