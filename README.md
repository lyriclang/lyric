# Lyric

> **`main` is Lyric 5 in development** (`5.0.0-dev`, no promise of any kind): a native successor
> compiled through C, designed in [`design/v5/spec/`](design/v5/spec/) and built along the plan in
> [`13-umsetzungsplan.md`](design/v5/spec/13-umsetzungsplan.md). The **last Lyric 4 release is
> [v4.5.0](https://github.com/lyriclang/lyric/releases/tag/v4.5.0)**; the 4.x line continues as
> [lyriclang/lyric-script](https://github.com/lyriclang/lyric-script).

A statically typed, GC-managed application language. Lyric 5 compiles to native code through C
(`zig cc`), with a runtime in C and a compiler in C#; it embeds as a C library.

![CI](https://github.com/lyriclang/lyric/actions/workflows/ci.yml/badge.svg)

Source files use `.lyr`.

## Status

[`STATUS.md`](STATUS.md) says which milestone is open. What the tree holds today:

- **`runtime/`** — the Lyric 5 runtime in C (M1): the collector contract with Boehm behind it,
  the object model, strings, arrays, panics with backtraces. Built for every Tier 1 target.
- **`src/Lyric5`** and **`src/Lyric5.Toolchain`** — the Lyric 5 command line as one NativeAOT
  binary (`lyric5` is its working name until M18) and the C build driver behind it.
- **`src/Lyric.Frontend`** — the 4.x front end (lexer, parser, resolver, sema, IR), which the
  Lyric 5 compiler grows out of milestone by milestone. The 4.x VM, tools and bytecode format are
  gone from this tree; they live on in `lyric-script`.

The tree says **4.6.0** for the 4.x code it still carries and **5.0.0-dev** for `lyric5`.

## Example

```lyr
import std.io.console { println };
import std.collections { List };

enum Shape {
    Circle(float),
    Rectangle(float, float);

    fn area(): float {
        return match (this) {
            Circle(r)       => 3.14159 * r * r,
            Rectangle(w, h) => w * h,
        };
    }
}

fn main(): int {
    let shapes = List<Shape>.empty();
    shapes.push(Shape.Circle(2.5));
    shapes.push(Shape.Rectangle(3.0, 4.0));

    for (s in shapes) {
        println(f"area = {s.area():N2}");
    }
    return 0;
}
```

This is 4.x Lyric, as the [`examples/`](examples/) are; `lyric fix --from-4` (M16) will carry
them over, and the milestones on the way say what changes.

## Requirements

.NET 10 SDK, and a C compiler for the runtime and the native programs: `zig` (recommended, the
version in [`tooling/zig-version`](tooling/zig-version)), clang or gcc. The sanitizer profiles
need clang.

## Build and run

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/Lyric5 -- --version
```

## Repository layout

```
lyric/
├── runtime/              the Lyric 5 runtime in C: include/lyr, src, tests, third_party
├── src/
│   ├── Lyric5/           → lyric5        the Lyric 5 command line, NativeAOT
│   ├── Lyric5.Toolchain/ → the C side: compiler, targets, profiles, cached C builds of the runtime
│   ├── Lyric.Core/       → lyrcore.dll   diagnostics, source manager, shared tool basics
│   ├── Lyric.Frontend/   → lyrfe.dll     lexer, parser, resolver, sema, IR (4.x, the basis of 5)
│   └── Lyric.Lsp/        → lyrlsp.dll    language server protocol and analysis (4.x; M13b decides)
├── stdlib/               the 4.x standard library in Lyric (the front-end tests import it; M8a)
├── tests/                xUnit test projects
├── examples/             22 example programs (4.x; the input of M16)
├── design/v5/            the Lyric 5 design round: corpus and decisions (spec/00–13)
├── tooling/              zig-version and c-smoke/ — the C toolchain; valgrind/ — memcheck locally;
│                         textmate/ — the editor grammar, pinned against the lexer by the tests
├── tools/                DocGen, the documentation site generator
└── docs/                 guide and 4.x references; archive/4.x/ — 4.x planning
```

## Documentation

| Document | Contents |
|---|---|
| [`design/v5/spec/`](design/v5/spec/) | The Lyric 5 decisions, one document per area, and the plan (13) |
| [`STATUS.md`](STATUS.md) | The open milestone, what was learned on the way |
| [`docs/guide/`](docs/guide/) | The 4.x user guide (the 5 guide comes with M17) |
| [`docs/Grammar.md`](docs/Grammar.md), [`docs/Bytecode.md`](docs/Bytecode.md), [`docs/Pack.md`](docs/Pack.md) | The 4.x references, frozen at the 4.6 cut |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | The three rules and the process |
| [`CHANGELOG.md`](CHANGELOG.md) | What changed per release, from v1.0.0 on |

The normative specification lives in [lyriclang/lyric-spec](https://github.com/lyriclang/lyric-spec):
`main` is the Lyric 5.0 text, written milestone by milestone; `script` is the 4.x text.

`tools/DocGen` renders the guide and the references into the documentation site:

```bash
dotnet run --project tools/DocGen -- site . artifacts/site dev
```

## Versioning

From v1.0 the project follows semantic versioning with three components, `vMAJOR.MINOR.PATCH`:

| Component | Increments on |
|---|---|
| MAJOR | incompatible language or standard library change |
| MINOR | backwards-compatible additions |
| PATCH | backwards-compatible fixes |

## Branches

| Branch | Purpose |
|---|---|
| `main` | Always green. Every commit passes CI on Linux, Windows and macOS. |
| `feature/<name>`, `fix/<name>`, `m<N>/<slice>` | Work, merged into `main` through a pull request. |

## Releases

Two channels ([11 T5](design/v5/spec/11-werkzeuge-interop.md)):

- **dev** — every push to `main` is built and replaces the rolling `dev` prerelease: one archive
  per Tier 1 target with `lyric5`. No promise of any kind.
- **Stable** — an annotated tag `vX.Y.Z`; the first Lyric 5 release is `v5.0.0` (M18).

The editor clients live in their own repositories and release on their own cadence:
[vscode-lyric](https://github.com/lyriclang/vscode-lyric) and
[jetbrains-lyric](https://github.com/lyriclang/jetbrains-lyric).

## License

[MIT](LICENSE)
