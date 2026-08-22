using System.Text.Json;
using System.Text.Json.Nodes;
using ChangeSharp;

namespace ChangeSharp.Mcp;

class Program
{
    private static readonly WorkspaceManager Manager = new();

    static async Task Main(string[] args)
    {
        // Set working directory to current directory to ensure WorkspaceManager finds the config
        Directory.SetCurrentDirectory(Directory.GetCurrentDirectory());

        while (true)
        {
            string? line = await Console.In.ReadLineAsync();
            if (line == null) break;

            try
            {
                var request = JsonNode.Parse(line);
                if (request == null) continue;

                var id = request["id"];
                var method = request["method"]?.ToString();

                if (method == "initialize")
                {
                    SendResponse(id, new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new
                        {
                            tools = new { }
                        },
                        serverInfo = new
                        {
                            name = "ChangeSharp MCP Server",
                            version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                        }
                    });
                }
                else if (method == "notifications/initialized")
                {
                    // No response needed for notifications
                }
                else if (method == "tools/list")
                {
                    SendResponse(id, new
                    {
                        tools = new object[]
                        {
                            new
                            {
                                name = "get_status",
                                description = "Get the status of unreleased fragments and the next computed version.",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new { }
                                }
                            },
                            new
                            {
                                name = "create_fragment",
                                description = "Create a new change fragment or add to the open changelist.",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        message = new { type = "string", description = "The description of the change." },
                                        category = new { type = "string", description = "The category of the change (e.g., Added, Fixed, Changed, Removed)." },
                                        allowMajor = new { type = "boolean", description = "Allow a fragment whose impact exceeds the allowed impact cap." },
                                        separate = new { type = "boolean", description = "Create a new fragment file instead of appending to the open changelist." },
                                        fragment = new { type = "string", description = "Append to a specific fragment file in the unreleased directory." },
                                        changelist = new { type = "string", description = "Append to (or create) a deterministically named changelist file." }
                                    },
                                    required = new[] { "message", "category" }
                                }
                            },
                            new
                            {
                                name = "validate_fragments",
                                description = "Validate all unreleased fragments.",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        apiMinLevel = new { type = "string", description = "Optional minimum API impact level (patch, minor, major). Fails if fragments are below this level." }
                                    }
                                }
                            },
                            new
                            {
                                name = "perform_release",
                                description = "Perform a release by aggregating fragments and bumping versions.",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        dryRun = new { type = "boolean", description = "If true, only preview the changes without applying them." },
                                        allowMajor = new { type = "boolean", description = "Allow a release whose impact exceeds SemverPolicy.MaxImpact." },
                                        apiMinLevel = new { type = "string", description = "Optional minimum API impact level (patch, minor, major). Fails if fragments are below this level." }
                                    }
                                }
                            }
                        }
                    });
                }
                else if (method == "tools/call")
                {
                    var toolName = request["params"]?["name"]?.ToString();
                    var arguments = request["params"]?["arguments"];

                    var result = await HandleToolCall(toolName, arguments);
                    SendResponse(id, result);
                }
                else if (id != null)
                {
                    SendError(id, -32601, "Method not found");
                }
            }
            catch (Exception ex)
            {
                // Silently ignore or log to stderr as stdout is reserved for JSON-RPC
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    private static async Task<object> HandleToolCall(string? name, JsonNode? args)
    {
        try
        {
            switch (name)
            {
                case "get_status":
                    Manager.GetStatus(out int count, out var changeSet, out var current, out var next);
                    return new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = $"Pending fragments: {count}\nCurrent version: {current}\nNext version: {next}\nChanges:\n{changeSet.ToChangelogString()}"
                            }
                        }
                    };

                case "create_fragment":
                    var message = args?["message"]?.ToString() ?? "";
                    var category = args?["category"]?.ToString() ?? "Added";
                    bool allowFragmentMajor = args?["allowMajor"]?.GetValue<bool>() ?? false;
                    bool separateFragment = args?["separate"]?.GetValue<bool>() ?? false;
                    string? fragmentTarget = args?["fragment"]?.ToString();
                    string? changelistName = args?["changelist"]?.ToString();
                    string? fragmentError = Manager.GetCreateFragmentError(category, allowFragmentMajor);
                    if (fragmentError != null)
                    {
                        return new
                        {
                            content = new[] { new { type = "text", text = fragmentError } },
                            isError = true
                        };
                    }
                    var (fragmentPath, appended, formattedCategory) = Manager.AppendFragment(message, category, separateFragment, fragmentTarget, changelistName);
                    return new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = appended
                                    ? $"Added to {Path.GetFileName(fragmentPath)} under '{formattedCategory}'"
                                    : $"Fragment created: {Path.GetFileName(fragmentPath)}"
                            }
                        }
                    };

                case "validate_fragments":
                    var validationResults = Manager.Validate();
                    if (validationResults.Count == 0 || validationResults.All(r => r.IsValid))
                    {
                        string? apiMinLevel = args?["apiMinLevel"]?.ToString();
                        if (apiMinLevel != null)
                        {
                            var (pass, maxImpact, maxLevelName) = Manager.CheckApiMinLevel(apiMinLevel);
                            if (!pass)
                            {
                                return new
                                {
                                    content = new[]
                                    {
                                        new
                                        {
                                            type = "text",
                                            text = $"API surface requires at least a '{apiMinLevel}' bump, but fragments only reach '{maxLevelName}' (level {maxImpact})."
                                        }
                                    },
                                    isError = true
                                };
                            }
                        }
                        return new { content = new[] { new { type = "text", text = "All fragments are valid." } } };
                    }
                    var errors = string.Join("\n", validationResults.Where(r => !r.IsValid).Select(r => $"- {r.FilePath}: {string.Join(", ", r.Errors)}"));
                    return new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = $"Validation failed:\n{errors}"
                            }
                        },
                        isError = true
                    };

                case "perform_release":
                    bool dryRun = args?["dryRun"]?.GetValue<bool>() ?? false;
                    bool allowMajor = args?["allowMajor"]?.GetValue<bool>() ?? false;
                    var gate = (Blocked: false, Message: "", CapExceeded: false);

                    if (!dryRun)
                    {
                        string? apiMinLevel = args?["apiMinLevel"]?.ToString();
                        gate = Manager.GetReleaseGateResult(apiMinLevel, allowMajor);
                        if (gate.Blocked)
                        {
                            return new
                            {
                                content = new[] { new { type = "text", text = gate.Message } },
                                isError = true
                            };
                        }

                        // Check Security config from changesharp.json
                        var config = Manager.LoadConfig();
                        if (config.Security.RequireApproval || config.Security.AllowAgentRelease == false)
                        {
                            string? envAllow = Environment.GetEnvironmentVariable("CHANGESHARP_ALLOW_UNSAFE_RELEASE");
                            if (envAllow != "true")
                            {
                                return new
                                {
                                    content = new[]
                                    {
                                        new
                                        {
                                            type = "text",
                                            text = "Release blocked by security policy. Set CHANGESHARP_ALLOW_UNSAFE_RELEASE=true to proceed. Use dryRun: true for a preview."
                                        }
                                    },
                                    isError = true
                                };
                            }
                        }
                    }

                    try
                    {
                        var (version, releaseWarnings) = Manager.Release(DateTime.Today, dryRun);
                        var allWarnings = releaseWarnings.ToList();
                        if (!dryRun && gate.CapExceeded)
                            allWarnings.Add("Major bump explicitly allowed via allowMajor.");
                        string warningText = allWarnings.Count > 0
                            ? "\nWarnings:\n" + string.Join("\n", allWarnings.Select(w => $"  - {w}"))
                            : "";
                        return new
                        {
                            content = new[]
                            {
                                new
                                {
                                    type = "text",
                                    text = (dryRun ? $"Dry-run: Would release version {version}." : $"Released version {version}.") + warningText
                                }
                            }
                        };
                    }
                    catch (InvalidOperationException ex)
                    {
                        return new
                        {
                            content = new[] { new { type = "text", text = ex.Message } },
                            isError = true
                        };
                    }

                default:
                    return new
                    {
                        content = new[] { new { type = "text", text = $"Unknown tool: {name}" } },
                        isError = true
                    };
            }
        }
        catch (Exception ex)
        {
            return new
            {
                content = new[] { new { type = "text", text = $"Error executing tool: {ex.Message}" } },
                isError = true
            };
        }
    }

    private static void SendResponse(JsonNode? id, object result)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id = id,
            result = result
        };
        Console.WriteLine(JsonSerializer.Serialize(response));
    }

    private static void SendError(JsonNode id, int code, string message)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id = id,
            error = new { code, message }
        };
        Console.WriteLine(JsonSerializer.Serialize(response));
    }
}
