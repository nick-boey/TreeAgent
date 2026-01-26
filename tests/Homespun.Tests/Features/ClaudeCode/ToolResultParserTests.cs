using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class ToolResultParserTests
{
    private ToolResultParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _parser = new ToolResultParser();
    }

    #region Read Tool Tests

    [Test]
    public void Parse_ReadTool_Success_ReturnsReadToolData()
    {
        // Arrange
        var content = "     1→public class Test\n     2→{\n     3→}";

        // Act
        var result = _parser.Parse("Read", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ToolName, Is.EqualTo("Read"));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.TypedData, Is.TypeOf<ReadToolData>());

        var readData = (ReadToolData)result.TypedData!;
        Assert.That(readData.Content, Is.EqualTo(content));
        Assert.That(readData.TotalLines, Is.EqualTo(3));
    }

    [Test]
    public void Parse_ReadTool_WithFilePath_ExtractsPath()
    {
        // Arrange
        var content = "/src/Homespun/Program.cs\n     1→using System;";

        // Act
        var result = _parser.Parse("Read", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Summary, Does.Contain("Program.cs"));
    }

    [Test]
    public void Parse_ReadTool_Error_SetsIsSuccessFalse()
    {
        // Arrange
        var content = "Error: File not found";

        // Act
        var result = _parser.Parse("Read", content, isError: true);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsSuccess, Is.False);
        Assert.That(result.TypedData, Is.TypeOf<ReadToolData>());
    }

    [Test]
    public void Parse_ReadTool_DetectsLanguageFromPath()
    {
        // Arrange
        var content = "File: /path/to/file.cs\nContent here";

        // Act
        var result = _parser.Parse("Read", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        var readData = result!.TypedData as ReadToolData;
        Assert.That(readData?.Language, Is.EqualTo("csharp"));
    }

    #endregion

    #region Write Tool Tests

    [Test]
    public void Parse_WriteTool_Created_ReturnsWriteToolData()
    {
        // Arrange
        var content = "File created: /path/to/newfile.cs";

        // Act
        var result = _parser.Parse("Write", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ToolName, Is.EqualTo("Write"));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.TypedData, Is.TypeOf<WriteToolData>());

        var writeData = (WriteToolData)result.TypedData!;
        Assert.That(writeData.Operation, Is.EqualTo("created"));
    }

    [Test]
    public void Parse_WriteTool_Updated_ReturnsCorrectOperation()
    {
        // Arrange
        var content = "File updated: /path/to/file.cs";

        // Act
        var result = _parser.Parse("Write", content, isError: false);

        // Assert
        var writeData = result?.TypedData as WriteToolData;
        Assert.That(writeData?.Operation, Is.EqualTo("updated"));
    }

    [Test]
    public void Parse_WriteTool_Error_SetsIsSuccessFalse()
    {
        // Arrange
        var content = "Error writing file: Permission denied";

        // Act
        var result = _parser.Parse("Write", content, isError: true);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsSuccess, Is.False);
        Assert.That(result.Summary, Does.Contain("Failed"));
    }

    #endregion

    #region Edit Tool Tests

    [Test]
    public void Parse_EditTool_Success_ReturnsWriteToolDataWithEditOperation()
    {
        // Arrange
        var content = "Successfully edited /path/to/file.cs";

        // Act
        var result = _parser.Parse("Edit", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ToolName, Is.EqualTo("Edit"));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.TypedData, Is.TypeOf<WriteToolData>());

        var writeData = (WriteToolData)result.TypedData!;
        Assert.That(writeData.Operation, Is.EqualTo("edited"));
    }

    #endregion

    #region Bash Tool Tests

    [Test]
    public void Parse_BashTool_Success_ReturnsBashToolData()
    {
        // Arrange
        var content = "total 32\ndrwxr-xr-x 5 user user 4096 Jan 1 00:00 .";

        // Act
        var result = _parser.Parse("Bash", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ToolName, Is.EqualTo("Bash"));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.TypedData, Is.TypeOf<BashToolData>());

        var bashData = (BashToolData)result.TypedData!;
        Assert.That(bashData.Output, Is.EqualTo(content));
        Assert.That(bashData.IsError, Is.False);
    }

    [Test]
    public void Parse_BashTool_WithCommand_ExtractsCommand()
    {
        // Arrange
        var content = "$ ls -la\ntotal 32\ndrwxr-xr-x 5 user user 4096 Jan 1 00:00 .";

        // Act
        var result = _parser.Parse("Bash", content, isError: false);

        // Assert
        var bashData = result?.TypedData as BashToolData;
        Assert.That(bashData?.Command, Is.EqualTo("ls -la"));
    }

    [Test]
    public void Parse_BashTool_Error_SetsErrorFlags()
    {
        // Arrange
        var content = "Command not found: xyz";

        // Act
        var result = _parser.Parse("Bash", content, isError: true);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsSuccess, Is.False);
        Assert.That(result.Summary, Is.EqualTo("Command failed"));

        var bashData = (BashToolData)result.TypedData!;
        Assert.That(bashData.IsError, Is.True);
    }

    [Test]
    public void Parse_BashTool_EmptyOutput_ShowsCompleted()
    {
        // Arrange
        var content = "";

        // Act
        var result = _parser.Parse("Bash", content, isError: false);

        // Assert
        Assert.That(result?.Summary, Is.EqualTo("Command completed"));
    }

    #endregion

    #region Task/Explore Tool Tests

    [Test]
    public void Parse_TaskTool_Success_ReturnsAgentToolData()
    {
        // Arrange
        var content = "Task completed successfully.\n\nDetailed output here...";

        // Act
        var result = _parser.Parse("Task", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ToolName, Is.EqualTo("Task"));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.TypedData, Is.TypeOf<AgentToolData>());

        var agentData = (AgentToolData)result.TypedData!;
        Assert.That(agentData.Summary, Does.Contain("Task completed"));
        Assert.That(agentData.DetailedOutput, Is.EqualTo(content));
    }

    [Test]
    public void Parse_ExploreTool_Success_ReturnsAgentToolData()
    {
        // Arrange
        var content = "Found 5 relevant files in the codebase.";

        // Act
        var result = _parser.Parse("Explore", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ToolName, Is.EqualTo("Explore"));
        Assert.That(result.TypedData, Is.TypeOf<AgentToolData>());
    }

    #endregion

    #region Grep Tool Tests

    [Test]
    public void Parse_GrepTool_WithMatches_ReturnsGrepToolData()
    {
        // Arrange
        var content = "src/file1.cs:10:public class Test\nsrc/file2.cs:20:public class Another";

        // Act
        var result = _parser.Parse("Grep", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ToolName, Is.EqualTo("Grep"));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.TypedData, Is.TypeOf<GrepToolData>());

        var grepData = (GrepToolData)result.TypedData!;
        Assert.That(grepData.Matches, Has.Count.EqualTo(2));
        Assert.That(grepData.TotalMatches, Is.EqualTo(2));
        Assert.That(result.Summary, Is.EqualTo("Found 2 matches"));
    }

    [Test]
    public void Parse_GrepTool_NoMatches_ReturnsEmptyMatches()
    {
        // Arrange
        var content = "";

        // Act
        var result = _parser.Parse("Grep", content, isError: false);

        // Assert
        var grepData = result?.TypedData as GrepToolData;
        Assert.That(grepData?.Matches, Is.Empty);
        Assert.That(result?.Summary, Is.EqualTo("No matches found"));
    }

    [Test]
    public void Parse_GrepTool_SingleMatch_UsesSingularForm()
    {
        // Arrange
        var content = "src/file.cs:10:match";

        // Act
        var result = _parser.Parse("Grep", content, isError: false);

        // Assert
        Assert.That(result?.Summary, Is.EqualTo("Found 1 match"));
    }

    [Test]
    public void Parse_GrepTool_ParsesMatchDetails()
    {
        // Arrange
        var content = "src/Program.cs:42:    Console.WriteLine(\"Hello\");";

        // Act
        var result = _parser.Parse("Grep", content, isError: false);

        // Assert
        var grepData = result?.TypedData as GrepToolData;
        Assert.That(grepData?.Matches, Has.Count.EqualTo(1));

        var match = grepData!.Matches[0];
        Assert.That(match.FilePath, Is.EqualTo("src/Program.cs"));
        Assert.That(match.LineNumber, Is.EqualTo(42));
        Assert.That(match.Content, Does.Contain("Console.WriteLine"));
    }

    #endregion

    #region Glob Tool Tests

    [Test]
    public void Parse_GlobTool_WithFiles_ReturnsGlobToolData()
    {
        // Arrange
        var content = "/src/file1.cs\n/src/file2.cs\n/src/file3.cs";

        // Act
        var result = _parser.Parse("Glob", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ToolName, Is.EqualTo("Glob"));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.TypedData, Is.TypeOf<GlobToolData>());

        var globData = (GlobToolData)result.TypedData!;
        Assert.That(globData.Files, Has.Count.EqualTo(3));
        Assert.That(globData.TotalFiles, Is.EqualTo(3));
        Assert.That(result.Summary, Is.EqualTo("Found 3 files"));
    }

    [Test]
    public void Parse_GlobTool_NoFiles_ReturnsEmptyList()
    {
        // Arrange
        var content = "";

        // Act
        var result = _parser.Parse("Glob", content, isError: false);

        // Assert
        var globData = result?.TypedData as GlobToolData;
        Assert.That(globData?.Files, Is.Empty);
        Assert.That(result?.Summary, Is.EqualTo("No files found"));
    }

    [Test]
    public void Parse_GlobTool_SingleFile_UsesSingularForm()
    {
        // Arrange
        var content = "/src/file.cs";

        // Act
        var result = _parser.Parse("Glob", content, isError: false);

        // Assert
        Assert.That(result?.Summary, Is.EqualTo("Found 1 file"));
    }

    #endregion

    #region Web Tool Tests

    [Test]
    public void Parse_WebFetchTool_Success_ReturnsWebToolData()
    {
        // Arrange
        var content = "Page content retrieved successfully";

        // Act
        var result = _parser.Parse("WebFetch", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ToolName, Is.EqualTo("WebFetch"));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.TypedData, Is.TypeOf<WebToolData>());
        Assert.That(result.Summary, Is.EqualTo("Content retrieved"));
    }

    [Test]
    public void Parse_WebSearchTool_Success_ReturnsWebToolData()
    {
        // Arrange
        var content = "Search results...";

        // Act
        var result = _parser.Parse("WebSearch", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ToolName, Is.EqualTo("WebSearch"));
        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Parse_WebTool_Error_SetsIsSuccessFalse()
    {
        // Arrange
        var content = "Failed to fetch URL";

        // Act
        var result = _parser.Parse("WebFetch", content, isError: true);

        // Assert
        Assert.That(result?.IsSuccess, Is.False);
        Assert.That(result?.Summary, Is.EqualTo("Request failed"));
    }

    #endregion

    #region Generic/Unknown Tool Tests

    [Test]
    public void Parse_UnknownTool_ReturnsGenericToolData()
    {
        // Arrange
        var content = "Some custom output";

        // Act
        var result = _parser.Parse("CustomTool", content, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ToolName, Is.EqualTo("CustomTool"));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.TypedData, Is.TypeOf<GenericToolData>());

        var genericData = (GenericToolData)result.TypedData!;
        Assert.That(genericData.Content, Is.EqualTo(content));
    }

    [Test]
    public void Parse_UnknownTool_Error_SetsIsSuccessFalse()
    {
        // Arrange
        var content = "Error occurred";

        // Act
        var result = _parser.Parse("CustomTool", content, isError: true);

        // Assert
        Assert.That(result?.IsSuccess, Is.False);
        Assert.That(result?.Summary, Does.Contain("failed"));
    }

    [Test]
    public void Parse_NullContent_ReturnsNoOutputSummary()
    {
        // Act
        var result = _parser.Parse("Read", null, isError: false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Summary, Is.EqualTo("(no output)"));
    }

    #endregion

    #region Tool Name Normalization Tests

    [Test]
    public void Parse_ToolNameCaseInsensitive_ParsesCorrectly()
    {
        // Arrange
        var content = "test content";

        // Act
        var readResult = _parser.Parse("READ", content, isError: false);
        var bashResult = _parser.Parse("BASH", content, isError: false);
        var grepResult = _parser.Parse("GREP", content, isError: false);

        // Assert
        Assert.That(readResult?.TypedData, Is.TypeOf<ReadToolData>());
        Assert.That(bashResult?.TypedData, Is.TypeOf<BashToolData>());
        Assert.That(grepResult?.TypedData, Is.TypeOf<GrepToolData>());
    }

    #endregion

    #region Summary Truncation Tests

    [Test]
    public void Parse_LongContent_TruncatesSummary()
    {
        // Arrange
        var longContent = new string('x', 200);

        // Act
        var result = _parser.Parse("CustomTool", longContent, isError: false);

        // Assert
        Assert.That(result?.Summary.Length, Is.LessThanOrEqualTo(53)); // 50 + "..."
    }

    #endregion
}
