# stdlib5 — the seed of the Lyric 5 standard library

What `lyric5` compiles against, and nothing else: it never sees the 4.x `stdlib/`, which the
front-end tests still import (until M8a). The modules here are the ones the compiler itself binds
(`std.core`, `std.string`) and what the M2 programs need (`std.io`), declared as natively backed
functions the C emitter maps to the runtime (`Lyric5.Compiler`, the intrinsic table). This is a
provisional shape with a date: M8a rewrites `std` in Lyric (design/v5/spec/10), source-first, and
the intrinsic table falls with it.
