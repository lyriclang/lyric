# What the compiler tells you

A compiler that refuses a program owes its writer more than "no". This chapter is about what Lyric
says when something is wrong, how to read it, which of it you decide, and the verbs of the `lyric`
command that work with it.

## Reading a diagnostic

Here is a program with a mistake in it:

```lyric,compile_fail=LYR-SEM0001
fn main(): void {
    let count: int = "three";
    let _ = count;
}
```

`lyric build` answers:

```text
error[LYR-SEM0001]: cannot assign 'string' to 'int'
 --> src/main.lyr:2:5
  |
2 |     let count: int = "three";
  |     ^^^^^^^^^^^^^^^^^^^^^^^^^
```

Every finding has the same parts. Its **severity** — `error`, which stops the build, or `warning`,
which does not. Its **code**, `LYR-SEM0001`: the prefix says which part of the toolchain found it
(`LEX` the lexer, `PAR` the parser, `RES` names and modules, `SEM` the checker, `PKG` the manifest,
`CLI` the command line, `RT` a panic at run time). The **message** says what is wrong in one
sentence. The **place**, a file, a line and a column, and under it the line itself with the part that
is wrong marked.

Below the source, a finding may carry **notes**: `note:` another place that belongs to the finding,
`help:` what to do about it. A `help:` that shows the line as it would read is a **suggestion** — a
change the compiler can make for you (see `lyric fix` below).

A mistake is said once. What follows from it — the next statement that uses the broken value, the
parser's way back into the file — is not reported again: when a build says three errors, there are
three things to fix.

## Every code is explained

`lyric explain` prints what a code means, why it is a rule, and a wrong and a right program:

```text
$ lyric explain LYR-SEM0001
```

The same text is in the reference of diagnostics on the website, one page a code. A code is
written as you like — `sem0001`, `LYR-SEM0001` — and a lint (below) by its name.

## Warnings are lints

A warning does not stop the build, and every warning is a **lint**: it has a name, a group, and a
level of its own. This program builds, and says so:

```lyric
fn main(): void {
    let unused = 1;
}
```

```text
warning[LYR-SEM0071]: 'unused' is never used
 --> src/main.lyr:2:9
  |
2 |     let unused = 1;
  |         ^^^^^^
  |
  = note: name it '_' when the value is deliberately unused
```

The lint is `unused-variable`, in the group `unused`. The groups are `unused`, `correctness`,
`resources`, `concurrency`, `deprecation`, `style`, `perf` and `docs`; `warnings` names them all.

A lint's **level** is `allow` (not said), `warn` (said as a warning) or `deny` (said as an error: the
build fails). Most lints warn; a few are allowed until you ask for them — `float-equality`,
`shadowed-local`, `needless-return`, the documentation lints — and one, `reserved-device-name`,
denies.

## Setting a lint's level

There are three places to set a level, and they work the same way.

**On a declaration**, with an attribute. It holds for what the declaration contains:

```lyric
@Allow("unused-variable")
fn main(): void {
    let unused = 1;
}
```

`@Warn` and `@Deny` take a lint, a group or a code the same way. An `@Allow` that decides nothing
— no such warning inside — is itself warned of (`useless-allow`), so that stale ones do not pile up.

**For the package**, in `lyric.toml`:

```toml
[lints]
deny = ["unused"]
allow = ["shadowed-local"]
```

**For one command**, on the command line: `-A`, `-W` and `-D` with a lint, a group, a code or
`warnings`:

```text
$ lyric build -D warnings
```

The narrowest attribute around a finding decides. Without one, the command line decides over the
manifest, and the manifest over the lint's own level; within one place a lint decides over its group,
and its group over `warnings`.

`--deny-warnings`, and a profile's `denyWarnings = true`, turn every warning into an error — the
setting for a continuous build that must stay clean. A denied finding says who denied it.

The warnings of your dependencies, and of the standard library, are not said: they are theirs to
fix. `--warn-deps` says them, at their own levels, and never as errors.

## Letting the compiler fix it

Many findings know their remedy. `lyric fix` makes the safe ones in your package's files — an
import taken out, `var` written as `let`, a `try` nothing under it throws, a deprecated name replaced
by its successor — and then says what is left:

```text
$ lyric fix
       Fixed src/main.lyr (3 changes)
warning[LYR-SEM0215]: 'my_val' is a local, and locals are named in camelCase
...
help: 'myVal'
```

A suggestion that needs a reader — a new name, a guess at what was meant — is shown and not made.
`lyric fix --dry-run` prints the changes as a diff and writes nothing. A second `lyric fix` changes
nothing.

## Checking without building

`lyric check` takes the program through everything a build does before the machine code — the
build scripts, the checker and its lints, what runs at compile time — and stops there. It says what
`build` would say, faster, and writes no binary: the verb for an editor's save or a pre-commit hook.

## The findings as data

`--json` on `build`, `run`, `test`, `check` and `fix` writes one JSON object a line instead of the
text: every finding with its code, places, notes and suggested changes, what the build wrote, and a
closing count. A tool reads a line at a time while the command runs.

## A package's first files, and the settings

`lyric new <name>` makes a directory with a package in it — `lyric.toml`, `src/main.lyr` printing
`Hello, world!`, a `.gitignore` — and `lyric init` makes the package in the directory you are in.
`--lib` makes a library instead, `--git` a git repository with it (or set `LYRIC_GIT_ON_NEW=1` once
for every new package).

`lyric env` lists every setting a build reads — the toolchain, the C compiler, the profile and each
of its fields, the target — and where each value comes from: the command line, an environment
variable, `lyric.toml`, or the default.

`lyric help` lists the verbs, `lyric help <verb>` a verb's options, and
`lyric completions bash|zsh|fish|pwsh` prints a script that teaches your shell all of them.
