using System.Text.Json;
using System.Diagnostics;
using DCode.Server.Tools;
using DCode.Server.Tools.BuiltIn;
using DCode.Server.Tools.Permissions;
using DCode.Server.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;

namespace DCode.Server.Tests;

public sealed class ToolRuntimeTests : IDisposable
{
    private readonly string _projectPath = Path.Combine(Path.GetTempPath(), "dcode-tool-tests", Guid.NewGuid().ToString("N"));

    public ToolRuntimeTests() => Directory.CreateDirectory(_projectPath);

    [Fact]
    public async Task ReadFile_ReadsFileInsideProject()
    {
        await File.WriteAllTextAsync(Path.Combine(_projectPath, "hello.txt"), "hello DCode");
        var result = await ExecuteAsync(new ReadFileTool(), new { path = "hello.txt" }, ToolPermission.FileSystemRead);

        Assert.True(result.Success);
        Assert.Contains("hello DCode", JsonSerializer.Serialize(result.Output));
    }

    [Fact]
    public async Task ReadFile_ReturnsRequestedInclusiveLineRangeAndMetadata()
    {
        await File.WriteAllTextAsync(Path.Combine(_projectPath, "lines.ts"), string.Join("\n", Enumerable.Range(1, 10).Select(number => $"line {number}")));
        var result = await ExecuteAsync(new ReadFileTool(), new { path = "lines.ts", startLine = 3, endLine = 5 }, ToolPermission.FileSystemRead);

        Assert.True(result.Success);
        var output = JsonSerializer.Serialize(result.Output);
        Assert.Contains("line 3", output); Assert.Contains("line 5", output); Assert.DoesNotContain("line 2", output); Assert.DoesNotContain("line 6", output);
        Assert.Equal(3, Convert.ToInt32(result.Metadata["actualStartLine"]));
        Assert.Equal(5, Convert.ToInt32(result.Metadata["actualEndLine"]));
        Assert.Equal(10, Convert.ToInt32(result.Metadata["totalLineCount"]));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(3, 2)]
    [InlineData(20, 21)]
    public async Task ReadFile_RejectsInvalidLineRanges(int startLine, int endLine)
    {
        await File.WriteAllTextAsync(Path.Combine(_projectPath, "range.txt"), "one\ntwo\nthree");
        var result = await ExecuteAsync(new ReadFileTool(), new { path = "range.txt", startLine, endLine }, ToolPermission.FileSystemRead);
        Assert.Equal("invalid_line_range", result.Error?.Code);
    }

    [Fact]
    public async Task ReadFile_RejectsPathTraversal()
    {
        var result = await ExecuteAsync(new ReadFileTool(), new { path = "../secret.txt" }, ToolPermission.FileSystemRead);

        Assert.False(result.Success);
        Assert.Equal("path_outside_project", result.Error?.Code);
    }

    [Fact]
    public async Task ReadFile_ReturnsControlledMissingFileError()
    {
        var result = await ExecuteAsync(new ReadFileTool(), new { path = "missing.txt" }, ToolPermission.FileSystemRead);

        Assert.False(result.Success);
        Assert.Equal("file_not_found", result.Error?.Code);
    }

    [Fact]
    public async Task SearchFiles_SkipsJunctionAndContinuesSearchingProject()
    {
        var outsidePath = $"{_projectPath}-outside";
        var junctionPath = Path.Combine(_projectPath, "outside-link");
        Directory.CreateDirectory(outsidePath);
        await File.WriteAllTextAsync(Path.Combine(outsidePath, "secret.txt"), "find-me outside");
        await File.WriteAllTextAsync(Path.Combine(_projectPath, "safe.txt"), "find-me inside");

        try
        {
            CreateJunction(junctionPath, outsidePath);

            var search = await ExecuteAsync(new SearchFilesTool(), new { query = "find-me", path = "." }, ToolPermission.FileSystemRead);
            Assert.True(search.Success);
            var serialized = JsonSerializer.Serialize(search.Output);
            Assert.Contains("safe.txt", serialized);
            Assert.DoesNotContain("secret.txt", serialized);
            Assert.Equal(1, Convert.ToInt32(search.Metadata["skippedCount"]));

            var list = await ExecuteAsync(new ListFilesTool(), new { path = ".", recursive = true }, ToolPermission.FileSystemRead);
            Assert.True(list.Success);
            var listed = JsonSerializer.Serialize(list.Output);
            Assert.Contains("safe.txt", listed);
            Assert.DoesNotContain("outside-link", listed);
            Assert.Equal(1, Convert.ToInt32(list.Metadata["skippedCount"]));

            var directRead = await ExecuteAsync(new ReadFileTool(), new { path = "outside-link/secret.txt" }, ToolPermission.FileSystemRead);
            Assert.False(directRead.Success);
            Assert.Equal("path_outside_project", directRead.Error?.Code);
        }
        finally
        {
            if (Directory.Exists(junctionPath)) Directory.Delete(junctionPath);
            if (Directory.Exists(outsidePath)) Directory.Delete(outsidePath, recursive: true);
        }
    }

