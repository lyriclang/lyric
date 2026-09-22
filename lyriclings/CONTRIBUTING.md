# Contributing an exercise

An exercise teaches ONE concept by being broken in exactly one instructive way. Before you
write one, read three existing ones in the chapter where yours belongs.

## The rules

1. **One concept, one error.** The broken file produces exactly one intended diagnostic (or
   one panic, or one wrong output). No typo hunts: the error must be something a learner
   would plausibly write and must be explained by the comment block — a type error, a
   missing case, an unhandled `throws`, a value-semantics surprise.
2. **The comment block explains the concept**, in a few sentences, with the diagnostic code
   the learner will see and a one-line reference to the spec (`spec §7.4`) or the guide
   (`guide chapter 9`). Then `Expected output:` with the exact lines. The explanation is
   written for someone who has done every exercise before it and none after.
3. **The marker** `// I AM NOT DONE` stands on its own line after the comment block.
4. **Only 4.4.1 features.** Check the spec under `~/dev/projects/lyricspec/spec/` and the
   guide; do not use anything marked as a later version, and avoid the known compiler
   limits (an `IR0001` is a compiler limit, never a teaching point).
5. **A solution** with the same file name in `solutions/` compiles without a single
   diagnostic — warnings included — and prints the expected output. Where more than one fix
   is reasonable, the solution's comment names the alternative.
6. **Register it** in `exercises.json`, in chapter order:

   ```json
   { "name": "optionals3", "path": "exercises/12_optionals/optionals3.lyr",
     "chapter": "12 optionals",
     "hint": "…what to look for, and roughly how to fix it…",
     "expect": "host = example.org\nport = unset\n",
     "broken": "panic:LYR-VM0007" }
   ```

   `broken` is the diagnostic code of the shipped file (`LYR-SEM0019`), `panic:LYR-VM0006`
   for a runtime panic, or `output` for a program that runs and prints the wrong thing.
   `expect` may be omitted when only "compiles and exits 0" is asked, but every exercise so
   far has one, and an expected output is what tells the learner they are done.
7. **Verify both directions** before you commit:

   ```bash
   lyric run lyriclings.lyr -- audit                # shipped fails as declared, solution passes
   lyric run lyriclings.lyr -- verify --solutions   # all green, in order
   lyric check solutions/<chapter>/<name>.lyr --deny-warnings
   ```

## Naming and numbering

Chapters are `NN_name/`, exercises `<topic>N.lyr` numbered from 1 within the chapter. A
multi-file exercise keeps its module files next to the entry file (chapter 18 is the
example) and says so in the comment block; the marker stays in the entry file.

## When the compiler is the problem

If a construct you wanted to teach hits a compiler bug or an `IR0001`, do not build the
exercise around it — file the finding with a minimal reproduction, and teach the concept
through a form that works. The exercises are a description of the language, not of the
current implementation.
