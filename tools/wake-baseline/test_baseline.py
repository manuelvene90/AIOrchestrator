import json, pathlib, subprocess, sys, tempfile

def test_counts_wakes_by_cause():
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        ch = root / "repo-1"; ch.mkdir()
        (ch / "owner-channel.md").write_text(
            "## [1] FROM owner — 2026-09-01 10:00 — hello\nbody\n\n"
            "## [2] FROM app — 2026-09-01 10:05 — [agent] turn_ended\nbody\n\n"
            "## [3] FROM supervisor — 2026-09-01 10:06 — ack\nbody\n")
        (ch / "imp-1").mkdir()
        (ch / "imp-1" / "channel.md").write_text(
            "## [1] FROM implementer — 2026-09-01 10:07 — TASK 1 done\nbody\n")
        out = subprocess.run(
            [sys.executable, "tools/wake-baseline/baseline.py", "--root", str(root), "--format", "json"],
            capture_output=True, text=True, check=True).stdout
        got = json.loads(out)
        assert got["entries_by_author"]["owner"] == 1
        assert got["entries_by_author"]["app"] == 1
        assert got["entries_by_author"]["implementer"] == 1
        # Under the watcher every author wakes the supervisor, the app's own bookkeeping included.
        assert got["wake_causes_under_watcher_measured"]["owner"] == 1
        assert got["wake_causes_under_watcher_measured"]["member"] == 1
        assert got["wake_causes_under_watcher_measured"]["app"] == 1
        # Under the app's policy the bookkeeping wakes nobody. This is the figure the series moves.
        assert got["wake_causes_under_ticket_MODELLED"]["owner"] == 1
        assert got["wake_causes_under_ticket_MODELLED"]["member"] == 1
        assert got["wake_causes_under_ticket_MODELLED"]["app"] == 0
        assert got["wakes_the_ticket_would_avoid_MODELLED"] == 1


def test_text_output_renders_every_section_including_the_scalar():
    """The text path is what a person reads, and it was crashing on the scalar section while the
    json-only test stayed green."""
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        ch = root / "repo-1"; ch.mkdir()
        (ch / "owner-channel.md").write_text(
            "## [1] FROM owner — 2026-09-01 10:00 — hello\nbody\n\n"
            "## [2] FROM app — 2026-09-01 10:05 — [agent] turn_ended\nbody\n")
        done = subprocess.run(
            [sys.executable, "tools/wake-baseline/baseline.py", "--root", str(root)],
            capture_output=True, text=True)
        assert done.returncode == 0, done.stderr
        assert "wake_causes_under_watcher_measured" in done.stdout
        assert "wakes_the_ticket_would_avoid_MODELLED == 1" in done.stdout.replace("== wakes_the_ticket_would_avoid_MODELLED ==", "wakes_the_ticket_would_avoid_MODELLED ==")
