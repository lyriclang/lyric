# Writing the Book

The Book is Markdown in this directory. `SUMMARY.md` gives its title and the chapters in reading
order, one list line each: `- [Label](file.md)`. A chapter's page is named after its file —
`first-program.md` is `book/first-program/` on the website. A link to another chapter is written to
its file, `[the first program](first-program.md#errors)`; a link to any other file of the
repository, relative to this directory, points at that file on GitHub. This file is not a chapter.

## Snippets

Every Lyric snippet of a chapter is compiled by the test suite (`BookTests`), so the Book cannot
show code the compiler does not take.

| Fence | What the suite checks |
|---|---|
| ` ```lyric ` | builds, runs and exits with 0 |
| ` ```lyric,no_run ` | builds; for code that waits for input, a network or a file |
| ` ```lyric,compile_fail=LYR-SEM0001 ` | the check fails with that code and no other error |
| ` ```output ` right below a `lyric` snippet | exactly what the snippet prints |
| ` ```diagnostic ` right below a `compile_fail` snippet | exactly what `lyric check` says about it, as the program is built (a snippet with `fn main` keeps its line numbers) |
| ` ```text `, ` ```toml `, ` ```sh ` … | nothing |

A snippet that declares `fn main` is the program as it stands. Without one, a snippet is one of two
kinds. **Declarations** — a line at its left edge starts a function, a type, an extension or an
attribute — are built with an empty `fn main(): void {}` after them: they must compile, nothing
runs. **Statements** are the body of `main`: the snippet's `import` lines go above it, everything
else into `fn main(): void { … }`. A snippet writes its imports itself — the prelude is all it has
without them — and declares what it uses; it sees nothing of the snippets before it.

The page says above a snippet what its info string says — "Does not compile" with the code, or
"Compiles; not run here"; an `output` or `diagnostic` block shows as what the program printed or
the compiler said, set apart from code. A snippet that must fail should have its `diagnostic`: a
reader new to the language sees from it what is wrong and where.