    [Theory]
    [InlineData("before", "start\ninserted\nanchor\nend")]
    [InlineData("after", "start\nanchor\ninserted\nend")]
    public async Task InsertFile_InsertsBeforeOrAfterUniqueAnchor(string position, string expected)
    {
        var path = Path.Combine(_projectPath, "insert.txt");
        await File.WriteAllTextAsync(path, "start\nanchor\nend");

        var result = await ExecuteAsync(new InsertFileTool(), new { path = "insert.txt", anchor = "anchor", position, content = position == "before" ? "inserted\n" : "\ninserted" }, ToolPermission.FileSystemWrite);

        Assert.True(result.Success);
        Assert.Equal(expected, await File.ReadAllTextAsync(path));
        Assert.Equal(1, Convert.ToInt32(result.Metadata["matchedOccurrenceCount"]));
        Assert.Equal(1, Convert.ToInt32(result.Metadata["selectedOccurrence"]));
        Assert.True(Convert.ToInt32(result.Metadata["insertionOffset"]) >= 0);
    }

    [Fact]
    public async Task InsertFile_ReturnsMissingAndAmbiguousAnchorErrorsWithoutChangingFile()
    {
        var path = Path.Combine(_projectPath, "anchors.txt");
        const string original = "anchor and anchor";
        await File.WriteAllTextAsync(path, original);
        var missing = await ExecuteAsync(new InsertFileTool(), new { path = "anchors.txt", anchor = "missing", position = "before", content = "x" }, ToolPermission.FileSystemWrite);
        var ambiguous = await ExecuteAsync(new InsertFileTool(), new { path = "anchors.txt", anchor = "anchor", position = "after", content = "x" }, ToolPermission.FileSystemWrite);

        Assert.Equal("anchor_not_found", missing.Error?.Code);
        Assert.Equal("anchor_ambiguous", ambiguous.Error?.Code);
        Assert.Equal(2, Convert.ToInt32(ambiguous.Metadata["matchedOccurrenceCount"]));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("first", "X-one-one", 1, 0)]
    [InlineData("last", "one-oneX-", 2, 7)]
    public async Task InsertFile_SelectsNamedOccurrence(string occurrence, string expected, int selected, int offset)
    {
        var path = Path.Combine(_projectPath, $"{occurrence}.txt");
        await File.WriteAllTextAsync(path, "one-one");

        var result = await ExecuteAsync(new InsertFileTool(), new { path = $"{occurrence}.txt", anchor = "one", position = occurrence == "first" ? "before" : "after", occurrence, content = "X-" }, ToolPermission.FileSystemWrite);

        Assert.True(result.Success);
        Assert.Equal(expected, await File.ReadAllTextAsync(path));
        Assert.Equal(2, Convert.ToInt32(result.Metadata["matchedOccurrenceCount"]));
        Assert.Equal(selected, Convert.ToInt32(result.Metadata["selectedOccurrence"]));
        Assert.Equal(offset, Convert.ToInt32(result.Metadata["insertionOffset"]));
    }

    [Fact]
    public async Task InsertFile_SelectsOneBasedNumericOccurrence()
    {
        var path = Path.Combine(_projectPath, "numeric.txt");
        await File.WriteAllTextAsync(path, "item,item,item");

        var result = await ExecuteAsync(new InsertFileTool(), new { path = "numeric.txt", anchor = "item", position = "before", occurrence = 2, content = "selected-" }, ToolPermission.FileSystemWrite);

        Assert.True(result.Success);
        Assert.Equal("item,selected-item,item", await File.ReadAllTextAsync(path));
        Assert.Equal(3, Convert.ToInt32(result.Metadata["matchedOccurrenceCount"]));
        Assert.Equal(2, Convert.ToInt32(result.Metadata["selectedOccurrence"]));
        Assert.Equal(5, Convert.ToInt32(result.Metadata["insertionOffset"]));
    }

