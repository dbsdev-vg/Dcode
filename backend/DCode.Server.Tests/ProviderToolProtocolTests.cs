using DCode.Server.Chats.AgentLoop;
using DCode.Server.Tools;

namespace DCode.Server.Tests;

public sealed class ProviderToolProtocolTests
{
    [Fact]
    public void LeadDelegatesProjectVerificationInsteadOfRefusingIt()
    {
        var tools = new[]
        {
            ToolDefinition.Create("filesystem.read", "Read a file", new { type = "object" }),
            ToolDefinition.Create("agent.delegate", "Delegate work", new { type = "object" })
        };

        var prompt = new ProviderToolProtocol().FormatUserMessage("Test it.", tools);

        Assert.Contains("test, build, typecheck, lint, run, preview", prompt, StringComparison.Ordinal);
        Assert.Contains("delegate that verification objective to Coder", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not tell the user to run it manually", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void CoderIsToldToExecuteStandaloneVerification()
    {
        var tools = new[]
        {
            ToolDefinition.Create("filesystem.read", "Read a file", new { type = "object" }),
            ToolDefinition.Create("process.run", "Run a command", new { type = "object" })
        };

        var prompt = new ProviderToolProtocol().FormatUserMessage("Run the build.", tools);

        Assert.Contains("use process.run even when no source edit is needed", prompt, StringComparison.Ordinal);
        Assert.Contains("actual command, exit code", prompt, StringComparison.Ordinal);
    }
}
