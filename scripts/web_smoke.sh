#!/usr/bin/env bash
# HTTP smoke tests for the ASP.NET + JS layer.
B=$1; pass=0; fail=0; T=$(mktemp -d)
chk(){ if eval "$2"; then pass=$((pass+1)); echo "ok   $1"; else fail=$((fail+1)); echo "FAIL $1"; fi; }
chk "page served" '[[ $(curl -s $B/) == *"app.js"* ]]'
chk "js served" '[[ $(curl -s $B/app.js) == *"/api/notes/summarize"* ]]'
R=$(curl -s -o $T/1 -w '%{http_code}' -H 'Content-Type: application/json' -d '{"sampleId":"bad","text":"x"}' $B/api/notes/summarize)
chk "invalid sample id -> 400 with field error" '[[ $R == 400 ]] && grep -q sampleId $T/1'
R=$(curl -s -o $T/2 -w '%{http_code}' -H 'Content-Type: application/json' -d '{"sampleId":"VB-202611","text":"Kontakta anna@example.se om provet"}' $B/api/notes/summarize)
chk "valid note -> 200, PII not echoed" '[[ $R == 200 ]] && grep -q summary $T/2 && ! grep -q "anna@example.se" $T/2'
R=$(curl -s -o $T/3 -w '%{http_code}' -H 'Content-Type: application/json' -d '{"sampleId":"VB-202612","text":"FAIL_MODEL"}' $B/api/notes/summarize)
chk "model failure -> 502 problem" '[[ $R == 502 ]]'
chk "audit via C# -> rows" '[[ $(curl -s $B/api/audit) == *prompt_hash* ]]'
echo; echo "web: $pass passed, $fail failed"; rm -rf $T; [ $fail -eq 0 ]
