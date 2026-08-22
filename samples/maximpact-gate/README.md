# MaxImpact Gate — Sample Workspace

This sample shows ChangeSharp's **max-impact cap**: a team policy that refuses
fragments (and releases) whose declared impact would force a **Major** bump
unless an author explicitly opts in.

It is the mirror of the `--api-min-level` floor gate: `--api-min-level` says
*fragments must not be lower than the real API impact*; `MaxImpact` says *fragments
must not bump higher than the team allows*.

## Configuration

`changesharp.json` in this folder sets `SemverPolicy.MaxImpact` to `minor`:

```json
{
  "SemverPolicy": {
    "MaxImpact": "minor"
  }
}
```

Impact levels supported: `major` (default, no cap), `minor`, `patch`.

## What is enforced, and where

| Step | Behavior |
| --- | --- |
| `changesharp new --breaking` | Refused at creation (exit 3) — the author must use `--allow-major` |
| `changesharp new` (interactive) | Blocked categories are marked `⚠ blocked (MaxImpact)` in the menu and re-prompted |
| `changesharp validate` | Does **not** enforce the cap (format check only) |
| `changesharp release` | Refused without `--allow-major` — the production gate (human-in-the-loop) |
| `changesharp release --allow-major` | Proceeds, deliberate Major recorded |

## Run the demo

```bash
samples/maximpact-gate/run-demo.sh
```

The script builds the ChangeSharp CLI from the repo and asserts the exit codes
for every scenario above. It is self-cleaning (no files are left behind).

## Notes

- With the default `MaxImpact: major` the cap is disabled and behavior is
  unchanged, so existing projects are unaffected until they opt in.
- The gate is enforced by ChangeSharp itself: no external API-diff tool is
  involved, unlike `--api-min-level` which receives its level from CI.