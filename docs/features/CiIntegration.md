# CI/CD Integration Contract (Step 6)

This document defines how ChangeSharp integrates into enterprise CI/CD pipelines (GitHub Actions, GitLab CI).

## 🚀 The Standard Workflow

ChangeSharp is designed to facilitate a "Release on Merge", "Release on Tag", or **on-demand** workflow. The recommended setup triggers the release **manually** (`workflow_dispatch`) from the current state of `main`, so a release only happens when a human decides to create one.

### 1. Pull Request / Merge Request Phase
**Goal**: Ensure every change is documented and valid before it reaches the main branch.

-   **Command**: `changesharp validate --require-fragments`
-   **Contract**:
    -   **Exit Code 0**: All fragments are valid and present.
    -   **Exit Code 3**: Validation Error (Missing fragment or invalid format).
-   **CI Action**: Post a ❌ on the PR if exit code is non-zero. Use the predicted version bump in the comment.

### 2. Pull Request Bot (Step 13)
To drive adoption and ensure fragment quality, we recommend using the **ChangeSharp Bot** logic in your CI. 

A "Phase 1" bot is a simple script that:
1. Runs `changesharp validate --require-fragments`.
2. If it fails, posts a helpful comment on the Pull Request.

Example for GitHub Actions:
```yaml
      - name: Post PR Comment
        if: failure()
        uses: actions/github-script@v7
        with:
          script: |
            github.rest.issues.createComment({
              issue_number: context.issue.number,
              owner: context.repo.owner,
              repo: context.repo.repo,
              body: "### ⚠️ Missing Change Fragments\n\nThis PR needs fragments. Run `changesharp new`."
            })
```
See `samples/ci/github-bot.yml` for a full implementation.

### 3. Post-Merge / Release Phase
**Goal**: Finalize the release, update the changelog, and bump versions.

-   **Workflow**:
    1.  **Dry-run Validation**: `changesharp release --dry-run`
        - Ensures the environment is ready (secrets, git permissions).
    2.  **Approval Gate**: (Manual step in GitHub/GitLab UI)
    3.  **Perform Release**: `changesharp release`
        - Aggregates fragments.
        - Updates `CHANGELOG.md` and project files (`.csproj`, `package.json`, etc.).
        - Stages fragments in `.changesharp/releasing/`, then deletes them after a successful release.
    4.  **Git Tag & Push**: (Scripted)
        - `git add . && git commit -m "chore: release v1.2.0"`
        - `git tag v1.2.0 && git push --atomic origin main v1.2.0` (commit + tag poussés atomiquement)
    5.  **Forge Release** (Optional): `changesharp publish`
        - Outputs the latest released version, its tag, and the corresponding changelog segment so the CI can create a release on the forge (GitHub, GitLab, …).
        - ChangeSharp only produces the payload; each forge's own tool does the release creation ("each tool does what it does well").

### `changesharp publish`

Reads `CHANGELOG.md` and outputs the most recent released version and its changelog segment. The output is forge-agnostic: the CI pipes it into the forge's release tool.

```bash
# Human-readable
changesharp publish
# Machine-readable (for CI)
changesharp publish --json
```

`--json` output shape:

```json
{
  "success": true,
  "data": {
    "version": "1.2.0",
    "tag": "v1.2.0",
    "title": "1.2.0",
    "body": "### Added\n- New feature"
  }
}
```

**GitHub** example (`gh`):

```bash
PAYLOAD=$(changesharp publish --json)
VERSION=$(echo "$PAYLOAD" | jq -r '.data.version')
BODY=$(echo "$PAYLOAD" | jq -r '.data.body')
gh release create "v$VERSION" --title "$VERSION" --notes "$BODY"
```

The same payload feeds `glab release create` on GitLab, `curl` to the GitLab/API, etc. Because the release is created as the **last** step of the CI pipeline, it only happens if everything before it succeeded.

---

## 🔐 Environment & Secrets

To function correctly in CI, ChangeSharp requires:

| Variable | Requirement | Description |
| :--- | :--- | :--- |
| `GITHUB_TOKEN` | Required | For the bot to post comments on PRs. |
| `GIT_AUTHOR_NAME` | Required | For the release commit. |
| `GIT_AUTHOR_EMAIL` | Required | For the release commit. |
| `CHANGESHARP_CONFIG` | Optional | Override `changesharp.json` path. |

---

## 🛠️ Sample GitHub Action (`release.yml`)

```yaml
name: Release
on:
  workflow_dispatch:
  push:
    branches: [main]

jobs:
  prepare:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    outputs:
      fragment_count: ${{ steps.plan.outputs.fragment_count }}
      next_version: ${{ steps.plan.outputs.next_version }}
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Install ChangeSharp
        run: dotnet tool install --global ChangeSharp.Cli

      - name: Compute release plan
        id: plan
        run: |
          STATUS=$(changesharp status --json)
          COUNT=$(echo "$STATUS" | jq -r '.data.fragmentCount')
          NEXT=$(echo "$STATUS" | jq -r '.data.nextVersion')
          echo "fragment_count=$COUNT" >> "$GITHUB_OUTPUT"
          echo "next_version=$NEXT" >> "$GITHUB_OUTPUT"
          {
            echo "## Release plan"
            echo
            echo "Next version: **$NEXT**"
            echo '```markdown'
            echo "$STATUS" | jq -r '.data.aggregatedChanges // "No unreleased fragments."'
            echo '```'
          } >> "$GITHUB_STEP_SUMMARY"

      - name: Post release plan for review
        if: steps.plan.outputs.fragment_count != '0' && github.event_name == 'workflow_dispatch'
        uses: actions/github-script@v7
        with:
          script: |
            const status = JSON.parse(process.env.STATUS_JSON);
            const body = [
              `## 🚀 Release plan — ${status.nextVersion}`,
              `**Fragments to release:** ${status.fragmentCount}`,
              `### Changes`,
              status.aggregatedChanges || "*none*",
              `> Approve the \`release\` environment to publish this version.`
            ].join('\n');
            await github.rest.repos.createCommitComment({
              owner: context.repo.owner, repo: context.repo.repo,
              commit_sha: context.sha, body
            });
        env:
          STATUS_JSON: ${{ steps.plan.outputs.status }}

  release:
    runs-on: ubuntu-latest
    needs: prepare
    if: github.event_name == 'workflow_dispatch' && needs.prepare.outputs.fragment_count != '0'
    environment: release
    permissions:
      contents: write
      id-token: write
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Install ChangeSharp
        run: dotnet tool install --global ChangeSharp.Cli

      - name: Perform Release
        run: |
          changesharp release
          git config user.name "github-actions"
          git config user.email "github-actions@github.com"
          git add .
          git commit -m "chore: release ${{ needs.prepare.outputs.next_version }}"
          git tag v${{ needs.prepare.outputs.next_version }}
          git push --atomic origin main v${{ needs.prepare.outputs.next_version }}

      - name: Create GitHub release
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          PAYLOAD=$(changesharp publish --json)
          VERSION=$(echo "$PAYLOAD" | jq -r '.data.version')
          BODY=$(echo "$PAYLOAD" | jq -r '.data.body')
          gh release create "v$VERSION" --title "$VERSION" --notes "$BODY"
```

---

## ⚠️ Reliability & Error Handling

-   **Missing External Tools**: ChangeSharp does not run external tools itself. Version propagation handlers that cannot locate their target file (e.g., a configured `.csproj` path) emit a warning to stderr and continue; the release is not blocked.
-   **Idempotence**: Running `release` twice on the same state will result in **Exit Code 2** (No changes to release), which is safe and should not fail the pipeline.
