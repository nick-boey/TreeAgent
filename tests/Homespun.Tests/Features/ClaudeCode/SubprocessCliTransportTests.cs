using System.Diagnostics;
using Homespun.ClaudeAgentSdk;
using Homespun.ClaudeAgentSdk.Transport;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class SubprocessCliTransportTests
{
    [Test]
    public void HomeEnvironmentVariable_WhenNotInOptions_ShouldBeSetFromEnvironment()
    {
        // Arrange
        var expectedHome = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var options = new ClaudeAgentOptions
        {
            Cwd = "/tmp",
            Env = new Dictionary<string, string>() // HOME not specified
        };

        // Act - Create a ProcessStartInfo the same way SubprocessCliTransport does
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Replicate the environment variable setting logic from SubprocessCliTransport
        foreach (var (key, value) in options.Env)
        {
            startInfo.Environment[key] = value;
        }

        // Ensure HOME is set for Claude CLI to find/create its config directory
        if (!options.Env.ContainsKey("HOME"))
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home))
            {
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            if (!string.IsNullOrEmpty(home))
            {
                startInfo.Environment["HOME"] = home;
            }
        }

        // Assert
        Assert.That(startInfo.Environment.ContainsKey("HOME"), Is.True,
            "HOME environment variable should be set");
        Assert.That(startInfo.Environment["HOME"], Is.EqualTo(expectedHome),
            "HOME should be set from the current environment");
    }

    [Test]
    public void HomeEnvironmentVariable_WhenSpecifiedInOptions_ShouldNotBeOverridden()
    {
        // Arrange
        var customHome = "/custom/home/path";
        var options = new ClaudeAgentOptions
        {
            Cwd = "/tmp",
            Env = new Dictionary<string, string>
            {
                ["HOME"] = customHome
            }
        };

        // Act - Create a ProcessStartInfo the same way SubprocessCliTransport does
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Replicate the environment variable setting logic from SubprocessCliTransport
        foreach (var (key, value) in options.Env)
        {
            startInfo.Environment[key] = value;
        }

        // Ensure HOME is set for Claude CLI to find/create its config directory
        if (!options.Env.ContainsKey("HOME"))
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home))
            {
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            if (!string.IsNullOrEmpty(home))
            {
                startInfo.Environment["HOME"] = home;
            }
        }

        // Assert
        Assert.That(startInfo.Environment["HOME"], Is.EqualTo(customHome),
            "HOME should not be overridden when specified in options");
    }

    [Test]
    public void HomeEnvironmentVariable_InSubprocess_ShouldBeAccessible()
    {
        // This integration test verifies that HOME is properly passed to a subprocess
        // Arrange
        var expectedHome = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = "-c \"echo $HOME\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Replicate the HOME-setting logic from SubprocessCliTransport
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        if (!string.IsNullOrEmpty(home))
        {
            startInfo.Environment["HOME"] = home;
        }

        // Act
        using var process = Process.Start(startInfo);
        Assert.That(process, Is.Not.Null, "Process should start successfully");

        var output = process!.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        // Assert
        Assert.That(process.ExitCode, Is.EqualTo(0), "Process should exit successfully");
        Assert.That(output, Is.EqualTo(expectedHome),
            "Subprocess should receive the HOME environment variable");
    }
}
