// 17 Vergleich: Rust — Sichtbarkeit ist MODUL-basiert: ein Feld/eine Methode ohne `pub` ist
// im deklarierenden Modul (und dessen Kindern) sichtbar, außerhalb nicht. Ein Struct-Literal
// außerhalb des Moduls ist nur möglich, wenn ALLE Felder pub sind — sonst Konstruktorfunktion.
// Das ist exakt der Lyric-Vorschlag. (Swift: `private`/`internal`/`public`, Standard `internal`
// = modulweit; Kotlin/C#: `private` ist TYP-privat — enger als Rust/Lyric-Vorschlag.)
mod bank {
    pub struct Account {
        pub owner: String,
        balance: i64,             // privat
        audit: i64,               // privat
    }
    impl Account {
        pub fn read(&self) -> i64 { self.balance }
        pub fn deposit(&mut self, n: i64) {
            if n <= 0 { return; }
            self.balance += n;
            self.log();
        }
        fn log(&mut self) { self.audit += 1; }   // privat
    }
    pub fn open(owner: &str) -> Account {
        Account { owner: owner.to_string(), balance: 0, audit: 0 }   // im Modul: alle Felder
    }
}

fn main() {
    let mut a = bank::open("ada");
    a.deposit(50);
    println!("read {} owner {}", a.read(), a.owner);
    // a.balance = -999;   // error[E0616]: field `balance` of struct `Account` is private
    // a.log();            // error[E0624]: method `log` is private
    // let forged = bank::Account { owner: "eve".into(), balance: 1_000_000, audit: 0 };
    //                     // error[E0451]: field `balance` of struct `Account` is private
}
// Ausgabe:
// read 50 owner ada
