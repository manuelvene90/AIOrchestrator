import json, pathlib, subprocess, sys, tempfile

SCRIPT = "tools/wake-baseline/context_share.py"


def write_transcript(path, rows):
    path.write_text("\n".join(json.dumps(r) for r in rows) + "\n")


def call(usage):
    return {"sessionId": "s1", "type": "assistant", "message": {"id": usage.pop("id"), "usage": usage}}


def test_inherited_share_is_cache_read_over_everything_handed_to_the_model():
    """INHERITED = what the model was handed that it had already been handed before.

    In the CLI's own accounting that is `cache_read_input_tokens`: a resumed turn re-sends the whole
    conversation and the prefix hits the cache. `input_tokens` is what is NEW this call. The share is
    read/(read+new) — NOT read/input_tokens, which would exceed one, and not a count of turns, which
    says nothing about size.
    """
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        proj = root / "repo-1"
        proj.mkdir()
        write_transcript(proj / "sup.jsonl", [
            call({"id": "m1", "input_tokens": 1_000, "cache_read_input_tokens": 9_000,
                  "cache_creation_input_tokens": 0, "output_tokens": 300}),
            call({"id": "m2", "input_tokens": 1_000, "cache_read_input_tokens": 9_000,
                  "cache_creation_input_tokens": 0, "output_tokens": 300}),
        ])
        out = subprocess.run(
            [sys.executable, SCRIPT, "--projects", str(root),
             "--role-of", "sup.jsonl=supervisor", "--format", "json"],
            capture_output=True, text=True, check=True).stdout
        got = json.loads(out)["supervisor"]

        assert got["calls"] == 2
        assert got["mean_input_tokens"] == 10_000
        assert got["mean_inherited_tokens"] == 9_000
        assert got["inherited_share"] == 0.9


def test_cache_creation_counts_as_new_not_as_inherited():
    """A call that WRITES the cache paid for those tokens this turn — it did not inherit them. Folding
    cache_creation into the inherited half would make a session's very first call look 90 % inherited,
    which is the opposite of true and would flatter every fresh arm of this comparison."""
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        proj = root / "repo-1"
        proj.mkdir()
        write_transcript(proj / "sup.jsonl", [
            call({"id": "m1", "input_tokens": 1_000, "cache_read_input_tokens": 0,
                  "cache_creation_input_tokens": 9_000, "output_tokens": 300}),
        ])
        out = subprocess.run(
            [sys.executable, SCRIPT, "--projects", str(root),
             "--role-of", "sup.jsonl=supervisor", "--format", "json"],
            capture_output=True, text=True, check=True).stdout
        got = json.loads(out)["supervisor"]

        assert got["mean_input_tokens"] == 10_000
        assert got["inherited_share"] == 0.0


def test_the_same_message_is_counted_once():
    """A transcript replays assistant messages on resume, and sidechains repeat them too. Counting
    `m1` twice inflates every figure this plan is judged by (token-efficiency spec C7: dedupe by
    message.id)."""
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        proj = root / "repo-1"
        proj.mkdir()
        row = call({"id": "m1", "input_tokens": 1_000, "cache_read_input_tokens": 9_000,
                    "cache_creation_input_tokens": 0, "output_tokens": 300})
        write_transcript(proj / "sup.jsonl", [row, row])
        out = subprocess.run(
            [sys.executable, SCRIPT, "--projects", str(root),
             "--role-of", "sup.jsonl=supervisor", "--format", "json"],
            capture_output=True, text=True, check=True).stdout

        assert json.loads(out)["supervisor"]["calls"] == 1


def test_a_half_written_line_is_skipped_not_fatal():
    """A transcript is appended to while it is read, so its last line can be half a JSON object. A
    measurement that dies on that is a measurement nobody can run against a live machine."""
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        proj = root / "repo-1"
        proj.mkdir()
        good = json.dumps(call({"id": "m1", "input_tokens": 1_000, "cache_read_input_tokens": 9_000,
                                "cache_creation_input_tokens": 0, "output_tokens": 300}))
        (proj / "sup.jsonl").write_text(good + '\n{"sessionId": "s1", "type": "assi')
        out = subprocess.run(
            [sys.executable, SCRIPT, "--projects", str(root),
             "--role-of", "sup.jsonl=supervisor", "--format", "json"],
            capture_output=True, text=True, check=True).stdout

        assert json.loads(out)["supervisor"]["calls"] == 1


def test_the_text_path_runs_too():
    """A json-only test once hid a crash on a scalar section (plan 01, task 1). Run what a person types."""
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        proj = root / "repo-1"
        proj.mkdir()
        write_transcript(proj / "sup.jsonl", [
            call({"id": "m1", "input_tokens": 1_000, "cache_read_input_tokens": 9_000,
                  "cache_creation_input_tokens": 0, "output_tokens": 300}),
        ])
        result = subprocess.run(
            [sys.executable, SCRIPT, "--projects", str(root), "--role-of", "sup.jsonl=supervisor"],
            capture_output=True, text=True, check=True)

        assert "== supervisor ==" in result.stdout
        assert "inherited_share" in result.stdout


def test_it_refuses_a_projects_dir_that_does_not_exist():
    """A harness that cannot find what it measures REFUSES (CLAUDE.md decision 20). Printing `0 calls`
    for a mistyped path is how a plan gets certified against nothing."""
    r = subprocess.run(
        [sys.executable, SCRIPT, "--projects", "/nope/nowhere"],
        capture_output=True, text=True)

    assert r.returncode != 0
    assert "does not exist" in (r.stderr + r.stdout)
