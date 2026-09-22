# 14 Vergleich: Python — Tupel-Entpacken im for-Kopf (`for k, v in d.items()`) und
# in enumerate; Lambda-Parameter können NICHT entpacken (seit Python 3 — `lambda (a, b):`
# wurde entfernt), also `lambda p: p[0] * p[1]`. Rust erlaubt beides: `for (k, v) in &m`
# und `|(a, b)| a * b` (Closure-Parameter sind Muster) — die Lyric-Form `((a, b)) =>` ist
# Rusts `|(a, b)|` mit der Doppelklammer, weil `(a, b) =>` in Lyric zwei Parameter bedeutet.
from functools import reduce

m = {"a": 1, "b": 2}
total = 0
for k, v in m.items():                       # (1)
    print(f"{k}={v}")
    total += v

for i, name in enumerate(["x", "y", "z"]):   # (2)
    print(f"{i}: {name}")

pairs = [(1, 10), (2, 20)]
s = reduce(lambda acc, p: acc + p[0] * p[1], pairs, 0)   # (3) kein Entpacken im Lambda
print(f"sum {s}")
# Ausgabe:
# a=1
# b=2
# 0: x
# 1: y
# 2: z
# sum 50

# Rust:
#   for (k, v) in &m { … }
#   pairs.iter().fold(0, |acc, &(a, b)| acc + a * b)
