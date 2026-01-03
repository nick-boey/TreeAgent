using TreeAgent.Web.Features.Agents.Services;

namespace TreeAgent.Web.Tests.Features.Agents;

[TestFixture]
public class MessageParserTests
{
    private readonly MessageParser _parser = new();

    [Test]
    public void Parse_TextMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """{"type":"text","content":"Hello, world!"}""";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Type, Is.EqualTo("text"));
        Assert.That(result.Content, Is.EqualTo("Hello, world!"));
    }

    [Test]
    public void Parse_ToolUseMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """{"type":"tool_use","name":"read_file","input":{"path":"/tmp/test.txt"}}""";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Type, Is.EqualTo("tool_use"));
        Assert.That(result.ToolName, Is.EqualTo("read_file"));
    }

    [Test]
    public void Parse_ToolResultMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """{"type":"tool_result","content":"File contents here"}""";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Type, Is.EqualTo("tool_result"));
    }

    [Test]
    public void Parse_InvalidJson_ReturnsNull()
    {
        // Arrange
        var json = "not valid json";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Parse_EmptyString_ReturnsNull()
    {
        // Act
        var result = _parser.Parse("");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Parse_NullString_ReturnsNull()
    {
        // Act
        var result = _parser.Parse(null!);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Parse_SystemMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """{"type":"system","content":"Initialization complete"}""";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Type, Is.EqualTo("system"));
    }

    [Test]
    public void Parse_ErrorMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """{"type":"error","message":"Something went wrong"}""";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Type, Is.EqualTo("error"));
        Assert.That(result.ErrorMessage, Is.EqualTo("Something went wrong"));
    }

    [Test]
    public void Parse_MessageWithMetadata_PreservesRawJson()
    {
        // Arrange
        var json = """{"type":"text","content":"Hello","timestamp":"2024-01-01T00:00:00Z"}""";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.RawJson, Is.EqualTo(json));
    }
}
