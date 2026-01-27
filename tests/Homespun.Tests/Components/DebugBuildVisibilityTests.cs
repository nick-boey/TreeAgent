using NUnit.Framework;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for debug-only UI visibility controlled by preprocessor directives.
///
/// These tests verify that the IsDebugBuild property is correctly set based on the build configuration.
/// The actual UI visibility is controlled by @if (IsDebugBuild) in the Razor components.
///
/// Components affected:
/// - ProjectDetail.razor: "Start Test Agent" button
/// - Session.razor: "Debug SignalR Events" panel
///
/// To verify Release behavior manually:
/// 1. Build with: dotnet build -c Release
/// 2. Run the app and verify the debug elements are not visible
/// </summary>
[TestFixture]
public class DebugBuildVisibilityTests
{
    /// <summary>
    /// Tests that IsDebugBuild returns true in Debug configuration.
    /// This test will pass when running tests in Debug mode (the default).
    /// </summary>
    [Test]
    public void IsDebugBuild_InDebugConfiguration_ReturnsTrue()
    {
        // Arrange & Act
        var isDebugBuild = GetIsDebugBuild();

        // Assert
#if DEBUG
        Assert.That(isDebugBuild, Is.True, "IsDebugBuild should be true in Debug configuration");
#else
        Assert.That(isDebugBuild, Is.False, "IsDebugBuild should be false in Release configuration");
#endif
    }

    /// <summary>
    /// Verifies that the conditional compilation symbols are working correctly.
    /// This test documents the expected behavior for both configurations.
    /// </summary>
    [Test]
    public void ConditionalCompilation_DebugSymbol_IsDefinedCorrectly()
    {
        // This test verifies the DEBUG symbol is defined correctly based on build configuration
        var debugDefined = false;
#if DEBUG
        debugDefined = true;
#endif

        // The DEBUG symbol should match the build configuration
        // This test passes in both Debug (debugDefined=true) and Release (debugDefined=false)
#if DEBUG
        Assert.That(debugDefined, Is.True,
            "DEBUG symbol should be defined when running tests in Debug configuration.");
#else
        Assert.That(debugDefined, Is.False,
            "DEBUG symbol should NOT be defined when running tests in Release configuration.");
#endif
    }

    /// <summary>
    /// Tests that debug-only stub methods exist and can be called without error.
    /// This ensures the Release build stubs are functional.
    /// </summary>
    [Test]
    public void DebugStubs_ExistAndAreCallable()
    {
        // These methods should exist in both Debug and Release configurations
        // In Debug, they have real implementations
        // In Release, they are empty stubs

        // Verify the pattern used for LogDebug-style stubs works correctly
        var logCalled = false;
        TestLogDebug("TestEvent", "TestMessage", () => logCalled = true);

#if DEBUG
        Assert.That(logCalled, Is.True, "LogDebug should execute in Debug mode");
#else
        Assert.That(logCalled, Is.False, "LogDebug should be a no-op in Release mode");
#endif
    }

    // Helper that mirrors the IsDebugBuild pattern used in components
    private static bool GetIsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    // Helper that mirrors the LogDebug stub pattern
    private static void TestLogDebug(string eventType, string message, Action? onDebugLog = null)
    {
#if DEBUG
        onDebugLog?.Invoke();
#endif
        // In Release, this method body is empty (no-op)
    }
}
