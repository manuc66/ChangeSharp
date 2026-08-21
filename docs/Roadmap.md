# Roadmap to Production

To build a reliable MVP, we will prioritize robust parsing, extensible configuration, and developer experience.

### Step 1: Create the CLI (.NET Tool) & Error Contract (Completed)
* Initialize `ChangeSharp.Cli` as a console application configured as a packable tool (`<PackAsTool>true</PackAsTool>`).
* Set up command handling with `System.CommandLine`.
* **Define Exit Codes Contract**: Establish stable exit codes (e.g., `0` for success, `1` for generic error, `2` for no changes to release). This is critical for CI/CD integration.
* See [Exit Codes Specification](ExitCodes.md).

### Step 2: Integrate Markdig (Early Parsing Layer) (Completed)
* Replace the manual line-by-line parser with **Markdig**.
* This guarantees correct handling of rich Markdown elements (nested lists, code blocks, bold text) inside fragment sections right from the start, avoiding complex edge-case bug fixing later.

### Step 3: Fragment Lifecycle & UX (Workflow) (Completed)
* **Initialize** (`changesharp init`): Sets up the configuration file and the `.changesharp/unreleased/` directory.
* **Status** (`changesharp status`): Lists pending fragments, previews the calculated version bump, and shows config state. This feeds into `--dry-run` and MCP integration.
* **New Fragment** (`changesharp new`): 
    * Support interactive prompts for message and category (high-priority UX).
    * **Naming Strategy**: Implement a deterministic naming convention (e.g., `YYYYMMDD-slug.md`) to ensure sorting and avoid collisions. See [Fragment Naming Strategy](FragmentNaming.md).
* **Release** (`changesharp release`):
    * **Idempotence**: Implement a transaction-like strategy or state detection to handle crashes mid-release (e.g., after updating CHANGELOG but before archiving fragments).
    * Aggregate fragments, derive version, update targets, and archive consumed fragments.

### Step 4: Config, Propagation & SemVer Policy (Completed)
* Provide standard target handlers (MSBuild, JSON, text/regex) via `changesharp.json`.
* **SemVer Mapping**: Document and allow override of the `Changed` → `Minor` policy (updated from `Major` based on user feedback to prevent accidental breaking bumps).
* **Principle**: Step 4 ensures the tool is extensible via configuration rather than hardcoded logic.

### Step 5: Dry-run Mode (Completed)
* Add `--dry-run` support to the `release` command.
* Allow users to preview the next version, aggregated changes, and affected version targets without making actual changes to the filesystem.

### Step 6: GitHub & GitLab CI/CD Integration (Completed)
* Created sample workflow files (`release.yml` for GitHub, `.gitlab-ci.yml` for GitLab) to automate the release process.
* **Focus**: Enable a standard "Release on Merge" or "Release on Tag" pipeline that is easy to adopt for DevOps engineers.
* Added `--next-only` and `--require-fragments` CLI options for better CI integration.

### Step 7: Pre-release Channels and Branch-based Versioning (Completed)
* See detailed specification: [Pre-release Feature](features/Prereleases.md)

### Step 8: Smart Init (Auto-discovery) (Completed)
* Enhance the `init` command to scan the workspace for common project files (e.g., `.csproj`, `package.json`, `Directory.Build.props`).
* Automatically suggest and add these as `VersionTargets` in `changesharp.json`.
* **Continuous Discovery**: `init` is now additive and can be rerun at any time to discover and add new projects added to the repository after the initial setup. `status` also checks for untracked components and warns the user.
* See [Continuous Discovery Specification](features/ContinuousDiscovery.md).
* **Frictionless Merge**: Strategy implemented to avoid conflicts on management files using isolated fragments and deferred versioning. See [Frictionless Merge Workflow](FrictionlessWorkflow.md).

### Step 9: Fragment Validation and Linting (Completed)
* Add a `lint` or `validate` command to check if unreleased fragments follow the expected Markdown structure.
* This must precede custom category support to ensure the validation engine is extensible.

### Step 10: Custom Categories and Mappings (Completed)
* Allow users to define custom changelog categories in `changesharp.json`.
* Enable mapping these custom categories to specific SemVer impacts (Major, Minor, or Patch).
* See [Custom Categories Specification](features/CustomCategories.md).

### Step 11: AI Automation Layer (CLI + MCP) (Completed)

#### Goal
Make ChangeSharp fully automation-ready for CI/CD systems and AI agents without adding complexity to the core system.
* Implemented a lightweight MCP server in `ChangeSharp.Mcp`.
* Exposes tools for status, fragment creation, validation, and release.
* See [MCP Integration Documentation](McpIntegration.md).

---

#### Approach
ChangeSharp uses a **two-layer automation model**:

**1. CLI (primary interface)**
* JSON-first output (`--json`)
* deterministic, machine-readable results
* used by CI/CD and scripts
* contains all business logic via the core library

**2. MCP (AI adapter layer)**
* lightweight STDIO-based MCP server
* no business logic duplication
* acts as a thin wrapper over the CLI or core library
* exposes ChangeSharp features to AI agents (Cursor, Copilot, Claude, etc.)

