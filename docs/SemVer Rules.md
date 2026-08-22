# SemVer Derivation Rules

To avoid the pitfalls of using *Keep a Changelog* categories directly for SemVer (where a simple change would trigger a false-positive Major bump), ChangeSharp uses a refined, pragmatic mapping:

| Fragment Section | SemVer Impact | Description / Discipline |
| :--- | :--- | :--- |
| `### Breaking Changes` | ⬆️ **Major** | Explicit breaking changes. Kept as a separate section in the fragment and compiled into the final release notes for clear visibility. |
| `### Removed` | ⬆️ **Major** | Removing a documented public feature is a breaking change. |
| `### Changed` | ➡️ **Minor** | Modifications to existing features. **Note**: By default, ChangeSharp treats "Changed" as a Minor bump to avoid accidental Major bumps. Override to "Major" in `changesharp.json` if needed. |
| `### Added` | ➡️ **Minor** | New backward-compatible features. |
| `### Deprecated` | ➡️ **Minor** | Warnings about future removals. |
| `### Fixed` | 🛞 **Patch** | Bug fixes. |
| `### Security` | 🛞 **Patch** | Security improvements. |

## Customization

You can override these default mappings or define entirely new categories in your `changesharp.json` configuration file. This is useful for internal maintenance, documentation changes, or specific project workflows.

```json
{
  "SemverPolicy": {
    "Mappings": {
      "Breaking Changes": "Major",
      "Added": "Minor",
      "Maintenance": "Patch",
      "Documentation": "None"
    }
  }
}
```

Impact levels supported: `Major`, `Minor`, `Patch`, `None`.

---

## 🛡️ Automated Verification (Safety Gates)

Human error is the main cause of SemVer violations. ChangeSharp provides a **Safety Gate** via the `--api-min-level` flag to cross-verify fragments against actual code changes.

### How it works
The CI pipeline runs an API diff tool of its choice (e.g., `PublicApiAnalyzers`, Swagger diff) and passes the minimum impact level to ChangeSharp:

```bash
changesharp validate --api-min-level minor   # PR gate
changesharp release --api-min-level major    # release gate
```

ChangeSharp compares the required level against the fragments' declared categories:
1. **Extract Expected Impact**: ChangeSharp reads the pending fragments (e.g., `### Added` implies Minor).
2. **Compare**: If the fragments' highest impact is below `--api-min-level`, validation fails.
3. **Fail or Warn**: Use `--api-min-level-warn` to warn instead of failing.

### Integration Examples
*   **Web APIs**: Compare Swagger/OpenAPI schemas before `changesharp validate --api-min-level`.
*   **.NET Libraries**: Use `PublicApiGenerator` to detect signature changes.
*   **CLI Tools**: Compare help output or command schemas.

ChangeSharp does **not** perform the API diff itself — it only enforces the policy. See [ApiSurfaceGate](features/ApiSurfaceGate.md) for details.

## ⛔ Maximum Impact Cap (`SemverPolicy.MaxImpact`)

`--api-min-level` is a **floor**: it guarantees fragments are not lower than the real API impact. `SemverPolicy.MaxImpact` is the symmetric **cap**: it guarantees no fragment silently forces a Major bump when the team does not want one.

```json
{
  "SemverPolicy": {
    "MaxImpact": "minor"
  }
}
```

`MaxImpact` accepts `patch`, `minor`, or `major`. The default is `major`, which disables the cap entirely — existing projects are unaffected until they opt in.

### Per-branch caps (`SemverPolicy.BranchMaxImpact`)

Restrict the allowed impact on specific branches (e.g. hotfix or release branches
that should only accept fixes). The effective cap is the most restrictive of the
global `MaxImpact` and the matching branch entry:

```json
{
  "SemverPolicy": {
    "MaxImpact": "minor",
    "BranchMaxImpact": {
      "release/*": "patch",
      "hotfix/*": "patch"
    }
  }
}
```

On a `release/1.2` branch, an `Added` fragment (Minor) is refused at creation
and at release (exit 3) unless `--allow-major` is passed; a `Fixed` fragment
(Patch) is fine. Branch patterns support `*` as a suffix wildcard.

### The `add` command

`changesharp add` is the everyday way to record a change:

```bash
changesharp add "Add search"             # append to the open changelist (category prompted)
changesharp add --added "Add search"     # category via flag
changesharp add --separate "doc only"    # force a new fragment file
changesharp add --fragment <file> "x"    # append to a specific fragment
changesharp add --changelist <name> "x"  # append to (or create) a named changelist
```

`add` appends to the most recent fragment (the open changelist). On the default
branch (`main`/`master`) it always creates a separate file so concurrent pushes
stay conflict-free; `--separate`, `--fragment`, and `--changelist` override the
target. `changesharp new` is equivalent to `add --separate`. The `MaxImpact`
cap applies to `add` exactly as it does to `new`.

> **Note on custom mappings**: the cap applies to the *impact level* your `Mappings` declare, not to whether a change is actually breaking. If you map `Changed → Major` (like this repository does) and set `MaxImpact: minor`, then `changesharp add --changed` is blocked too — even for harmless changes. With such a mapping, choose `MaxImpact: major` (or accept that every `Changed` needs `--allow-major`).

### Where it is enforced

| Step | Behavior |
| --- | --- |
| `changesharp add` / `new` (flags) | Category above the cap → refused (exit 3) unless `--allow-major` |
| `changesharp add` / `new` (interactive) | Blocked categories are marked `⚠ blocked (MaxImpact)` in the menu and re-prompted |
| `changesharp validate` | Does **not** enforce the cap (format check only) |
| `changesharp release` | Refused (exit 3) unless `--allow-major` — the production gate (human-in-the-loop) |

```bash
changesharp add --breaking              # ❌ refused above the cap
changesharp add --breaking --allow-major # ✅ deliberate
changesharp release                      # ❌ refused above the cap
changesharp release --allow-major        # ✅ deliberate
```

The `--allow-major` flag is the explicit opt-in at both creation and release, so a Major requires two deliberate decisions. A runnable demo lives in `samples/maximpact-gate/`.

### Known limits

The gates are enforced per command: the `--api-min-level` floor on `validate` and `release`, the `MaxImpact` cap on `new` and `release`. The `prerelease` create/promote path bypasses them: `changesharp prerelease --promote` promotes a pre-release to a final release without any gate check. This matches the pre-existing behavior of `--api-min-level` on that path; if you rely on the gates, run `changesharp validate --api-min-level <level>` (or `changesharp release --allow-major`) as an explicit check before promoting.
