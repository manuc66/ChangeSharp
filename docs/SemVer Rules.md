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

### Where it is enforced

| Step | Behavior |
| --- | --- |
| `changesharp new` (flags) | Category above the cap → refused (exit 3) unless `--allow-major` |
| `changesharp new` (interactive) | Blocked categories are marked `⚠ blocked (MaxImpact)` in the menu and re-prompted |
| `changesharp validate` | Does **not** enforce the cap (format check only) |
| `changesharp release` | Refused (exit 3) unless `--allow-major` — the production gate (human-in-the-loop) |

```bash
changesharp new --breaking              # ❌ refused above the cap
changesharp new --breaking --allow-major # ✅ deliberate
changesharp release                      # ❌ refused above the cap
changesharp release --allow-major        # ✅ deliberate
```

The `--allow-major` flag is the explicit opt-in at both creation and release, so a Major requires two deliberate decisions. A runnable demo lives in `samples/maximpact-gate/`.

### Known limits

The gates (`--api-min-level`, `--allow-major`) are enforced on the `new`, `validate`, and `release` commands (and the MCP tools). The `prerelease` create/promote path bypasses them: `changesharp prerelease --promote` promotes a pre-release to a final release without a cap check. This matches the pre-existing behavior of `--api-min-level` on that path; if you rely on the cap, run `changesharp release --allow-major` (or a dry-run `validate --api-min-level`) as an explicit gate before promoting.
