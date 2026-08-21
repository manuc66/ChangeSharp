# ChangeSharp

Derive the version. Keep a Changelog.

[![CI](https://github.com/manuc66/ChangeSharp/actions/workflows/dotnet.yml/badge.svg)](https://github.com/manuc66/ChangeSharp/actions/workflows/dotnet.yml) [![CodeQL](https://github.com/manuc66/ChangeSharp/actions/workflows/codeql.yml/badge.svg)](https://github.com/manuc66/ChangeSharp/security/code-scanning) [![Code Coverage](https://codecov.io/gh/manuc66/ChangeSharp/branch/main/graph/badge.svg)](https://codecov.io/gh/manuc66/ChangeSharp) [![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=manuc66_ChangeSharp&metric=alert_status)](https://sonarcloud.io/dashboard?id=manuc66_ChangeSharp) [![Coverage](https://sonarcloud.io/api/project_badges/measure?project=manuc66_ChangeSharp&metric=coverage)](https://sonarcloud.io/dashboard?id=manuc66_ChangeSharp) [![Snyk](https://snyk.io/test/github/manuc66/ChangeSharp/badge.svg)](https://snyk.io/test/github/manuc66/ChangeSharp) [![CodeFactor](https://www.codefactor.io/repository/github/manuc66/ChangeSharp/badge)](https://www.codefactor.io/repository/github/manuc66/ChangeSharp)

A .NET tool for changelog-driven semantic versioning.

## Quick start

```bash
dotnet tool install --global ChangeSharp.Cli
cd your-project
changesharp init
changesharp new --added "Initial setup"
changesharp release        # updates CHANGELOG.md and version files (locally)
```

`changesharp release` only updates the files — you still need to commit, tag, and publish the release on your forge:

```bash
git add . && git commit -m "chore: release 0.1.0"
git tag v0.1.0
git push --atomic origin main v0.1.0
gh release create v0.1.0 --title 0.1.0 \
  --notes "$(changesharp publish --json | jq -r '.data.body')"
```

`changesharp publish` outputs the released version and its changelog segment, ready to feed any forge's release tool (`gh`, `glab`, …).

## Documentation

See the [documentation site](https://manuc66.github.io/ChangeSharp/) — quick start in [Getting Started](https://manuc66.github.io/ChangeSharp/getting-started/).

## Features

- **Changelog-driven versioning** — fragments in `.changesharp/unreleased/` drive SemVer bumps
- **CI/CD integration** — `--json` output, `--next-only`, `--require-fragments`, forge release publishing via `changesharp publish`
- **AI-ready** — built-in MCP server for AI agent integration
- **Version propagation** — MSBuild, JSON, and regex target handlers
- **Pre-release channels** — branch-based pre-release workflows
- **Safety gates** — `--api-min-level`, `--require-approval`, `--dry-run`

## License

MIT
