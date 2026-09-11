# ledger-sampler — install

Records the ledger counts of every orchestration on a timer, so token spend can be divided by what
was delivered. It runs BESIDE the app: no build, no deploy, no restart of a live orchestration.

Why a sampler rather than an event emitted by the app: measured 2026-09-10 on the VPS, no
orchestration's `PLAN.md` is under git, the file is rewritten in place, and `orchestrator.log.jsonl`
never records a line closing — so the history does not exist and cannot be recovered. An event in
the app would be exact to the minute and would cost a deploy that restarts six live orchestrations.
Cost per delivered item does not need the minute.

## Install (no sudo, no root)

The machine's convention is systemd timers (`aiorch-backup.timer`, `aiorch-canary.timer`), so this
is a systemd **user** timer. It runs without a login session because lingering is already enabled
for `orch` (`loginctl show-user orch -p Linger` → `Linger=yes`).

```sh
install -Dm755 tools/ledger-sampler/sample_ledger.py ~/.local/bin/sample_ledger.py
install -Dm644 deploy/ledger-sampler/aiorch-ledger-sampler.service ~/.config/systemd/user/
install -Dm644 deploy/ledger-sampler/aiorch-ledger-sampler.timer   ~/.config/systemd/user/
python3 ~/.local/bin/sample_ledger.py --self-test        # refuses to be trusted untested
systemctl --user daemon-reload
systemctl --user enable --now aiorch-ledger-sampler.timer
systemctl --user list-timers aiorch-ledger-sampler.timer
```

## Read it

```sh
python3 ~/.local/bin/sample_ledger.py --report
python3 ~/.local/bin/sample_ledger.py --report --since 2026-09-11 --until 2026-09-12
```

The first sample in a window is the baseline, so a window containing one sample reports zero
closed. **Sampling began when this was installed; nothing before that is recoverable.**

## Remove it

```sh
systemctl --user disable --now aiorch-ledger-sampler.timer
rm -f ~/.config/systemd/user/aiorch-ledger-sampler.{service,timer} ~/.local/bin/sample_ledger.py
rm -rf ~/.aiorch-ledger-samples          # the samples themselves
systemctl --user daemon-reload
```

Nothing in the app reads any of it, so removal cannot break anything that is running.
