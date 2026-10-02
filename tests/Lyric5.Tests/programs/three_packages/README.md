# A program of three packages

M7's example (design/v5/spec/13): a program built from its own package and two it depends on —
one by its directory, one from git — offline once the cache holds it. It lives beside M6's
artifacts: `examples/` is the Lyric 4 corpus, which the 4.x checks read under 4.x rules.

- `app/` — the program: `src/main.lyr`, and `src/report.lyr`, a module of its own.
- `shapes/` — a path dependency: `app` names it `shapes = { path = "../shapes" }`.
- `units/` — the package `app` reads from git. To build the example, make `units/` a repository
  of its own and add it to `app`:

  ```
  cd units && git init -q -b main && git add -A && git commit -qm units && git tag v1.0.0 && cd ..
  lyric5 add units --git "file://$PWD/units" --tag v1.0.0 -C app
  lyric5 run -C app
  ```

  `lyric.lock` beside `app/lyric.toml` then holds the commit and the content's hash of `units`
  at `v1.0.0`, and `~/.cache/lyric/git` the repository: `lyric5 build --offline -C app` builds
  from there, with the repository gone. `lyric5 build --target x86_64-windows-gnu -C app` builds
  the same program for Windows.

`ExampleTests` in `tests/Lyric5.Tests` does exactly this.
