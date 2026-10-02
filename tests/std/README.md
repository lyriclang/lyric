# The standard library's own tests

A package of tests for `stdlib5/`, which `lyric test` runs as it runs any package's:

```
lyric5 test -C tests/std
```

`StdTests` in `tests/Lyric5.Tests` runs it from a copy, so CI does. A module here is
`stdtests.tests.<name>`; each tests one module of `std` through what it makes `pub`. The
behaviour of the library is tested here; the language's rules are the conformance suite's
(lyric-spec), which leaves the library to these tests.
