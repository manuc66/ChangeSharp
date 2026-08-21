# Enterprise Security & Approval Gates

To ensure that AI agents and automated systems do not trigger unauthorized production releases, ChangeSharp implements strict security gates. This is particularly critical when using the MCP (Model Context Protocol) integration.

## 🛡️ Release Safety Philosophy

In an enterprise environment, the tool that calculates the version should not necessarily be the one that has the permission to push the final artifacts without human oversight.

ChangeSharp enforces this via two primary mechanisms:

### 1. Mandatory Dry-Run (`--dry-run`)

The `perform_release` tool in the MCP server and the `release` command in the CLI support a dry-run mode. 

- **Behavior**: It performs all calculations (version derivation, fragment aggregation) but **does not modify any files** and **does not push any tags**.
- **Output**: It returns a machine-readable (JSON) and human-readable summary of what *would* happen.
- **Enforcement**: In high-security environments, the MCP server can be configured to *only* allow dry-runs, forcing the actual release to be performed by a human or a specialized CI runner.

### 2. Approval Enforcement (`--require-approval`)

When running in an automated environment, ChangeSharp can be set to require an explicit approval token or flag.

- **MCP Integration**: The `perform_release` tool will fail if an AI agent attempts to run it without the `dryRun: true` parameter, unless an environment variable `CHANGESHARP_ALLOW_UNSAFE_RELEASE=true` is explicitly set in the server configuration.
- **Human-in-the-loop**: The recommended workflow is for the AI agent to propose a release via `dry-run`, and then a human triggers the final pipeline step in the CI/CD UI (e.g., GitHub Actions environment approval).

### 3. Protected CI Environment with Required Reviewers (Recommended)

A `workflow_dispatch` trigger alone is not a real review — clicking "Run workflow" does not force anyone to look at what will be released. To make the human-in-the-loop gate meaningful on GitHub, use a **protected environment** with **required reviewers**, split into two jobs:

1.  `prepare` — runs on **every push to `main`** and on manual dispatch. It computes the release plan (next version + aggregated changelog segment) and surfaces it:
    - on push: as the run's **job summary** (always visible in Actions);
    - on manual dispatch: as a **commit comment** with the full plan.
2.  `release` — **manual only** (`workflow_dispatch`), `environment: release` with **required reviewers**, and `needs: prepare`. The run pauses at the environment gate; a human reviews the plan comment and explicitly **approves** before release + NuGet push + tag + GitHub release happen.

GitHub setup (one time): **Settings → Environments → New environment → `release` → Protection rules → Required reviewers** (add yourself/team). A dispatch run with fragments then waits for your approval.

## 🧩 AI Agent Workflow with Gates

1.  **Agent**: "I've finished the feature. I'll prepare a release."
2.  **Agent**: Calls `perform_release(dryRun: true)`.
3.  **ChangeSharp**: Returns: "Next version: 1.2.0. Changes: Added X, Fixed Y."
4.  **Agent**: "The release is ready. Please approve the release of version 1.2.0 in the CI pipeline."
5.  **Human**: Reviews the changelog and clicks "Approve" in GitHub/GitLab.

## ⚙️ Configuration

Security gates can be configured in `changesharp.json`:

```json
{
  "Security": {
    "RequireApproval": true,
    "AllowAgentRelease": false,
    "DryRunByDefault": true
  }
}
```

- `DryRunByDefault`: If true, `changesharp release` without `--dry-run` defaults to dry-run (CLI only).
- `RequireApproval` / `AllowAgentRelease`: Gates the MCP `perform_release` tool. If `RequireApproval` is true **or** `AllowAgentRelease` is false, any non-dry-run release request is rejected unless the environment variable `CHANGESHARP_ALLOW_UNSAFE_RELEASE=true` is set.

> Note: the CLI `release` command does not read `RequireApproval`/`AllowAgentRelease`. It only honors the explicit `--require-approval` flag (same `CHANGESHARP_ALLOW_UNSAFE_RELEASE` env var). To gate CLI releases via config, set `DryRunByDefault` and rely on a human or CI runner to perform the final release.
