#!/bin/bash
A="/c/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/funktionen-audit"
S="/c/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes"
LYRC="/c/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="/c/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
LYRFMT="/c/Users/Olivier/CLionProjects/lyric/src/Lyrfmt/bin/Debug/net10.0/lyrfmt.dll"
cd "$A"; mkdir -p out
cp "$S/funktionen-rev2/q28_nonescaping_closure.lyr" c_q28_nonescaping_closure.lyr
cp "$S/funktionen-rev2/q20_fmt_chain.lyr" c_q20_fmt_chain.lyr
codes() { grep -oE "LYR-[A-Z]+[0-9]+" | sort | uniq -c | tr '\n' ' '; }
mainonly() { awk '/^fn main\.main /{p=1} p{print} p&&/^}/{exit}'; }
echo "##### 1. check vs check --emit on the ICE/defect cases"
for f in c_p61_iface_default_omitted c_q09_this_lambda c_q10_break_lambda c_q11b_lambda_yield_called; do
  r=$(dotnet "$LYRC" check "$f.lyr" 2>&1); x=$?; echo "check $f: exit=$x :: $(echo "$r" | head -1 | cut -c1-120)"
  r=$(dotnet "$LYRC" check --emit "$f.lyr" 2>&1); x=$?; echo "check --emit $f: exit=$x :: $(echo "$r" | head -1 | cut -c1-120)"
done
echo "##### 2. release build of p61 (verifier message)"
dotnet "$LYRC" build --release c_p61_iface_default_omitted.lyr -o out/p61r.lyrbc 2>&1 | head -2 | cut -c1-220
echo "##### 3. release IR counts in main.main"
for f in c_q18_iter_trailing a11_single_chain_rel c_q29_noncapturing_local c_q28_nonescaping_closure; do
  dotnet "$LYRC" lower --release "$f.lyr" > "out/$f.rel.ir" 2>&1
  dotnet "$LYRC" lower "$f.lyr" > "out/$f.dbg.ir" 2>&1
  m=$(mainonly < "out/$f.rel.ir"); d=$(mainonly < "out/$f.dbg.ir")
  echo "$f RELEASE main: callvirt=$(echo "$m" | grep -c callvirt) callind=$(echo "$m" | grep -c callind) mkclosure=$(echo "$m" | grep -c mkclosure) newobj=$(echo "$m" | grep -c newobj) call=$(echo "$m" | grep -cE ' call ') __inl=$(echo "$m" | grep -c __inl_) lines=$(echo "$m" | wc -l)"
  echo "$f DEBUG   main: callvirt=$(echo "$d" | grep -c callvirt) callind=$(echo "$d" | grep -c callind) mkclosure=$(echo "$d" | grep -c mkclosure) newobj=$(echo "$d" | grep -c newobj) call=$(echo "$d" | grep -cE ' call ') __inl=$(echo "$d" | grep -c __inl_) lines=$(echo "$d" | wc -l)"
