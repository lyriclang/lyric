# stdlib5 — the Lyric 5 standard library

What `lyric5` compiles against, and nothing else: it never sees the 4.x `stdlib/`, which the
front-end tests still import until the 4.x front end leaves `main` (M17). It is written in Lyric,
source-first (design/v5/spec/10). What stays in C is reached through bodiless declarations the C
emitter maps to the runtime (`Lyric5.Compiler`, the intrinsic table), until `extern "C"` takes
them over (M14). `std.core` is what the language binds without an import and `std.prelude` what
every module names without one; the other modules follow design 10's module cut. M8b brings the
I/O and the system, M8c the rest of design 10's list.
