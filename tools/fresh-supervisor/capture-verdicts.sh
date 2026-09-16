#!/usr/bin/env bash
# TEN VERDICTS FROM THE ERA THAT IS ABOUT TO END.
#
# Run this BEFORE the supervisor is flipped to `resume: fresh`. Afterwards the transcript-era
# verdicts exist only in channel history, and Channel_Compactor moves older entries out of the live
# file into <channel>.archive.md (CLAUDE.md decision 13) — so both are read here, as ONE history.
# Without this the acceptance criterion of plan 06 ("ten verdicts read blind against ten from the
# transcript era") cannot be evaluated at all. That ordering is not stated in the spec.
#
# ANONYMISED, because the comparison is BLIND. The header, the index, the date and the STATE: line
# each say which era an entry is from, and a verdict that identifies itself makes the read worthless
# — a confident answer to a question nobody asked. The key goes OUTSIDE the directory handed to the
# reader.
#
#   0  captured
#   2  cannot check — the channel is not where it was said to be
set -eu

SUP="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}"

self_test() {
  local root out failures=0 files
  root="$(mktemp -d)"
  mkdir -p "$root/repo-1"

  # An archive and a live file, as a compacted channel actually looks. The oldest verdict is in the
  # ARCHIVE — the case that matters, and the one a live-file-only read would miss.
  {
    printf '## [1] FROM supervisor — 2026-09-01 10:00 — VERDICT — oldest\n\nARCHIVED BODY\nSTATE: waiting\n\n'
    printf '## [2] FROM owner — 2026-09-01 10:05 — thanks\n\nok\n\n'
  } > "$root/repo-1/owner-channel.archive.md"
  {
    printf '## [3] FROM supervisor — 2026-09-02 09:00 — VERDICT — newer\n\nLIVE BODY\nSTATE: working\n\n'
    printf '## [4] FROM app — 2026-09-02 09:01 — [agent] turn_ended\n\nnote\n\n'
  } > "$root/repo-1/owner-channel.md"

  out="$(AIORCH_SUPERVISION_ROOT="$root" bash "$0" repo-1 5 "$root/out" 2>&1)" || {
    echo "FAIL: capture exited non-zero — $out"; failures=1; }

  files="$(find "$root/out" -name '*.md' 2>/dev/null | wc -l | tr -d ' ')"
  [ "$files" = 2 ] || { echo "FAIL: expected 2 supervisor verdicts (archive + live), got $files"; failures=1; }

  grep -rq "ARCHIVED BODY" "$root/out" || { echo "FAIL: the ARCHIVE was not read — a compacted channel's live file is not its history"; failures=1; }
  grep -rq "LIVE BODY" "$root/out" || { echo "FAIL: the live file was not read"; failures=1; }

  # Nothing that says which era, and nothing another author wrote.
  grep -rq "FROM supervisor" "$root/out" && { echo "FAIL: the header survived — it dates the entry"; failures=1; }
  grep -rq "^STATE:" "$root/out" && { echo "FAIL: the STATE: line survived — it is a tell"; failures=1; }
  grep -rq "2026-09" "$root/out" && { echo "FAIL: a date survived — it dates the entry"; failures=1; }
  grep -rq "turn_ended" "$root/out" && { echo "FAIL: an app entry was captured — only the supervisor's words are verdicts"; failures=1; }
  grep -rq "thanks" "$root/out" && { echo "FAIL: an owner entry was captured"; failures=1; }

  [ -s "$root/out.key.txt" ] || { echo "FAIL: no key was written"; failures=1; }

  rm -rf "$root"
  [ "$failures" = 0 ] && echo "capture-verdicts self-test: PASS"
  return "$failures"
}

[ "${1:-}" = "--self-test" ] && { self_test; exit $?; }

ORCH="${1:?usage: capture-verdicts.sh <orch-id> <count> <out-dir> | --self-test}"
COUNT="${2:?usage: capture-verdicts.sh <orch-id> <count> <out-dir>}"
OUT="${3:?usage: capture-verdicts.sh <orch-id> <count> <out-dir>}"

FOLDER="$SUP/$ORCH"
LIVE="$FOLDER/owner-channel.md"
ARCHIVE="$FOLDER/owner-channel.archive.md"

# REFUSES rather than producing an empty sample (decision 20): zero verdicts is not a finding, it is
# a script that looked in the wrong place.
[ -f "$LIVE" ] || { echo "cannot check: $LIVE does not exist" >&2; exit 2; }

mkdir -p "$OUT"
key="$OUT.key.txt"; : > "$key"

# Archive first, then live: one history, oldest to newest (decision 13). Bodies only — the header
# carries the author, the index and the date, all of which date the entry.
cat "$ARCHIVE" 2>/dev/null "$LIVE" \
  | awk '
      /^## \[[0-9]+\] FROM supervisor /  { if (keep && body != "") printf "%s\x1e", body; keep = 1; body = ""; next }
      /^## \[[0-9]+\] FROM /             { if (keep && body != "") printf "%s\x1e", body; keep = 0; body = ""; next }
      keep                               { body = body $0 "\n" }
      END                                { if (keep && body != "") printf "%s\x1e", body }
    ' \
  | tail -c 10000000 \
  | while IFS= read -r -d $'\x1e' entry; do
      # STATE: is the session's own status line and says which era wrote it; strip it and blank lines
      # at the ends. Everything left is the verdict's prose, which is what is being judged.
      cleaned="$(printf '%s' "$entry" | grep -v '^STATE:')"
      [ -n "$(printf '%s' "$cleaned" | tr -d '[:space:]')" ] || continue
      name="$(printf '%s' "$cleaned" | shasum | cut -c1-8)"
      printf '%s\n' "$cleaned" > "$OUT/$name.md"
      echo "$name transcript-era $ORCH" >> "$key"
    done

# The newest COUNT, oldest dropped — done after the write so the shas are stable and the key matches.
for stale in $(ls -t "$OUT"/*.md 2>/dev/null | tail -n +"$((COUNT + 1))"); do
  rm -f "$stale"
  sed -i.bak "/$(basename "$stale" .md)/d" "$key" && rm -f "$key.bak"
done

echo "captured $(find "$OUT" -name '*.md' | wc -l | tr -d ' ') verdicts into $OUT (key: $key)"
