# Telemetry Aggregation & Downsampling Subsystem Architecture

This document details the architectural design, metric classification semantics, windowing, idempotency, storage design, and worker behavior for the **Telemetry Aggregation & Downsampling Subsystem** implemented in Stage 08-05.

---

## 1. Architectural Overview

The Telemetry Aggregation & Downsampling subsystem transforms high-volume raw telemetry records (`TelemetryHistoryRecord` in PostgreSQL) into reliable, query-efficient, time-series aggregates (`TelemetryAggregateRecord`) across multiple UTC-aligned time granularities (`1m`, `5m`, `1h`, `1d`).

### Data Lifecycle
$$\text{Raw Telemetry } (30s) \longrightarrow \text{1m Aggregates} \longrightarrow \text{5m Rollups} \longrightarrow \text{1h Rollups} \longrightarrow \text{1d Rollups}$$

- **Authoritative Raw Telemetry:** `TelemetryHistoryRecord` remains the immutable source of truth. Aggregates are purely derived representations and raw telemetry is never mutated or deleted by the aggregation layer.
- **Granularities:**
  - `1m`: Computed directly from raw `TelemetryHistoryRecord` entries.
  - `5m`: Computed by rolling up `1m` aggregate records.
  - `1h`: Computed by rolling up `5m` aggregate records.
  - `1d`: Computed by rolling up `1h` aggregate records.

---

## 2. Metric Classification & Aggregation Semantics

Each metric in `TelemetryModel` is classified and aggregated according to its physical semantics:

| Metric Name | Type / Semantics | Unit | Aggregation Operations | Reset & Null Behavior |
|---|---|---|---|---|
| `Cpu` | Instantaneous Gauge | Percentage (`0-100%`) | Min, Max, Avg, P50, P95, P99 | Sample-weighted mean for rollups; 0 is valid. |
| `Ram` | Instantaneous Gauge | MB | Min, Max, Avg, P50, P95, P99 | Sample-weighted mean for rollups; >= 0. |
| `Uptime` | Monotonic Cumulative Counter | Seconds | Delta ($\Delta = \text{last} - \text{first}$) | If $\text{current} < \text{previous}$, reboot/reset detected and $\Delta = \text{current}$. |
| `TotalLaunches` | Monotonic Cumulative Counter | Count | Delta ($\Delta = \text{last} - \text{first}$) | Reset detected if counter drops. |
| `TotalCrashes` | Monotonic Cumulative Counter | Count | Delta ($\Delta = \text{last} - \text{first}$) | Reset detected if counter drops. |
| `TotalRestarts` | Monotonic Cumulative Counter | Count | Delta ($\Delta = \text{last} - \text{first}$) | Reset detected if counter drops. |
| `RunningGameCpu` | Game Process Gauge | Percentage (`0-100%`) | GameCpuAvg, GameCpuMax | Computed only over samples where active game present. |
| `RunningGameRam` | Game Process Gauge | MB | GameRamAvg, GameRamMax | Computed only over samples where active game present. |
| `RunningGameDuration` | Process Monotonic Counter | Seconds | GameDurationMax | Max elapsed duration during window. |
| `RunningGameName` | Categorical / Metadata | String | PrimaryGameName | Mode (most frequent active game in window). |

---

## 3. Window Boundary Semantics

All time calculations use strictly UTC-aligned boundaries $[start, end)$:
- **1-Minute (`1m`):** `[HH:MM:00, HH:MM+1:00)`
- **5-Minute (`5m`):** `[HH:MM:00, HH:MM+5:00)` (where MM is a multiple of 5)
- **1-Hour (`1h`):** `[HH:00:00, HH+1:00:00)`
- **1-Day (`1d`):** `[YYYY-MM-DD 00:00:00, YYYY-MM-DD+1 00:00:00)`

### Partial Windows
Each aggregate record stores `IsPartial` and `SampleCount`. If fewer samples than the expected frequency arrive during a window (e.g. workstation shut down mid-minute), `IsPartial = true` is set, preserving sample count integrity without fabricating missing data.

---

## 4. Idempotency, Concurrency & Batch Merging

- **Unique Compound Key:** `(WorkstationId, Granularity, WindowStart)` is enforced via a unique index in PostgreSQL table `TelemetryAggregateRecords`.
- **Weighted Batch Merge:** When late telemetry arrives or when incremental batches touch an existing window, `TelemetryAggregateRepository.SaveAggregatesBatchAsync`:
  - Merges sample counts: $\text{SampleCount}_{\text{new}} = \text{SampleCount}_{\text{existing}} + \text{SampleCount}_{\text{batch}}$
  - Recalculates weighted gauge averages:
    $$\text{CpuAvg} = \frac{\text{CpuAvg}_{\text{existing}} \times N_{\text{existing}} + \text{CpuAvg}_{\text{batch}} \times N_{\text{batch}}}{N_{\text{existing}} + N_{\text{batch}}}$$
  - Accumulates counter deltas ($\Delta_{\text{total}} = \Delta_{\text{existing}} + \Delta_{\text{batch}}$).
  - Updates Min/Max bounds ($\text{CpuMin} = \min(\text{CpuMin}_{\text{existing}}, \text{CpuMin}_{\text{batch}})$).
- **EF Core Local Change Tracker Check:** Checks EF Core's local context (`Local`) before querying the database, preventing duplicate entity tracking or key collision exceptions during batch processing.

---

## 5. Durable Checkpointing & Background Worker

`TelemetryAggregationWorker` is a background hosted service (`BackgroundService`) executing periodic sweeps controlled by `TelemetryAggregationOptions`:
- **Checkpoint Entity:** `TelemetryAggregationCheckpoint` stores `LastProcessedWindowEnd` and `LastProcessedServerTimestamp` per granularity (`1m`, `5m`, `1h`).
- **Restart Safety:** On restart, the worker resumes execution cleanly from the last persisted checkpoint timestamp.
- **Observability Instrumentation:** Emits OpenTelemetry metrics via `Meter("Sayra.Backend.Telemetry")`:
  - `telemetry_aggregation_runs_total`
  - `telemetry_aggregation_errors_total`
  - `telemetry_aggregate_rows_written_total`