---

#### Architecture
```
ChangeSharp.Core   → business logic
        ↑
ChangeSharp.CLI    → JSON-first automation interface (Source of Truth)
        ↑
ChangeSharp.MCP    → AI/tooling adapter (Interface only)
```

---

#### Key Principle
> **CLI is the source of truth for automation.**
> **MCP is only an interface layer for AI agents.**

---

### Step 12: Self-hosting & Dogfooding (Completed)
* Implement `CHANGELOG.md` for ChangeSharp using ChangeSharp itself.
* Successfully performed the first release (v1.0.0) using the tool.
* Symbolically important for a tool promoting this practice.

### Step 13: ChangeSharp Bot for Pull Requests (Priority 2)
* **Goal**: Ensure fragment quality and provide visibility into release impact during the PR phase.
* **Phase 1: CI-Native Bot (GitHub Actions/GitLab CI) (Completed)**:
    * Implemented a lightweight script/action that runs `changesharp validate`.
    * Post PR comments using standard CI tokens. See `samples/ci/github-bot.yml`.
    * No external infrastructure required.
* **Phase 2: Full ChangeSharp App (Marketplace)**:
    * Build a dedicated GitHub App / GitLab Webhook service.
    * Support OAuth, webhook signature validation, and multi-repo management.
    * Provide a seamless "Install" experience from the marketplace.
* See [CI/CD Integration Contract](features/CiIntegration.md).

### Step 14: Enterprise Security & Approval Gates (Completed)
* **Dry-run enforcement**: Implement a mandatory `--dry-run` or `--require-approval` flag for MCP `perform_release` tool.
* Ensure AI agents cannot trigger a production release without an explicit human gate in the loop.
* See [Enterprise Security & Approval Gates](features/ApprovalGates.md).

### Step 15: Repository Intelligence for Agents (Priority 2)
* **Goal**: Allow AI agents to understand what has changed in the codebase since the last release, even if fragments are missing.
* **Features**:
    * `query_changes`: A tool to compare the current state with the last tag/release and identify modified files/functions.
    * **Fragment Gap Analysis**: Automatically suggest missing fragments based on uncommitted or unreleased code changes.
    * Summarize "What's New" from both a technical (Git) and user (Changelog) perspective.

### Step 16: Syntactic API Validation (Safety Gate) (Completed)
* **Goal**: Ensure the promised SemVer impact in fragments matches the reality of the code changes.
* **Approach**: Provide a `--api-min-level` flag on `validate` and `release`. The CI pipeline runs an API diff tool of its choice (e.g., `PublicApiAnalyzers`, Swagger diff) and passes the minimum impact level to ChangeSharp. ChangeSharp compares it against the fragments' declared categories and fails if they are too low.
* **CLI**:
  ```
  changesharp validate --api-min-level minor   # PR gate
  changesharp release --api-min-level major    # release gate
  ```
* **Full decoupling**: ChangeSharp does not perform the diff — the team owns the diff tool. ChangeSharp only enforces the policy.
* See [API Surface Gate Specification](features/ApiSurfaceGate.md).

### Step 17: Multi-Team Monorepo Scoping (Priority 2)
* **Goal**: Support large-scale enterprise monorepos with independent team policies.
* **Position**: ChangeSharp aims to support enterprise monorepos. To avoid friction between teams, we will implement a scoped model where `changesharp.json` can exist in sub-directories, defining independent versioning policies and changelogs for that specific scope.
* **Actions**:
    * **Config Hierarchies**: Allow `changesharp.json` to be placed in subdirectories, overriding global settings for that scope.
    * **Scoped Fragments**: Support `.changesharp/` directories per service/sub-project.
    * **Service-Specific Changelogs**: Generate independent `CHANGELOG.md` files for different parts of the repository.
    * **Policy Overrides**: Team A might want `Changed -> Major` while Team B wants `Changed -> Minor`.
* See [Multi-Team Monorepo Support](features/MonorepoSupport.md).

### Step 18: Migration & Enterprise Adoption (Priority 1)
* **Goal**: Provide a clear path for teams moving from GitVersion/MinVer to ChangeSharp.
* **Actions**:
    * Create a dedicated [Migration Guide](Migration.md).
    * Add "Compatibility Modes" (e.g., auto-import fragments from Git trailers if missing).
    * Develop a "Migration Script" to convert existing changelogs into ChangeSharp fragments (reverse release).

### Step 19: Concrete Metrics & Benchmarks (Priority 3)
* **Goal**: Prove the value proposition with data.
* **Actions**:
    * Conduct benchmarks on merge conflict reduction in multi-developer teams.
    * Track "Version Accuracy" (declared vs. actual API impact) using the Syntactic Safety Gate.

---

### Future Expansion: Polyglot Support
*To maintain focus on the core .NET enterprise experience, broader polyglot features are isolated here.*
* **Step 20: Native Handlers**: Implement `pyproject.toml` (Python), `Cargo.toml` (Rust), and `pom.xml` (Java) handlers.
* **Step 21: Universal Plugin System**: Allow external scripts to act as version targets.