    [Fact]
    public async Task InsertFile_OutOfRangeOccurrenceDoesNotModifyFile()
    {
        var path = Path.Combine(_projectPath, "out-of-range.txt");
        const string original = "anchor-anchor";
        await File.WriteAllTextAsync(path, original);

        var result = await ExecuteAsync(new InsertFileTool(), new { path = "out-of-range.txt", anchor = "anchor", position = "after", occurrence = 3, content = "changed" }, ToolPermission.FileSystemWrite);

        Assert.False(result.Success);
        Assert.Equal("occurrence_out_of_range", result.Error?.Code);
        Assert.Equal(2, Convert.ToInt32(result.Metadata["matchedOccurrenceCount"]));
        Assert.Equal(3, Convert.ToInt32(result.Metadata["selectedOccurrence"]));
        Assert.Null(result.Metadata["insertionOffset"]);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task InsertFile_RejectsTraversal()
    {
        var result = await ExecuteAsync(new InsertFileTool(), new { path = "../outside.txt", anchor = "x", position = "before", content = "y" }, ToolPermission.FileSystemWrite);
        Assert.Equal("path_outside_project", result.Error?.Code);
    }

    [Fact]
    public async Task InsertFile_RejectsOversizedAnchorWithoutChangingFile()
    {
        var path = Path.Combine(_projectPath, "large-anchor.txt");
        const string original = "stable\n];";
        await File.WriteAllTextAsync(path, original);
        var result = await ExecuteAsync(new InsertFileTool(), new { path = "large-anchor.txt", anchor = new string('x', InsertFileTool.MaximumAnchorLength + 1), position = "before", content = "value" }, ToolPermission.FileSystemWrite);

        Assert.Equal("anchor_too_large", result.Error?.Code);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task PatchFile_AppliesMultipleHunksAtomically()
    {
        var path = Path.Combine(_projectPath, "patch.ts");
        await File.WriteAllTextAsync(path, "const one = 1;\nconst two = 2;\n");
        var result = await ExecuteAsync(new PatchFileTool(), new { path = "patch.ts", hunks = new[] { new { oldText = "one = 1", newText = "one = 10" }, new { oldText = "two = 2", newText = "two = 20" } } }, ToolPermission.FileSystemWrite);

        Assert.True(result.Success);
        Assert.Equal("const one = 10;\nconst two = 20;\n", await File.ReadAllTextAsync(path));
        Assert.Contains("\"hunksApplied\":2", JsonSerializer.Serialize(result.Output));
    }

    [Fact]
    public async Task PatchFile_FailedLaterHunkLeavesFileUnchanged()
    {
        var path = Path.Combine(_projectPath, "atomic.ts");
        const string original = "const one = 1;\nconst two = 2;\n";
        await File.WriteAllTextAsync(path, original);
        var result = await ExecuteAsync(new PatchFileTool(), new { path = "atomic.ts", hunks = new[] { new { oldText = "one = 1", newText = "one = 10" }, new { oldText = "missing", newText = "value" } } }, ToolPermission.FileSystemWrite);

        Assert.False(result.Success);
        Assert.Equal("patch_hunk_not_found", result.Error?.Code);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task EditFile_GuardsLargePayloadButStillSupportsSmallExactReplacement()
    {
        var path = Path.Combine(_projectPath, "edit.txt");
        await File.WriteAllTextAsync(path, "small original");
        var small = await ExecuteAsync(new EditFileTool(), new { path = "edit.txt", oldText = "original", newText = "updated" }, ToolPermission.FileSystemWrite);
        Assert.True(small.Success);
        Assert.Equal("small updated", await File.ReadAllTextAsync(path));

        var oversized = new string('x', EditFileTool.MaximumTextLength + 1);
        var guarded = await ExecuteAsync(new EditFileTool(), new { path = "edit.txt", oldText = oversized, newText = "value" }, ToolPermission.FileSystemWrite);
        Assert.Equal("edit_too_large", guarded.Error?.Code);
        Assert.Equal("small updated", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task EditFile_ValidTypeScriptEditReportsSuccessfulValidation()
    {
        var path = Path.Combine(_projectPath, "valid-edit.ts");
        await File.WriteAllTextAsync(path, "export const value = { count: 1 };\n");
        var result = await ExecuteAsync(new EditFileTool(), new { path = "valid-edit.ts", oldText = "count: 1", newText = "count: 2" }, ToolPermission.FileSystemWrite);
        Assert.True(result.Success);
        Assert.Equal(true, result.Metadata["validationPerformed"]);
        Assert.Equal("typescript", result.Metadata["language"]);
        Assert.Equal(0, Convert.ToInt32(result.Metadata["diagnosticCount"]));
    }

    [Fact]
    public async Task EditFile_MissingBraceFailsValidationAndRollsBack()
    {
        var path = Path.Combine(_projectPath, "brace.ts");
        const string original = "export const value = { count: 1 };\n";
        await File.WriteAllTextAsync(path, original);
        var result = await ExecuteAsync(new EditFileTool(), new { path = "brace.ts", oldText = " };", newText = ";" }, ToolPermission.FileSystemWrite);
        Assert.Equal("syntax_validation_failed", result.Error?.Code);
        Assert.True(Convert.ToInt32(result.Metadata["diagnosticCount"]) > 0);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task InsertFile_MissingCommaFailsValidationAndRollsBack()
    {
        var path = Path.Combine(_projectPath, "comma.ts");
        const string original = "export const values = [{ id: 1 }];\n";
        await File.WriteAllTextAsync(path, original);
        var result = await ExecuteAsync(new InsertFileTool(), new { path = "comma.ts", anchor = "]", position = "before", content = "{ id: 2 }" }, ToolPermission.FileSystemWrite);
        Assert.Equal("syntax_validation_failed", result.Error?.Code);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task PatchFile_MalformedArrayFailsValidationAndRollsBack()
    {
        var path = Path.Combine(_projectPath, "array.ts");
        const string original = "export const values = [1, 2];\n";
        await File.WriteAllTextAsync(path, original);
        var result = await ExecuteAsync(new PatchFileTool(), new { path = "array.ts", hunks = new[] { new { oldText = "[1, 2]", newText = "[1, 2" } } }, ToolPermission.FileSystemWrite);
        Assert.Equal("syntax_validation_failed", result.Error?.Code);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteFile_MalformedObjectFailsValidationAndPreservesExistingFile()
    {
        var path = Path.Combine(_projectPath, "object.ts");
        const string original = "export const value = { id: 1 };\n";
        await File.WriteAllTextAsync(path, original);
        var result = await ExecuteAsync(new WriteFileTool(), new { path = "object.ts", content = "export const value = { id: 2;" }, ToolPermission.FileSystemWrite);
        Assert.Equal("syntax_validation_failed", result.Error?.Code);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task RunCommand_CapturesSuccessfulExecution()
    {
        var result = await ExecuteAsync(new RunCommandTool(), new { command = "dotnet", arguments = new[] { "--version" }, timeoutMs = 10_000 }, ToolPermission.ProcessExecute);

        Assert.True(result.Success);
        Assert.Contains("exitCode", JsonSerializer.Serialize(result.Output));
    }

    [Fact]
    public async Task RunCommand_ReturnsCommandFailure()
    {
        var result = await ExecuteAsync(new RunCommandTool(), new { command = "dotnet", arguments = new[] { "not-a-real-dotnet-command" }, timeoutMs = 10_000 }, ToolPermission.ProcessExecute);

        Assert.False(result.Success);
        Assert.Equal("command_failed", result.Error?.Code);
        Assert.NotEqual(0, result.Metadata["exitCode"]);
    }

    [Fact]
    public async Task RunCommand_ReturnsTimeout()
    {
        var result = await ExecuteAsync(new RunCommandTool(), new { command = "powershell.exe", arguments = new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 5" }, timeoutMs = 100 }, ToolPermission.ProcessExecute);

        Assert.False(result.Success);
        Assert.Equal("timeout", result.Error?.Code);
    }

    [Fact]
    public async Task RunCommand_HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource(100);
        var result = await ExecuteAsync(
            new RunCommandTool(),
            new { command = "powershell.exe", arguments = new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 5" }, timeoutMs = 10_000 },
            [ToolPermission.ProcessExecute],
            cancellation.Token
        );

        Assert.False(result.Success);
        Assert.Equal("cancelled", result.Error?.Code);
    }

    [Fact]
    public async Task Executor_ReturnsUnknownToolError()
    {
        var executor = new ToolExecutor(new ToolRegistry(), new FixedPolicyResolver(ToolPermissionPolicy.Allow));
        var result = await executor.ExecuteAsync(
            new ToolCall("unknown.tool", JsonSerializer.SerializeToElement(new { })),
            CreateContext([])
        );

        Assert.False(result.Success);
        Assert.Equal("unknown_tool", result.Error?.Code);
    }

    [Theory]
    [InlineData(ToolPermissionPolicy.Ask, "approval_required")]
    [InlineData(ToolPermissionPolicy.Deny, "permission_denied")]
    public async Task Executor_EnforcesStoredPolicy(ToolPermissionPolicy policy, string expectedError)
    {
        await File.WriteAllTextAsync(Path.Combine(_projectPath, "policy.txt"), "policy test");
        var registry = new ToolRegistry();
        registry.Register(new ReadFileTool());
        var executor = new ToolExecutor(registry, new FixedPolicyResolver(policy));

        var result = await executor.ExecuteAsync(
            new ToolCall("filesystem.read", JsonSerializer.SerializeToElement(new { path = "policy.txt" })),
            CreateContext([])
        );

        Assert.False(result.Success);
        Assert.Equal(expectedError, result.Error?.Code);
    }

    [Fact]
    public void GlobalPolicies_AreSeededAndPersisted()
    {
        var databasePath = Path.Combine(_projectPath, "policies.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DatabasePath"] = databasePath })
            .Build();
        var database = new DCodeDatabase(configuration);
        database.Initialize();
        var repository = new ToolPolicyRepository(database);

        Assert.Equal(ToolPermissionPolicy.Allow, repository.GetGlobal("filesystem.read"));
        Assert.Equal(ToolPermissionPolicy.Allow, repository.GetGlobal("filesystem.list"));
        Assert.Equal(ToolPermissionPolicy.Allow, repository.GetGlobal("filesystem.search"));
        Assert.Equal(ToolPermissionPolicy.Ask, repository.GetGlobal("filesystem.write"));
        Assert.Equal(ToolPermissionPolicy.Ask, repository.GetGlobal("filesystem.edit"));
        Assert.Equal(ToolPermissionPolicy.Ask, repository.GetGlobal("filesystem.insert"));
        Assert.Equal(ToolPermissionPolicy.Ask, repository.GetGlobal("filesystem.patch"));
        Assert.Equal(ToolPermissionPolicy.Ask, repository.GetGlobal("process.run"));
        repository.SetGlobal("filesystem.read", ToolPermissionPolicy.Deny);
        Assert.Equal(ToolPermissionPolicy.Deny, new ToolPolicyRepository(database).GetGlobal("filesystem.read"));
        Assert.Equal(ToolPermissionPolicy.Ask, new ToolPolicyResolver(repository).Resolve("future.plugin.tool").Policy);
    }

    [Fact]
    public void Registry_RejectsDuplicateToolIds()
    {
        var registry = new ToolRegistry();
        registry.Register(new ReadFileTool());

        Assert.Throws<InvalidOperationException>(() => registry.Register(new ReadFileTool()));
    }

    private Task<ToolResult> ExecuteAsync(ITool tool, object arguments, params ToolPermission[] permissions) =>
        ExecuteAsync(tool, arguments, permissions, CancellationToken.None);

    private Task<ToolResult> ExecuteAsync(ITool tool, object arguments, IReadOnlyCollection<ToolPermission> permissions, CancellationToken cancellationToken)
    {
        _ = permissions;
        var registry = new ToolRegistry();
        registry.Register(tool);
        return new ToolExecutor(registry, new FixedPolicyResolver(ToolPermissionPolicy.Allow)).ExecuteAsync(
            new ToolCall(tool.Definition.Id, JsonSerializer.SerializeToElement(arguments)),
            CreateContext(permissions, cancellationToken)
        );
    }

    private ToolContext CreateContext(IEnumerable<ToolPermission> permissions, CancellationToken cancellationToken = default) =>
        new("project-1", _projectPath, _projectPath, cancellationToken);

    private static void CreateJunction(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junctionPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start mklink.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Unable to create test junction: {process.StandardError.ReadToEnd()}");
        }
    }

    private sealed class FixedPolicyResolver(ToolPermissionPolicy policy) : IToolPolicyResolver
    {
        public ToolPolicyResolution Resolve(string toolId, string? projectId = null, string? agentId = null) =>
            new(toolId, policy, "global");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_projectPath)) Directory.Delete(_projectPath, recursive: true);
    }
}