done
echo "--- a11 release main: the call-shaped lines"
mainonly < out/a11_single_chain_rel.rel.ir | grep -nE "callvirt|callind|mkclosure|mkiface|newobj|loadfield" | head -20
echo "--- c_q29 release main: all lines"
mainonly < out/c_q29_noncapturing_local.rel.ir | head -30
echo "##### 4. a10 debug lower: where does side() get called"
dotnet "$LYRC" lower a10_default_callsite.lyr 2>&1 | grep -nE "^fn |call main\.side" | head -10
echo "##### 5. q08 ordered codes"
dotnet "$LYRC" build c_q08_fold_trailing.lyr -o out/x.lyrbc 2>&1 | grep -oE "^(error|warning)\[LYR-[A-Z]+[0-9]+\]" | tr '\n' ' '; echo
echo "##### 6. q19: stdout lines, stderr lines, frames"
dotnet "$LYRVM" run out/c_q19_depth.lyrbc > out/q19.out 2> out/q19.err; echo "stdout=$(wc -l < out/q19.out) stderr=$(wc -l < out/q19.err) in_main.down=$(grep -c 'in main.down' out/q19.err) in_println=$(grep -c 'println' out/q19.err)"
head -3 out/q19.err | cut -c1-120; tail -2 out/q19.err | cut -c1-120
echo "##### 7. p01 source and per-call diagnostics"
cat c_p01_named.lyr; dotnet "$LYRC" build c_p01_named.lyr -o out/x.lyrbc 2>&1 | grep -cE "^error"
echo "##### 8. messages: n08 (module var), n17/n18 (SEM0045), c_q02 SEM0071 text, c_q27 IR0001 text, c_q21 SEM0086 head"
dotnet "$LYRC" build n08_global_in_lambda.lyr -o out/x.lyrbc 2>&1 | grep -E "^(error|warning)" | cut -c1-200
dotnet "$LYRC" build n17_bare_no_context.lyr -o out/x.lyrbc 2>&1 | grep -E "^(error|warning)" | cut -c1-200
dotnet "$LYRC" build n18_paren_untyped.lyr -o out/x.lyrbc 2>&1 | grep -E "^(error|warning)" | cut -c1-200
dotnet "$LYRC" build c_q02_outer_it.lyr -o out/x.lyrbc 2>&1 | grep -E "^(error|warning)" | cut -c1-200
dotnet "$LYRC" build c_q27_default_refs_param.lyr -o out/x.lyrbc 2>&1 | grep -E "^(error|warning)" | cut -c1-220
dotnet "$LYRC" build c_q21_overload_lambda.lyr -o out/x.lyrbc 2>&1 | grep -E "^(error|warning)|note" | head -4 | cut -c1-200
echo "##### 9. new probes n08b n28 n29"
for f in n08b_global_let_in_lambda n28_overload_trailing_rettype n29_var_struct_capture; do
  b=$(dotnet "$LYRC" build "$f.lyr" -o "out/$f.lyrbc" 2>&1 >/dev/null); x=$?; echo "$f build=$x $(echo "$b" | codes)"; echo "$b" | grep -E "^(error|warning)" | head -3 | cut -c1-200
  [ $x -eq 0 ] && echo "  run: $(dotnet "$LYRVM" run "out/$f.lyrbc" 2>&1 | tr '\n' '|')"
done
echo "--- n08b lower: how the global is read inside the lambda"
dotnet "$LYRC" lower n08b_global_let_in_lambda.lyr 2>&1 | grep -nE "^fn |lambda|global|mkclosure|env" | head -12
echo "##### 10. JIT: q16 and a11 with --jit"
dotnet "$LYRVM" run --jit out/c_q16_escaping_closure.lyrbc 2>&1 | tr '\n' '|'; echo " exit=$?"
dotnet "$LYRVM" run --jit out/a11_single_chain_rel.lyrbc 2>&1 | tr '\n' '|'; echo
dotnet "$LYRVM" run --jit out/c_q28_nonescaping_closure.lyrbc 2>&1 | tr '\n' '|'; echo
echo "##### 11. lyrfmt --stdin on the forms and on the chain"
echo "--- n27:"; dotnet "$LYRFMT" --stdin < n27_fmt_forms.lyr 2>&1 | sed -n '4,9p'
echo "--- q20:"; dotnet "$LYRFMT" --stdin < c_q20_fmt_chain.lyr 2>&1 | sed -n '5,10p'
echo "##### 12. timing (release), 3M calls, 2 runs each"
for f in t_direct t_ind t_virt; do
  dotnet "$LYRC" build --release "$f.lyr" -o "out/$f.lyrbc" 2>&1 | grep -E "^error" | head -2
  for i in 1 2; do s=$(date +%s%N); dotnet "$LYRVM" run "out/$f.lyrbc" > /dev/null 2>&1; e=$(date +%s%N); echo "$f run$i: $(( (e - s) / 1000000 )) ms"; done
done
echo "--- t_ind release IR main.main call shape:"; dotnet "$LYRC" lower --release t_ind.lyr 2>&1 | mainonly | grep -nE "callind|call |mkclosure" | head -5
echo "--- t_virt release IR main.main call shape:"; dotnet "$LYRC" lower --release t_virt.lyr 2>&1 | mainonly | grep -nE "callvirt|call |mkiface|__inl" | head -5
echo "--- t_direct release IR main.main call shape:"; dotnet "$LYRC" lower --release t_direct.lyr 2>&1 | mainonly | grep -nE "call |__inl" | head -5
