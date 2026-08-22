# MCP Integration for AI Agents

ChangeSharp provides a built-in Model Context Protocol (MCP) server, allowing AI agents (like Claude, Cursor, or GitHub Copilot) to interact directly with your changelog and release workflow.

## Features

The MCP server exposes the following tools:

- `get_status`: Get the count of pending fragments, the current version, and the next calculated version.
- `create_fragment`: Create a new change fragment or add to the open changelist. Mirrors the CLI `add` command: optional `allowMajor` bypasses the allowed impact cap, and optional `separate` / `fragment` / `changelist` control the target (new file, specific file, named changelist).
- `validate_fragments`: Ensure all pending fragments follow the correct format. Optional `apiMinLevel` (`patch` | `minor` | `major`) mirrors the CLI `--api-min-level` gate: fails if the fragments' declared impact is below this level.
- `perform_release`: Execute a release, aggregate changes into the changelog, and bump project versions. Mirrors the CLI safety gates: optional `allowMajor` (bypass `SemverPolicy.MaxImpact` cap) and optional `apiMinLevel`.

The safety gates exposed by the CLI (`--api-min-level`, `--allow-major` / `SemverPolicy.MaxImpact`) behave identically through the MCP tools.

> **Security Warning**: In enterprise environments, AI agents should NOT be allowed to perform a release without human approval. It is highly recommended to use the `--dry-run` flag or implement a mandatory approval gate in your CI/CD pipeline before the final release is pushed.

### Security Configuration

The MCP server enforces a two-layer security model for the `perform_release` tool:

| Setting | Default | Description |
|---|---|---|
| `Security.AllowAgentRelease` | `true` | Set to `false` in `changesharp.json` to block all MCP agent releases. |
| `Security.RequireApproval` | `false` | Set to `true` in `changesharp.json` to require `CHANGESHARP_ALLOW_UNSAFE_RELEASE=true` env var. |
| `CHANGESHARP_ALLOW_UNSAFE_RELEASE` | — | Environment variable that must be set to `true` when `RequireApproval` is enabled or `AllowAgentRelease` is `false`. |

When either `AllowAgentRelease: false` or `RequireApproval: true` is set, the agent must set `CHANGESHARP_ALLOW_UNSAFE_RELEASE=true` in its environment to proceed with a release. Dry-run previews are always allowed.

## Configuration

To use the ChangeSharp MCP server, you need to add it to your AI agent's configuration.

### Claude Desktop

Add the following to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "changesharp": {
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/to/ChangeSharp.Mcp/ChangeSharp.Mcp.csproj"]
    }
  }
}
```

### Cursor

1. Open Cursor Settings.
2. Go to **Features** > **MCP**.
3. Click **Add New MCP Server**.
4. Name: `ChangeSharp`
5. Type: `command`
6. Command: `dotnet run --project /absolute/path/to/ChangeSharp.Mcp/ChangeSharp.Mcp.csproj`

## Benefits

By integrating ChangeSharp with your AI agent:
- The agent can automatically create fragments as it implements new features or fixes bugs.
- You can ask the agent: "What's the status of our next release?"
- The agent can help you prepare a release by summarizing the changes.
- Validation is automated before any release is performed.

## 🧠 Repository Intelligence (Upcoming)

Future versions of the MCP server will include tools to help agents better understand the state of the repository:

- `query_changes`: Summarizes technical changes (commits, file diffs) since the last release.
- `suggest_fragments`: Analyzes code changes and suggests the appropriate category and message for a new fragment.
- `audit_compliance`: Runs the Semantic Safety Gates to ensure the agent's proposed release notes match the actual code modifications.
