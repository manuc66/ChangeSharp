### Added
- SemverPolicy.MaxImpact cap: block fragments/releases that would force a Major bump unless --allow-major is passed (new + release)
- Sample workspace samples/maximpact-gate demonstrating the MaxImpact cap (run-demo.sh)
- Dogfood the API Surface Gate on ChangeSharp itself: committed baselines (CLI help, MCP tools, library public API) + update script + api-surface CI job + PublicApiBaselineTests
- Expose the safety gates on MCP tools: validate_fragments apiMinLevel, perform_release allowMajor/apiMinLevel
- Unify safety-gate orchestration in the library (GetCreateFragmentError, GetReleaseGateResult) so the CLI and MCP share the same gate sequence
- Record explicit --allow-major decisions in release output (audit trail, CLI + MCP)

### Fixed
- Reduce CodeFactor cognitive-complexity findings in the interactive category menu and version-bump computation (behavior-preserving refactor)