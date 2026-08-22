### Added
- Add command: append a change to the open changelist (--separate/--fragment/--changelist), trunk-safe on the default branch
- SemverPolicy.BranchMaxImpact: per-branch impact caps (e.g. release/* only accepts fixes)
- Validate the public API surface with a single command (changesharp validate --api-surface): regenerate surfaces, check baselines, derive impact vs origin/main, gate fragments - CI job reduced to one step
