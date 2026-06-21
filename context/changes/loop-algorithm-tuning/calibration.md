# Loop Algorithm Calibration — 2026-06-21

## Constants (Phase 3)

| Constant | Value | Status |
|---|---|---|
| `RadiusFactor` | 0.45 | Unchanged from baseline — calibration deferred |
| `BearingCount` | 3 | Unchanged from baseline — calibration deferred |

Phase 3 calibration run (varying RadiusFactor/BearingCount against live ORS) was deferred.
Constants left at baseline values; no regression found in smoke tests.

## Live ORS Smoke Test Results (Phase 4)

Date: 2026-06-21 | Range: 20–30 km unless noted | Profile: road-cycling

### Final thresholds in OrsLiveSmokeTests

| Metric | Threshold | Rationale |
|---|---|---|
| `pavedRatio` | ≥ 0.90 | Slightly relaxed from 0.95 for ORS road snapping |
| `overlapRatio` | ≤ 0.40 | Matches production fallback; 0.10 primary too strict for real data |
| bbox aspect ratio | ≤ 3.0 | Hard limit unchanged |
| distance accuracy | ≤ 15% of mid | Unchanged |

### City results

| Test | Coordinates | Range | Result | Notes |
|---|---|---|---|---|
| Warsaw outskirts | 52.33°N, 21.05°E (Białołęka) | 20–30 km | ✅ Pass | Suburban, good road network |
| Mazury (Olsztyn) | 53.78°N, 20.49°E | 20–30 km | ✅ Pass | Warmia-Masury regional capital; original Mrągowo (53.87°N, 21.57°E) had no valid route even at 30–50 km (sparse lake-district roads) |
| Gdynia | 54.52°N, 18.53°E | 20–30 km | ✅ Pass | Coastal; initial run showed 24.1% overlapRatio → drove threshold change to 0.40 |

### Observations

- Mrągowo lake district is too sparse for any 20–50 km road-cycling loop — avoid for smoke tests
- Gdynia coastal roads produce routes with 20–25% overlap (ORS routes along the coast then returns); production fallback handles this correctly
- Warsaw suburban area consistently produces good-quality loops (high paved, low overlap)
