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
                                description = "Create a new unreleased change fragment.",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        message = new { type = "string", description = "The description of the change." },
                                        category = new { type = "string", description = "The category of the change (e.g., Added, Fixed, Changed, Removed)." }
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
                    string path = Manager.CreateFragment(message, category);
                    return new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = $"Fragment created: {Path.GetFileName(path)}"
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

                    if (!dryRun)
                    {
                        string? apiMinLevel = args?["apiMinLevel"]?.ToString();
                        if (apiMinLevel != null)
                        {
                            var (pass, _, _) = Manager.CheckApiMinLevel(apiMinLevel);
                            if (!pass)
                            {
                                return new
                                {
                                    content = new[]
                                    {
                                        new
                                        {
                                            type = "text",
                                            text = $"API surface requires at least a '{apiMinLevel}' bump, but fragments are below this level."
                                        }
                                    },
                                    isError = true
                                };
                            }
                        }

                        if (!allowMajor)
                        {
                            var (withinCap, maxImpact, maxLevelName, _, offendingCategories) = Manager.CheckApiMaxLevel();
                            if (!withinCap)
                            {
                                string cats = offendingCategories.Length > 0 ? $" ({string.Join(", ", offendingCategories)})" : "";
                                return new
                                {
                                    content = new[]
                                    {
                                        new
                                        {
                                            type = "text",
                                            text = $"Release would bump to '{maxLevelName}' (level {maxImpact}), above the configured SemverPolicy.MaxImpact{cats}. Set allowMajor: true to proceed."
                                        }
                                    },
                                    isError = true
                                };
                            }
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
                        string warningText = releaseWarnings.Length > 0
                            ? "\nWarnings:\n" + string.Join("\n", releaseWarnings.Select(w => $"  - {w}"))
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
