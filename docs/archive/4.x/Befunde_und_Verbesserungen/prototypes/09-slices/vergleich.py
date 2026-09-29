# 09 Vergleich: Python — Slicing `xs[a:b]` ist eine KOPIE (Liste), Grenzen optional,
# halboffen. Das ist genau die vorgeschlagene Semantik (Kopie, keine View).
# Rust: `&cs[start..i]` ist eine VIEW (Borrow) — braucht Lifetimes, für Lyric nicht nötig.
# Go: `xs[a:b]` ist eine View auf das Backing-Array (Aliasing!) — bewusst NICHT das Modell.

def is_alpha(c: str) -> bool:
    return c.isascii() and c.isalpha()

def first_word(text: str) -> str:
    cs = list(text)
    i = 0
    while i < len(cs) and is_alpha(cs[i]):
        i += 1
    return "".join(cs[:i])                    # Slice statt Kopierschleife

print(first_word("hello, world"))

xs = [10, 20, 30, 40, 50]
head, tail, mid = xs[:2], xs[3:], xs[1:4]     # 1..=3 ist in Python 1:4
print(f"{head[0]},{head[1]} … {tail[0]},{tail[1]} mid {len(mid)}")

arg = "--name=ada"
print(arg[7:])                                # Strings sind in Python indexierbar (O(1), UTF-32/Latin-1 intern)
# Ausgabe:
# hello
# 10,20 … 40,50 mid 3
# ada
