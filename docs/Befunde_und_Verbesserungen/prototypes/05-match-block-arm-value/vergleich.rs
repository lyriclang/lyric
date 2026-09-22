// 05 Vergleich: Rust — ein Block ist ein Ausdruck; der letzte Ausdruck ohne `;` ist sein Wert.
// Seiteneffekt und Ergebnis stehen im selben Arm; `return` bleibt "Funktion verlassen".
#[derive(Clone, Debug)]
enum Event { Dial(String), Ack, Data(i64), Hangup }
#[derive(Clone, Debug)]
enum State { Idle, Connecting(String), Connected(String), Closed }

struct Session { state: State, received: i64, log: i64 }

impl Session {
    fn step(&mut self, ev: Event) {
        self.state = match ev {
            Event::Dial(host) => { self.log += 1; State::Connecting(host) }   // Tail-Expression
            Event::Ack => match &self.state {
                State::Connecting(h) => State::Connected(h.clone()),
                other => other.clone(),
            },
            Event::Data(n) => { self.received += n; self.state.clone() }
            Event::Hangup => { self.log += 1; State::Closed }
        };
    }
}

fn main() {
    let mut s = Session { state: State::Idle, received: 0, log: 0 };
    s.step(Event::Dial("host".into())); s.step(Event::Ack);
    s.step(Event::Data(5)); s.step(Event::Data(7));
    println!("{:?} received={} log={}", s.state, s.received, s.log);
    s.step(Event::Hangup);
    println!("{:?} received={} log={}", s.state, s.received, s.log);
}
// Ausgabe:
// Connected("host") received=12 log=1
// Closed received=12 log=2

// Kotlin zum Vergleich — `when` als Ausdruck, Block-Zweig: letzter Ausdruck ist der Wert:
//   state = when (ev) {
//       is Event.Data -> { received += ev.n; state }
//       is Event.Hangup -> { log += 1; State.Closed }
//       ...
//   }
