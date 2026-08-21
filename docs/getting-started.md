# Getting Started

Go from zero to a released version in under five minutes.

## 1. Install the tool

```bash
dotnet tool install --global ChangeSharp.Cli
```

## 2. Initialize your project

```bash
cd your-project
changesharp init
```

This creates `changesharp.json` and the `.changesharp/unreleased/` directory.

## 3. Record a change

```bash
changesharp new --added "Add billing API"
```

Every change gets its own fragment. No more editing `CHANGELOG.md` by hand.

## 4. Check what will be released

```bash
changesharp status
```

Shows pending fragments and the version bump they will trigger.

## 5. Release

```bash
changesharp release
```

ChangeSharp aggregates the fragments, bumps the version in your project files, updates `CHANGELOG.md`, and removes the consumed fragments.

## What you get

- A `CHANGELOG.md` following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
- A version number following [SemVer](SemVer%20Rules.md), derived from your fragments.
- No merge conflicts: each change lives in its own file (see [Frictionless Workflow](FrictionlessWorkflow.md)).

## Next steps

- **CI/CD** — automate validation and release: [CI/CD Integration](features/CiIntegration.md)
- **Pre-releases** — publish to channels per branch: [Pre-releases](features/Prereleases.md)
- **Version propagation** — control version files beyond `.csproj`: [SemVer Rules](SemVer%20Rules.md)