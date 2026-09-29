#!/bin/bash
A="/c/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/funktionen-audit"
LYRC="/c/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="/c/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
LYRFMT="/c/Users/Olivier/CLionProjects/lyric/src/Lyrfmt/bin/Debug/net10.0/lyrfmt.dll"
cd "$A"; mkdir -p out
mainonly() { awk '/^fn main\.main /{p=1} p{print} p&&/^}/{exit}'; }
ordered() { grep -oE "LYR-[A-Z]+[0-9]+" | tr '\n' ' '; }
echo "##### A. check --emit (option after file)"
for f in c_p61_iface_default_omitted c_q11b_lambda_yield_called; do
  r=$(dotnet "$LYRC" check "$f.lyr" --emit --progress never 2>&1); echo "check --emit $f: exit=$? :: $(echo "$r" | head -1 | cut -c1-160)"
done
echo "##### B. release build of p61"
dotnet "$LYRC" build c_p61_iface_default_omitted.lyr -o out/p61r.lyrbc --release --progress never 2>&1 | head -2 | cut -c1-240
echo "##### C. release IR counts in main.main"
for f in c_q18_iter_trailing a11_single_chain_rel c_q29_noncapturing_local c_q28_nonescaping_closure; do
  dotnet "$LYRC" lower "$f.lyr" --release --progress never > "out/$f.rel.ir" 2>&1
  m=$(mainonly < "out/$f.rel.ir")
  echo "$f RELEASE main: callvirt=$(echo "$m" | grep -c callvirt) callind=$(echo "$m" | grep -c callind) mkclosure=$(echo "$m" | grep -c mkclosure) newobj=$(echo "$m" | grep -c newobj) call=$(echo "$m" | grep -cE ' call ') __inl=$(echo "$m" | grep -c __inl_) lines=$(echo "$m" | wc -l) fns_total=$(grep -c '^fn ' out/$f.rel.ir)"
done
echo "--- a11 release main: call-shaped lines"; mainonly < out/a11_single_chain_rel.rel.ir | grep -nE "callvirt|callind|mkclosure|mkiface|newobj|loadfield" | head -20
echo "--- a11 release: which fns survive"; grep '^fn ' out/a11_single_chain_rel.rel.ir | cut -c1-80
echo "--- c_q29 release main"; mainonly < out/c_q29_noncapturing_local.rel.ir | grep -nE "mkclosure|callind|call "
echo "--- c_q28 release main"; mainonly < out/c_q28_nonescaping_closure.rel.ir | grep -nE "mkclosure|callind|newobj|call "
echo "##### D. q08 ordered codes"; dotnet "$LYRC" build c_q08_fold_trailing.lyr -o out/x.lyrbc --progress never 2>&1 >/dev/null | ordered; echo
echo "##### E. p01 ordered codes"; dotnet "$LYRC" build c_p01_named.lyr -o out/x.lyrbc --progress never 2>&1 >/dev/null | ordered; echo
echo "##### F. message texts"
for f in n08_global_in_lambda n17_bare_no_context n18_paren_untyped c_q02_outer_it c_q27_default_refs_param c_q12_default_before_trailing; do
  echo "--- $f"; dotnet "$LYRC" build "$f.lyr" -o out/x.lyrbc --progress never 2>&1 >/dev/null | grep -E "^(error|warning)" | head -3 | cut -c1-220
done
echo "##### G. JIT (option after file)"
for f in c_q16_escaping_closure a11_single_chain_rel c_q28_nonescaping_closure n16_depth_callind c_p39_tail_recursion; do
  r=$(dotnet "$LYRVM" run "out/$f.lyrbc" --jit 2>&1); x=$?; echo "$f --jit: exit=$x :: $(echo "$r" | head -3 | tr '\n' '|' | cut -c1-200) lines=$(echo "$r" | wc -l)"
done
echo "##### H. lyrfmt n27 (trailing lambda line)"; dotnet "$LYRFMT" --stdin < n27_fmt_forms.lyr 2>&1 | sed -n '9,12p'
echo "##### I. timing release, 3M calls"
for f in t_direct t_ind t_virt; do
  dotnet "$LYRC" build "$f.lyr" -o "out/$f.lyrbc" --release --progress never 2>&1 >/dev/null | grep -E "^error" | head -2
  for i in 1 2 3; do s=$(date +%s%N); dotnet "$LYRVM" run "out/$f.lyrbc" > out/t.out 2>&1; e=$(date +%s%N); echo "$f run$i: $(( (e - s) / 1000000 )) ms out=$(cat out/t.out)"; done
done
echo "--- t_ind release main shape:"; dotnet "$LYRC" lower t_ind.lyr --release --progress never 2>&1 | mainonly | grep -nE "callind|call |mkclosure" | head -4
echo "--- t_virt release main shape:"; dotnet "$LYRC" lower t_virt.lyr --release --progress never 2>&1 | mainonly | grep -nE "callvirt|call |mkiface|__inl" | head -4
echo "--- t_direct release main shape:"; dotnet "$LYRC" lower t_direct.lyr --release --progress never 2>&1 | mainonly | grep -nE "call |__inl" | head -4
echo "##### J. debug timing for scale"; for f in t_direct t_ind; do dotnet "$LYRC" build "$f.lyr" -o "out/$f.dbg.lyrbc" --progress never >/dev/null 2>&1; s=$(date +%s%N); dotnet "$LYRVM" run "out/$f.dbg.lyrbc" >/dev/null 2>&1; e=$(date +%s%N); echo "$f debug: $(( (e - s) / 1000000 )) ms"; done
