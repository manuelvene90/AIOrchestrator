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


# ---------------------------------------------------------------------------
# The boot read: how much of what a session reads at startup is the app's own bookkeeping.
# ---------------------------------------------------------------------------

BASELINE = "tools/wake-baseline/baseline.py"


def run(root, *extra):
    """Runs the script and returns (completed process, parsed json or None)."""
    done = subprocess.run(
        [sys.executable, BASELINE, "--root", str(root), *extra],
        capture_output=True, text=True)
    return done, (json.loads(done.stdout) if done.returncode == 0 and "--format" in extra else None)


def write(path, *blocks):
    """Writes the blocks joined, and returns the BYTE length of each block plus the file's own."""
    text = "".join(blocks)
    path.write_text(text, encoding="utf-8")
    return [len(block.encode("utf-8")) for block in blocks], len(text.encode("utf-8"))


def test_counts_the_bookkeeping_a_boot_read_no_longer_carries():
    """The 17 routed kinds, by entry and by byte, out of a live channel.

    The fixture carries one of each near-miss on purpose: an untagged app entry (owner-facing, never
    routed whatever its kind), and an agent-tagged app entry that is not one of the 17.
    """
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        ch = root / "repo-1"; ch.mkdir()

        turn_ended = "## [1] FROM app — 2026-09-01 10:00 — [agent] turn_ended sup turn 1 — success\noutcome: success\n\n"
        ledger = "## [2] FROM app — 2026-09-01 10:01 — [agent] PLAN.md is behind your verdicts\nupdate it\n\n"
        started = "## [3] FROM app — 2026-09-01 10:02 — orchestration 'repo-1' started\nbody\n\n"
        unread = "## [4] FROM app — 2026-09-01 10:03 — [agent] unread traffic — you have not answered\nbody\n\n"
        deaf = "## [5] FROM app — 2026-09-01 10:04 — [agent] imp-1 may be deaf to wakes\nnudged, silent\n"

        sizes, _ = write(ch / "owner-channel.md", turn_ended, ledger, started, unread, deaf)
        done, got = run(root, "--format", "json")

        assert done.returncode == 0, done.stderr
        # Owner-facing app entries are never routed, and an agent-tagged note that is not one of the
        # 17 kinds is not either. `may be deaf to wakes` IS one — the plan's marker list said
        # "has gone deaf", which no writer produces.
        assert got["bookkeeping_out_of_the_boot_read_MODELLED"] == 3
        assert got["boot_read_bookkeeping_MODELLED"]["entries"] == 3
        # Hand-counted from the fixture's own blocks: entries 1, 2 and 5.
        assert got["boot_read_bookkeeping_MODELLED"]["bytes"] == sizes[0] + sizes[1] + sizes[4]


def test_the_archive_is_out_of_the_boot_read_and_in_the_wake_counts():
    """The two readings disagree on the archive ON PURPOSE, and each says which it is.

    A booting session reads the live file (`kit/skills/supervisor/SKILL.md:131`), so archived bytes
    are not in its boot read. The wake counts span both, because an archived entry still woke
    somebody when it was written (CLAUDE.md decision 13).
    """
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        ch = root / "repo-1"; ch.mkdir()

        _, live_bytes = write(
            ch / "owner-channel.md",
            "## [9] FROM app — 2026-09-01 10:00 — [agent] turn_ended sup turn 9 — success\nbody\n")
        _, archived_bytes = write(
            ch / "owner-channel.archive.md",
            "## [1] FROM app — 2026-08-30 09:00 — [agent] turn_ended sup turn 1 — success\nbody\n")

        done, got = run(root, "--format", "json")

        assert done.returncode == 0, done.stderr
        assert got["entries_by_author"]["app"] == 2                                  # both wakes
        assert got["boot_read_bytes_measured"]["live_bytes"] == live_bytes           # live only
        assert got["boot_read_bytes_measured"]["archived_bytes_outside_the_boot_read"] == archived_bytes
        assert got["boot_read_bookkeeping_MODELLED"]["bytes"] == live_bytes          # the archived one is not in it
        assert got["boot_read_bookkeeping_MODELLED"]["entries"] == 1


def test_a_header_quoted_inside_a_closed_fence_is_not_an_entry():
    """One parser, fence-aware — and the byte figures are why it has to be.

    A fence-blind reader does not merely miscount here: it hands the tail of the supervisor's brief
    to `app`, which inflates the bureaucracy share this file exists to measure (CLAUDE.md decision
    12).
    """
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        ch = root / "repo-1"; ch.mkdir()

        brief = ("## [1] FROM supervisor — 2026-09-01 10:00 — brief\n"
                 "What the app wrote to you last night:\n"
                 "```\n"
                 "## [7] FROM app — 2026-08-31 23:00 — [agent] turn_ended sup turn 7 — success\n"
                 "outcome: success\n"
                 "```\n"
                 "Do not answer it.\n\n")
        real = "## [2] FROM app — 2026-09-01 10:05 — [agent] turn_ended sup turn 8 — success\nbody\n"

        sizes, _ = write(ch / "owner-channel.md", brief, real)
        done, got = run(root, "--format", "json")

        assert done.returncode == 0, done.stderr
        assert got["entries_by_author"]["app"] == 1
        assert got["entries_by_author"]["supervisor"] == 1
        assert got["bookkeeping_out_of_the_boot_read_MODELLED"] == 1
        # The quoted lines belong to the SUPERVISOR's span, not to the app's: this is the assertion
        # a fence-blind reader fails on, and it fails on the bytes rather than only on the count.
        assert got["boot_read_bytes_measured"]["live_bytes_authored_by_app"] == sizes[1]


