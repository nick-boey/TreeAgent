namespace Homespun.E2E.Tests;

/// <summary>
/// End-to-end tests for critical user journeys in the Homespun application.
/// These tests verify the full application stack including UI rendering.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class CriticalJourneysTests : PageTest
{
    private string BaseUrl => HomespunFixture.BaseUrl;

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
        };
    }

    [Test]
    public async Task HomePage_LoadsSuccessfully()
    {
        // Navigate to home page
        await Page.GotoAsync(BaseUrl);

        // Wait for Blazor to initialize
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify the page title or main heading
        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Homespun"));
    }

    [Test]
    public async Task ProjectsPage_DisplaysProjectsList()
    {
        // Navigate to projects page
        await Page.GotoAsync($"{BaseUrl}/projects");

        // Wait for Blazor to initialize
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify that the page contains project-related content
        // The page should show either a list of projects or a "no projects" message
        var hasContent = await Page.Locator("main").IsVisibleAsync();
        Assert.That(hasContent, Is.True, "Main content should be visible");
    }

    [Test]
    public async Task Navigation_WorksBetweenPages()
    {
        // Start at home page
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Navigate to settings page via nav link
        var settingsLink = Page.Locator("a[href*='settings'], a:has-text('Settings')").First;
        await settingsLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify URL changed to settings
        Assert.That(Page.Url, Does.Contain("settings").IgnoreCase);
    }

    [Test]
    public async Task SettingsPage_LoadsSuccessfully()
    {
        // Navigate to settings page
        await Page.GotoAsync($"{BaseUrl}/settings");

        // Wait for Blazor to initialize
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify the settings page loads
        var mainContent = Page.Locator("main");
        await Expect(mainContent).ToBeVisibleAsync();
    }

    [Test]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        // This test verifies the health check endpoint works
        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/health");

        Assert.That(response.Ok, Is.True, "Health endpoint should return successful response");
    }

    [Test]
    public async Task SwaggerPage_LoadsSuccessfully()
    {
        // Navigate to Swagger UI
        await Page.GotoAsync($"{BaseUrl}/swagger");

        // Wait for page to load
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify Swagger UI loads - use specific selector to avoid matching multiple elements
        await Expect(Page.Locator("div.swagger-ui")).ToBeVisibleAsync(new()
        {
            Timeout = 10000
        });
    }
}
