// 01 Vergleich: Rust — Result<T, E> ist ein gewöhnliches Enum, `?` propagiert.
// Ein Result ist ein WERT: es lässt sich sammeln, mappen, später auswerten.
// Rust hat keine Ausnahmen; Lyric hat sie, und dort ersetzt `throws` das `?`.

#[derive(Debug)]
struct ParseError { line: String, why: String }

impl std::fmt::Display for ParseError {
    fn fmt(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
        write!(f, "{} in '{}'", self.why, self.line)
    }
}

fn parse_port(line: &str) -> Result<i64, ParseError> {
    let err = |why: &str| ParseError { line: line.to_string(), why: why.to_string() };
    let (key, value) = line.split_once('=').ok_or_else(|| err("no '='"))?;   // `?` = frühe Rückkehr
    if key != "port" { return Err(err(&format!("unknown key {key}"))); }
    value.parse::<i64>().map_err(|_| err("not a number"))
}

fn parse_and_double(line: &str) -> Result<i64, ParseError> {
    Ok(parse_port(line)? * 2)                       // `?` reicht den Err weiter
}

fn main() {
    let inputs = ["port=8080", "host=x", "port=abc", "nonsense"];
    let results: Vec<Result<i64, ParseError>> = inputs.iter().map(|l| parse_and_double(l)).collect();
    let mut ok = 0;
    for r in &results {
        match r {
            Ok(v) => { println!("ok  {v}"); ok += 1; }
            Err(e) => println!("err {e}"),
        }
    }
    std::process::exit(ok);
}
// Ausgabe:
// ok  16160
// err unknown key host in 'host=x'
// err not a number in 'port=abc'
// err no '=' in 'nonsense'