def test_an_unclosed_fence_suppresses_nothing():
    """A stray ``` must not swallow the rest of a channel — only a CLOSED fence suppresses."""
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        ch = root / "repo-1"; ch.mkdir()

        write(ch / "owner-channel.md",
              "## [1] FROM supervisor — 2026-09-01 10:00 — brief\nhere is a payload:\n```json\n{}\n\n",
              "## [2] FROM app — 2026-09-01 10:05 — [agent] turn_ended sup turn 1 — success\nbody\n")

        done, got = run(root, "--format", "json")

        assert done.returncode == 0, done.stderr
        assert got["entries_by_author"]["app"] == 1
        assert got["bookkeeping_out_of_the_boot_read_MODELLED"] == 1


def test_the_live_bytes_account_for_themselves():
    """live_bytes = the bytes inside entries + the bytes outside any entry.

    The preamble is reported rather than absorbed: it is the arithmetic check on every byte figure
    here, and a parser that lost an entry's span would show up as a bulge in it.
    """
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        ch = root / "repo-1"; ch.mkdir()

        preamble = "# channel repo-1\n\nopened 2026-09-01\n\n"
        entry = "## [1] FROM owner — 2026-09-01 10:00 — hello\nbody\n"
        sizes, total = write(ch / "owner-channel.md", preamble, entry)

        done, got = run(root, "--format", "json")
        measured = got["boot_read_bytes_measured"]

        assert done.returncode == 0, done.stderr
        assert measured["live_bytes"] == total
        assert measured["live_bytes_in_entries"] == sizes[1]
        assert measured["live_bytes_outside_any_entry"] == sizes[0]


def test_tokens_are_the_bytes_over_the_declared_ratio():
    """3.87 bytes per token, an [estimate], stated in one place and applied to measured bytes."""
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        ch = root / "repo-1"; ch.mkdir()

        body = "a report with an em dash — and an accented città, which cost more bytes than characters.\n"
        sizes, total = write(
            ch / "owner-channel.md",
            f"## [1] FROM app — 2026-09-01 10:00 — [agent] turn_ended sup turn 1 — success\n{body}\n",
            f"## [2] FROM supervisor — 2026-09-01 10:05 — verdict\n{body}")

        done, got = run(root, "--format", "json")
        tokens = got["boot_read_tokens_ESTIMATE_at_3_87_bytes_per_token"]

        assert done.returncode == 0, done.stderr
        assert tokens["live"] == round(total / 3.87)
        assert tokens["bookkeeping_MODELLED"] == round(sizes[0] / 3.87)
        # BYTES, NOT CHARACTERS. The fixture's em dashes and its `à` make the two differ by design,
        # so a script measuring the decoded string would report `characters` here and be ~4 % under
        # on every channel in this system, which is written in exactly this prose.
        characters = len((ch / "owner-channel.md").read_text(encoding="utf-8"))
        assert characters < total
        assert got["boot_read_bytes_measured"]["live_bytes"] == total


def test_the_key_names_say_which_kind_of_number_each_is():
    """A prediction that calls itself a measurement cost this tool's author a day.

    The suffixes are the contract: `_measured` off the disk, `_MODELLED` through a heuristic,
    `ESTIMATE` through the bytes-per-token ratio. This case exists so a later rename has to be
    deliberate.
    """
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        ch = root / "repo-1"; ch.mkdir()
        write(ch / "owner-channel.md", "## [1] FROM owner — 2026-09-01 10:00 — hello\nbody\n")

        done, got = run(root, "--format", "json")

        assert done.returncode == 0, done.stderr
        assert set(got) == {
            "entries_by_author",
            "wake_causes_under_watcher_measured",
            "wake_causes_under_ticket_MODELLED",
            "wakes_the_ticket_would_avoid_MODELLED",
            "boot_read_bytes_measured",
            "boot_read_bookkeeping_MODELLED",
            "boot_read_tokens_ESTIMATE_at_3_87_bytes_per_token",
            "bookkeeping_out_of_the_boot_read_MODELLED",
        }


def test_it_refuses_a_root_that_is_not_a_directory():
    """CLAUDE.md decision 20: a harness that cannot find what it measures must not report zero."""
    with tempfile.TemporaryDirectory() as d:
        missing = pathlib.Path(d) / "no-such-supervision-home"
        done, _ = run(missing)

        assert done.returncode == 2, done.stdout
        assert "not a directory" in done.stderr


def test_it_refuses_a_root_with_no_channels_in_it():
    """An existing but wrong directory is the likelier mistake, and reads exactly like a quiet machine."""
    with tempfile.TemporaryDirectory() as d:
        root = pathlib.Path(d)
        (root / "repo-1").mkdir()
        (root / "repo-1" / "session.json").write_text("{}")

        done, _ = run(root)

        assert done.returncode == 2, done.stdout
        assert "no channel files" in done.stderr
