#!/usr/bin/env bash
# Scores brain lookup against a test set of queries. It only reads: lookup writes no graph file.
#
#   scripts/search-eval.sh <queries.tsv>
#
# Each line of the file is a query, a tab, and the refs it should find, space separated. A query with no refs is
# nonsense: it should find nothing sure. Blank lines and # lines are skipped. Every query runs through `brain lookup`
# from the current directory, so the project scope is the one around it. BRAIN names the brain to run, `brain` on PATH
# by default.
#
# Lookup prints a ref, then its tier, sure or likely, and leaves weak hits out, so every hit it returns is sure or
# likely.
#
# Per labelled query it prints the rank of the first expected ref, 0 when none is there, how many of the expected refs
# are in the top 5, and how many come back at all. Per nonsense query it prints how many hits came back sure and how
# many likely. Then the totals: recall@5, the mean share of a query's refs in its top 5; MRR, the mean of 1 / rank of
# its first expected ref; how many of all expected refs came back; how many hits a query returned; and how many
# nonsense queries returned a sure hit, and any hit.
set -euo pipefail

(($# == 1)) || {
    echo "usage: scripts/search-eval.sh <queries.tsv>" >&2
    exit 2
}

brain="${BRAIN:-brain}"
queries=0 recall_sum=0 reciprocal_sum=0 expected=0 came=0 returned=0
nonsense=0 nonsense_sure=0 nonsense_any=0

while IFS=$'\t' read -r query refs; do
    [[ -z "$query" || "$query" == \#* ]] && continue
    hits=() tiers=()
    while read -r ref tier _; do
        [[ "$ref" == *:* ]] || continue
        hits+=("$ref") tiers+=("$tier")
    done < <("$brain" lookup "$query" 2>/dev/null)

    read -r -a wanted <<<"${refs:-}"
    if ((${#wanted[@]} == 0)); then
        sure=0 likely=0
        for tier in ${tiers[@]+"${tiers[@]}"}; do
            [[ "$tier" == sure ]] && sure=$((sure + 1))
            [[ "$tier" == likely ]] && likely=$((likely + 1))
        done
        printf 'nonsense  %d sure  %d likely  %s\n' "$sure" "$likely" "$query"
        nonsense=$((nonsense + 1))
        ((sure > 0)) && nonsense_sure=$((nonsense_sure + 1))
        ((sure + likely > 0)) && nonsense_any=$((nonsense_any + 1))
        continue
    fi

    first=0 found=0 back=0
    for ((rank = 1; rank <= ${#hits[@]}; rank++)); do
        for ref in "${wanted[@]}"; do
            [[ "${hits[rank - 1]}" == "$ref" ]] || continue
            ((first == 0)) && first=$rank
            ((rank <= 5)) && found=$((found + 1))
            back=$((back + 1))
        done
    done
    printf '%2d  %d/%d  %d/%d back  %s\n' "$first" "$found" "${#wanted[@]}" "$back" "${#wanted[@]}" "$query"
    queries=$((queries + 1))
    returned=$((returned + ${#hits[@]}))
    expected=$((expected + ${#wanted[@]}))
    came=$((came + back))
    recall_sum="$(awk -v s="$recall_sum" -v f="$found" -v w="${#wanted[@]}" 'BEGIN { print s + f / w }')"
    reciprocal_sum="$(awk -v s="$reciprocal_sum" -v r="$first" 'BEGIN { print s + (r ? 1 / r : 0) }')"
done <"$1"

((queries > 0)) || {
    echo "search-eval: no labelled queries in $1" >&2
    exit 1
}
awk -v n="$queries" -v r="$recall_sum" -v m="$reciprocal_sum" -v e="$expected" -v c="$came" -v h="$returned" \
    'BEGIN { printf "search-eval: %d queries, recall@5 %.3f, MRR %.3f, %d/%d expected refs back, %.1f hits a query\n", n, r / n, m / n, c, e, h / n }'
printf 'search-eval: %d nonsense queries, %d with a sure hit, %d with any hit\n' \
    "$nonsense" "$nonsense_sure" "$nonsense_any"
