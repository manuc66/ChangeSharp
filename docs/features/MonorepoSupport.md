# Multi-Team Monorepo Support (Step 17)

ChangeSharp is evolving to support large-scale enterprise monorepos where multiple teams manage different services within the same repository.

## 🏗️ Hierarchical Configuration

The current `changesharp.json` is global. To support monorepos, ChangeSharp will implement a hierarchical lookup strategy:

1.  **Local Config**: ChangeSharp looks for `changesharp.json` in the current directory.
2.  **Parent Lookup**: If not found, it traverses up the directory tree until it finds a configuration file.
3.  **Overrides**: A configuration file in a subdirectory can override specific settings of a parent configuration (e.g., custom categories or SemVer policies).

## 📂 Scoped Fragments

In a monorepo, having a single `.changesharp/unreleased/` directory at the root leads to noise.

-   **Proposed Strategy**: Each project or service can have its own `.changesharp/` folder.
-   **Command Scope**:
    -   `changesharp status`: When run from the root, it aggregates all fragments from all subdirectories.
    -   `changesharp status --scope ./services/billing`: Only shows fragments and version impacts for the billing service.

## 📑 Service-Specific Changelogs

Teams often want separate changelogs for separate artifacts.

```json
{
  "Packages": [
    {
      "Name": "BillingService",
      "Path": "./services/billing",
      "ChangelogPath": "./services/billing/CHANGELOG.md",
      "FragmentSource": "./services/billing/.changesharp/unreleased"
    }
  ]
}
```

## ⚖️ Independent Policies

Different teams have different tolerances for version bumps.

-   **Team A (API)**: `Changed` -> `Major` (Strict SemVer).
-   **Team B (Internal Tool)**: `Changed` -> `Minor` (Agile/Pragmatic).

Hierarchical configuration allows Team B to override the `SemverPolicy` without affecting Team A.

## 🚀 Benefits for Enterprise

-   **Reduced Noise**: Developers only see fragments relevant to their service.
-   **Ownership**: Teams have full control over their own release notes and versioning cadence.
-   **Centralized Governance**: Global policies (like security categories) can still be enforced from the root configuration.
