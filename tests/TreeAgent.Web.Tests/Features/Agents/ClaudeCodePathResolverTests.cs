using TreeAgent.Web.Features.Agents.Services;

namespace TreeAgent.Web.Tests.Features.Agents;

[TestFixture]
public class ClaudeCodePathResolverTests
{
    [Test]
    public void Resolve_WithEnvironmentVariable_ReturnsEnvPath()
    {
        // Arrange
        var envPath = @"C:\custom\claude.exe";
        var resolver = new ClaudeCodePathResolver(
            environmentVariable: envPath,
            fileExistsCheck: _ => false);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.That(result, Is.EqualTo(envPath));
    }

    [Test]
    public void Resolve_WithoutEnvVar_ChecksDefaultLocations()
    {
        // Arrange
        var existingPath = @"C:\Users\test\AppData\Local\Programs\claude-code\claude.exe";
        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == existingPath,
            getWindowsLocalAppData: () => @"C:\Users\test\AppData\Local",
            getWindowsAppData: () => @"C:\Users\test\AppData\Roaming",
            getHomeDirectory: () => @"C:\Users\test",
            isWindows: () => true);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.That(result, Is.EqualTo(existingPath));
    }

    [Test]
    public void Resolve_Windows_ChecksNativeInstallerFirst()
    {
        // Arrange
        var nativePath = @"C:\Users\test\AppData\Local\Programs\claude-code\claude.exe";
        var npmPath = @"C:\Users\test\AppData\Roaming\npm\claude.cmd";

        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == nativePath || path == npmPath,
            getWindowsLocalAppData: () => @"C:\Users\test\AppData\Local",
            getWindowsAppData: () => @"C:\Users\test\AppData\Roaming",
            getHomeDirectory: () => @"C:\Users\test",
            isWindows: () => true);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.That(result, Is.EqualTo(nativePath));
    }

    [Test]
    public void Resolve_Windows_FallsBackToNpmPath()
    {
        // Arrange
        var npmPath = @"C:\Users\test\AppData\Roaming\npm\claude.cmd";

        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == npmPath,
            getWindowsLocalAppData: () => @"C:\Users\test\AppData\Local",
            getWindowsAppData: () => @"C:\Users\test\AppData\Roaming",
            getHomeDirectory: () => @"C:\Users\test",
            isWindows: () => true);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.That(result, Is.EqualTo(npmPath));
    }

    [Test]
    public void Resolve_Linux_ChecksNativeInstallerFirst()
    {
        // Arrange
        var nativePath = "/usr/local/bin/claude";
        var npmPath = "/home/test/.npm-global/bin/claude";

        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == nativePath || path == npmPath,
            getWindowsLocalAppData: () => null,
            getWindowsAppData: () => null,
            getHomeDirectory: () => "/home/test",
            isWindows: () => false);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.That(result, Is.EqualTo(nativePath));
    }

    [Test]
    public void Resolve_Linux_FallsBackToNpmGlobalPath()
    {
        // Arrange
        var npmPath = "/home/test/.npm-global/bin/claude";

        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == npmPath,
            getWindowsLocalAppData: () => null,
            getWindowsAppData: () => null,
            getHomeDirectory: () => "/home/test",
            isWindows: () => false);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.That(result, Is.EqualTo(npmPath));
    }

    [Test]
    public void Resolve_Linux_FallsBackToLocalBinPath()
    {
        // Arrange
        var localBinPath = "/home/test/.local/bin/claude";

        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == localBinPath,
            getWindowsLocalAppData: () => null,
            getWindowsAppData: () => null,
            getHomeDirectory: () => "/home/test",
            isWindows: () => false);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.That(result, Is.EqualTo(localBinPath));
    }

    [Test]
    public void Resolve_NoPathFound_ReturnsClaude()
    {
        // Arrange
        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: _ => false,
            getWindowsLocalAppData: () => @"C:\Users\test\AppData\Local",
            getWindowsAppData: () => @"C:\Users\test\AppData\Roaming",
            getHomeDirectory: () => @"C:\Users\test",
            isWindows: () => true);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.That(result, Is.EqualTo("claude"));
    }

    [Test]
    public void Resolve_NullLocalAppData_SkipsNativeWindowsPath()
    {
        // Arrange
        var npmPath = @"C:\Users\test\AppData\Roaming\npm\claude.cmd";

        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: path => path == npmPath,
            getWindowsLocalAppData: () => null,
            getWindowsAppData: () => @"C:\Users\test\AppData\Roaming",
            getHomeDirectory: () => @"C:\Users\test",
            isWindows: () => true);

        // Act
        var result = resolver.Resolve();

        // Assert
        Assert.That(result, Is.EqualTo(npmPath));
    }

    [Test]
    public void GetDefaultPaths_Windows_ReturnsExpectedPaths()
    {
        // Arrange
        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: _ => false,
            getWindowsLocalAppData: () => @"C:\Users\test\AppData\Local",
            getWindowsAppData: () => @"C:\Users\test\AppData\Roaming",
            getHomeDirectory: () => @"C:\Users\test",
            isWindows: () => true);

        // Act
        var paths = resolver.GetDefaultPaths().ToList();

        // Assert
        Assert.That(paths, Does.Contain(@"C:\Users\test\AppData\Local\Programs\claude-code\claude.exe"));
        Assert.That(paths, Does.Contain(@"C:\Users\test\AppData\Roaming\npm\claude.cmd"));
    }

    [Test]
    public void GetDefaultPaths_Linux_ReturnsExpectedPaths()
    {
        // Arrange
        var resolver = new ClaudeCodePathResolver(
            environmentVariable: null,
            fileExistsCheck: _ => false,
            getWindowsLocalAppData: () => null,
            getWindowsAppData: () => null,
            getHomeDirectory: () => "/home/test",
            isWindows: () => false);

        // Act
        var paths = resolver.GetDefaultPaths().ToList();

        // Assert
        Assert.That(paths, Does.Contain("/usr/local/bin/claude"));
        Assert.That(paths, Does.Contain("/home/test/.npm-global/bin/claude"));
        Assert.That(paths, Does.Contain("/home/test/.local/bin/claude"));
    }
}
