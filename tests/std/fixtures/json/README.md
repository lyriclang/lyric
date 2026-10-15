# JSONTestSuite

The parsing cases of [nst/JSONTestSuite](https://github.com/nst/JSONTestSuite) (Nicolas Seriot,
MIT — `LICENSE`), as `tests/std/tests/json_suite.lyr` runs them through `json.parse` (12 §JSON
rule 2; the review of M10, M16): the 95 `y_` files must parse, the 188 `n_` files must be refused,
the 35 `i_` files go either way and must not panic. Taken from the repository's `master` on
2026-10-15 (the archive `master.zip`, SHA-256 of the download
`c2a1c43521f85097d125c6becaf35a7e4b415429c52e49c5900b846f4f402fe5`); `test_parsing/` whole, nothing else of the repository.

Updating: replace `test_parsing/` and `LICENSE` from a new archive, fix the counts in the test
where they changed, and note the date and hash here.
