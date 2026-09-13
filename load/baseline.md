# Phase 0 load baseline (2026-09-14)

Environment: source-run stack (`dotnet run`, Debug), freshly reseeded databases
(12 products, product 1 stock 38, seeded orders), Windows, k6 2.2.0
closed-model constant-VUs (binary at `artifacts/tools/k6/`, not committed -
install from the grafana/k6 GitHub releases; `scripts/run-load.ps1` expects it
there). Each level runs 20s. `rps` counts HTTP requests, so an orders-write
iteration (POST + DELETE) is roughly two requests.

Cold start: 3.4s from process start to first 200 on `/` in this session
(warm OS file caches after many runs). Request-log durations for the
first dashboard render earlier in the same session ran 10-13.3s. Probe
sizing in later phases should use ~13s, not 3.4s.

## Results

See `baseline-results.md` for the full matrix and `results/` for raw k6
summaries per run.

| scenario | shape | ceiling observed |
|---|---|---|
| dashboard (full path) | 5/25/50 VUs | ~190-208 rps, flat - saturates around 200 |
| products page (full path) | 5/25/50 VUs | ~570-650 rps |
| catalog list (direct) | 5/25/50 VUs | ~410-640 rps |
| orders list (direct) | 5/25/50 VUs | ~540-690 rps |
| orders write (direct, POST+DELETE) | 2/8/16/32 VUs | peaks ~10 req/s at 8 VUs, collapses at 16+ |

Read failures: zero at every level; dashboard degradation message: never
triggered at 50 VUs. p95 grows with VUs on every scenario (queueing), and
throughput flattens or drops as VUs rise - per-request cost grows under
contention, so capacity claims must name the concurrency level, not just a
peak number.

## Write ceiling and failure modes (Phase 2 must beat these)

- Throughput: ~7.9 req/s at 2 VUs, ~9.7 at 8 VUs, then collapse: ~5.0 at
  16 VUs, ~6.0 at 32 VUs. Failures: 0% at 2 VUs, 0.5% at 8, 3.9% at 16,
  10% at 32.
- `SQLite Error 5: 'database is locked'` on Orders: unhandled 500s after
  6-15s stalls (the 5-second busy timeout plus queue depth). This is the
  single-writer ceiling from the plan, measured.
- `SQLite Error 19: 'UNIQUE constraint failed: ReservationKeys.Key'` on
  Catalog: a reserve that committed after the caller's timeout was retried
  by Orders with the same reservation key, hit the key table's primary key,
  and surfaced as an unhandled 500. Live evidence for Phase 2 step 2's
  violation-to-replayed catch - it is not hypothetical.
- One `502 CatalogUnavailable` in ~8.6s: CatalogClient's 3s timeout times
  retry budget exhausted under contention - the degradation path working as
  designed.

## Concurrent-reserve sweep

`scripts/test-concurrent-reserve.ps1`, results in `concurrent-reserve.md`:

- Invariant (no oversell, no negative stock, stock == N - successes):
  PASS at every N tested (8 through 256).
- Goal (exactly N of N succeed): PASS through N=64; FAIL at N=128
  (118/128, ten 500s) and N=256 (235/256, twenty-one 500s). SQLite's busy
  timeout breaking point is between 64 and 128 parallel reserves on this
  box.
- The xunit fact `ConcurrentReserves_NeverOversell_AllSucceedAtLowN`
  (N=8) pins the invariant and the goal at a level safely below the
  breaking point; suite is 11/11.

## What Phase 2 is sized against

Raise the write ceiling from ~10 req/s and the busy-breaking N from
~64-128, keep the invariant exactly as green as it is on SQLite, and turn
the ReservationKeys violation into `replayed: true` instead of a 500.
