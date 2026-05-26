using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Projects;
using Homespun.Shared.Requests;

namespace Homespun.Api.Tests.Features;

[TestFixture]
public class IssuesApiTests
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private string _projectId = null!;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _factory = new HomespunWebApplicationFactory();
        _client = _factory.CreateClient();

        // Create a project to use across tests
        var request = new { Name = "IssuesApiTest-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var response = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<Project>(JsonOptions);
        _projectId = project!.Id;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<IssueResponse> CreateIssue(
        string title,
        IssueType type = IssueType.Task,
        string? description = null,
        string? parentIssueId = null)
    {
        var request = new CreateIssueRequest
        {
            ProjectId = _projectId,
            Title = title,
            Type = type,
            Description = description,
            ParentIssueId = parentIssueId
        };
        var response = await _client.PostAsJsonAsync("/api/issues", request, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions))!;
    }

    // --- POST /api/issues (Create) ---

    [Test]
    public async Task Create_WithValidData_ReturnsCreated()
    {
        // Arrange
        var request = new CreateIssueRequest
        {
            ProjectId = _projectId,
            Title = "Test issue " + Guid.NewGuid().ToString("N")[..8],
            Type = IssueType.Task,
            Description = "A test description"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var issue = await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue!.Title, Is.EqualTo(request.Title));
            Assert.That(issue.Type, Is.EqualTo(IssueType.Task));
            Assert.That(issue.Description, Is.EqualTo("A test description"));
            Assert.That(issue.Status, Is.EqualTo(IssueStatus.Open));
            Assert.That(issue.Id, Is.Not.Empty);
        });
    }

    [Test]
    public async Task Create_WithBugType_ReturnsBugIssue()
    {
        // Arrange
        var request = new CreateIssueRequest
        {
            ProjectId = _projectId,
            Title = "Bug issue " + Guid.NewGuid().ToString("N")[..8],
            Type = IssueType.Bug
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var issue = await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions);
        Assert.That(issue!.Type, Is.EqualTo(IssueType.Bug));
    }

    [Test]
    public async Task Create_WithNonExistentProject_ReturnsNotFound()
    {
        // Arrange
        var request = new CreateIssueRequest
        {
            ProjectId = "non-existent-project",
            Title = "Should fail"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/projects/{projectId}/issues (List) ---

    [Test]
    public async Task GetByProject_ReturnsOk_WithIssues()
    {
        // Arrange
        var title = "List test " + Guid.NewGuid().ToString("N")[..8];
        await CreateIssue(title);

        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/issues");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var issues = await response.Content.ReadFromJsonAsync<List<IssueResponse>>(JsonOptions);
        Assert.That(issues, Is.Not.Null);
        Assert.That(issues!.Any(i => i.Title == title), Is.True);
    }

    [Test]
    public async Task GetByProject_EmptyProject_ReturnsEmptyList()
    {
        // Arrange - create a fresh project with no issues
        var request = new { Name = "EmptyProject-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", request, JsonOptions);
        projectResponse.EnsureSuccessStatusCode();
        var project = await projectResponse.Content.ReadFromJsonAsync<Project>(JsonOptions);

        // Act
        var response = await _client.GetAsync($"/api/projects/{project!.Id}/issues");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var issues = await response.Content.ReadFromJsonAsync<List<IssueResponse>>(JsonOptions);
        Assert.That(issues, Is.Not.Null);
        Assert.That(issues!, Is.Empty);
    }

    [Test]
    public async Task GetByProject_NonExistentProject_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/non-existent-id/issues");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/issues/{issueId} (Get by ID) ---

    [Test]
    public async Task GetById_ReturnsIssue_WhenExists()
    {
        // Arrange
        var created = await CreateIssue("GetById test " + Guid.NewGuid().ToString("N")[..8]);

        // Act
        var response = await _client.GetAsync($"/api/issues/{created.Id}?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var issue = await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions);
        Assert.Multiple(() =>
        {
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue!.Id, Is.EqualTo(created.Id));
            Assert.That(issue.Title, Is.EqualTo(created.Title));
        });
    }

    [Test]
    public async Task GetById_NonExistentIssue_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/issues/non-existent?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetById_NonExistentProject_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/issues/some-id?projectId=non-existent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- PUT /api/issues/{issueId} (Update) ---

    [Test]
    public async Task Update_Title_ReturnsUpdatedIssue()
    {
        // Arrange
        var created = await CreateIssue("Original title " + Guid.NewGuid().ToString("N")[..8]);
        var newTitle = "Updated title " + Guid.NewGuid().ToString("N")[..8];
        var request = new UpdateIssueRequest
        {
            ProjectId = _projectId,
            Title = newTitle
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/issues/{created.Id}", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var issue = await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions);
        Assert.That(issue!.Title, Is.EqualTo(newTitle));
    }

    [Test]
    public async Task Update_Status_ReturnsUpdatedIssue()
    {
        // Arrange
        var created = await CreateIssue("Status test " + Guid.NewGuid().ToString("N")[..8]);
        var request = new UpdateIssueRequest
        {
            ProjectId = _projectId,
            Status = IssueStatus.Progress
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/issues/{created.Id}", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var issue = await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions);
        Assert.That(issue!.Status, Is.EqualTo(IssueStatus.Progress));
    }

    [Test]
    public async Task Update_Description_ReturnsUpdatedIssue()
    {
        // Arrange
        var created = await CreateIssue("Desc test " + Guid.NewGuid().ToString("N")[..8]);
        var request = new UpdateIssueRequest
        {
            ProjectId = _projectId,
            Description = "Updated description"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/issues/{created.Id}", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var issue = await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions);
        Assert.That(issue!.Description, Is.EqualTo("Updated description"));
    }

    [Test]
    public async Task Update_Type_ReturnsUpdatedIssue()
    {
        // Arrange
        var created = await CreateIssue("Type test " + Guid.NewGuid().ToString("N")[..8]);
        var request = new UpdateIssueRequest
        {
            ProjectId = _projectId,
            Type = IssueType.Bug
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/issues/{created.Id}", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var issue = await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions);
        Assert.That(issue!.Type, Is.EqualTo(IssueType.Bug));
    }

    [Test]
    public async Task Update_NonExistentIssue_ReturnsNotFound()
    {
        // Arrange
        var request = new UpdateIssueRequest { ProjectId = _projectId, Title = "Does not matter" };

        // Act
        var response = await _client.PutAsJsonAsync("/api/issues/non-existent", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Update_NonExistentProject_ReturnsNotFound()
    {
        // Arrange
        var request = new UpdateIssueRequest { ProjectId = "non-existent", Title = "Does not matter" };

        // Act
        var response = await _client.PutAsJsonAsync("/api/issues/some-id", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- DELETE /api/issues/{issueId} ---

    [Test]
    public async Task Delete_ExistingIssue_ReturnsNoContent()
    {
        // Arrange
        var created = await CreateIssue("Delete test " + Guid.NewGuid().ToString("N")[..8]);

        // Act
        var response = await _client.DeleteAsync($"/api/issues/{created.Id}?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task Delete_NonExistentIssue_ReturnsNotFound()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/issues/non-existent?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_NonExistentProject_ReturnsNotFound()
    {
        // Act
        var response = await _client.DeleteAsync("/api/issues/some-id?projectId=non-existent");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/projects/{projectId}/issues/ready ---

    [Test]
    public async Task GetReadyIssues_ReturnsOk()
    {
        // Arrange
        await CreateIssue("Ready test " + Guid.NewGuid().ToString("N")[..8]);

        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/issues/ready");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var issues = await response.Content.ReadFromJsonAsync<List<IssueResponse>>(JsonOptions);
        Assert.That(issues, Is.Not.Null);
    }

    [Test]
    public async Task GetReadyIssues_NonExistentProject_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/non-existent/issues/ready");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- GET /api/projects/{projectId}/issues/assignees ---

    [Test]
    public async Task GetAssignees_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync($"/api/projects/{_projectId}/issues/assignees");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var assigneesResponse = await response.Content.ReadFromJsonAsync<ProjectAssigneesResponse>(JsonOptions);
        Assert.That(assigneesResponse, Is.Not.Null);
        Assert.That(assigneesResponse!.Assignees, Is.Not.Null);
    }

    [Test]
    public async Task GetAssignees_NonExistentProject_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/projects/non-existent/issues/assignees");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/issues/{childId}/set-parent ---

    [Test]
    public async Task SetParent_ValidIssues_ReturnsOk()
    {
        // Arrange
        var parent = await CreateIssue("Parent " + Guid.NewGuid().ToString("N")[..8]);
        var child = await CreateIssue("Child " + Guid.NewGuid().ToString("N")[..8]);
        var request = new SetParentRequest
        {
            ProjectId = _projectId,
            ParentIssueId = parent.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/issues/{child.Id}/set-parent", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var issue = await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions);
        Assert.That(issue!.ParentIssues.Any(p => p.ParentIssue == parent.Id), Is.True);
    }

    [Test]
    public async Task SetParent_NonExistentChild_ReturnsNotFound()
    {
        // Arrange
        var parent = await CreateIssue("Parent " + Guid.NewGuid().ToString("N")[..8]);
        var request = new SetParentRequest
        {
            ProjectId = _projectId,
            ParentIssueId = parent.Id
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/non-existent/set-parent", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task SetParent_NonExistentProject_ReturnsNotFound()
    {
        // Arrange
        var request = new SetParentRequest
        {
            ProjectId = "non-existent",
            ParentIssueId = "some-parent"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/some-child/set-parent", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- POST /api/issues/{issueId}/move-sibling ---

    [Test]
    public async Task MoveSibling_ValidIssueWithParent_ReturnsOk()
    {
        // Arrange - create parent with two children
        var parent = await CreateIssue("MoveSibParent " + Guid.NewGuid().ToString("N")[..8]);
        var child1 = await CreateIssue("Child1 " + Guid.NewGuid().ToString("N")[..8], parentIssueId: parent.Id);
        var child2 = await CreateIssue("Child2 " + Guid.NewGuid().ToString("N")[..8], parentIssueId: parent.Id);

        var request = new MoveSeriesSiblingRequest
        {
            ProjectId = _projectId,
            Direction = MoveDirection.Up
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/issues/{child2.Id}/move-sibling", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task MoveSibling_NonExistentIssue_ReturnsNotFound()
    {
        // Arrange
        var request = new MoveSeriesSiblingRequest
        {
            ProjectId = _projectId,
            Direction = MoveDirection.Up
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/non-existent/move-sibling", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task MoveSibling_NonExistentProject_ReturnsNotFound()
    {
        // Arrange
        var request = new MoveSeriesSiblingRequest
        {
            ProjectId = "non-existent",
            Direction = MoveDirection.Up
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/issues/some-id/move-sibling", request, JsonOptions);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // --- Hierarchy verification via list endpoint ---

    [Test]
    public async Task SetParent_VerifyHierarchyViaGetById()
    {
        // Arrange
        var parent = await CreateIssue("HierParent " + Guid.NewGuid().ToString("N")[..8]);
        var child = await CreateIssue("HierChild " + Guid.NewGuid().ToString("N")[..8]);
        var setParentRequest = new SetParentRequest
        {
            ProjectId = _projectId,
            ParentIssueId = parent.Id
        };
        await _client.PostAsJsonAsync($"/api/issues/{child.Id}/set-parent", setParentRequest, JsonOptions);

        // Act - fetch the child and verify parent relationship
        var response = await _client.GetAsync($"/api/issues/{child.Id}?projectId={_projectId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var issue = await response.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions);
        Assert.That(issue!.ParentIssues.Any(p => p.ParentIssue == parent.Id), Is.True);
    }

    // --- /api/projects/{projectId}/issues/history/{state,undo,redo} ---

    [Test]
    public async Task History_RoundTrip_CreateEditUndoRedo()
    {
        // Use a dedicated project so per-test stacks don't bleed across runs.
        var projRequest = new { Name = "History-" + Guid.NewGuid().ToString("N")[..8], DefaultBranch = "main" };
        var projResponse = await _client.PostAsJsonAsync("/api/projects", projRequest, JsonOptions);
        projResponse.EnsureSuccessStatusCode();
        var project = (await projResponse.Content.ReadFromJsonAsync<Project>(JsonOptions))!;

        async Task<IssueHistoryState> ReadState()
        {
            var stateResp = await _client.GetAsync($"/api/projects/{project.Id}/issues/history/state");
            stateResp.EnsureSuccessStatusCode();
            return (await stateResp.Content.ReadFromJsonAsync<IssueHistoryState>(JsonOptions))!;
        }

        // Empty stacks on a fresh project.
        var initialState = await ReadState();
        Assert.Multiple(() =>
        {
            Assert.That(initialState.CanUndo, Is.False);
            Assert.That(initialState.CanRedo, Is.False);
            Assert.That(initialState.UndoCount, Is.EqualTo(0));
            Assert.That(initialState.RedoCount, Is.EqualTo(0));
        });

        // Empty-stack undo reports failure but still returns 200 + state.
        var emptyUndo = await _client.PostAsync($"/api/projects/{project.Id}/issues/history/undo", null);
        emptyUndo.EnsureSuccessStatusCode();
        var emptyUndoBody = (await emptyUndo.Content.ReadFromJsonAsync<IssueHistoryOperationResponse>(JsonOptions))!;
        Assert.That(emptyUndoBody.Success, Is.False);
        Assert.That(emptyUndoBody.ErrorMessage, Is.EqualTo("Nothing to undo"));

        // Create + edit through the public mutation endpoints — both pushes accrue on the stack.
        var createReq = new CreateIssueRequest
        {
            ProjectId = project.Id,
            Title = "Undo round-trip test",
            Type = IssueType.Task
        };
        var createResp = await _client.PostAsJsonAsync("/api/issues", createReq, JsonOptions);
        createResp.EnsureSuccessStatusCode();
        var created = (await createResp.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions))!;

        var updateReq = new UpdateIssueRequest
        {
            ProjectId = project.Id,
            Status = IssueStatus.Progress
        };
        var updateResp = await _client.PutAsJsonAsync($"/api/issues/{created.Id}", updateReq, JsonOptions);
        updateResp.EnsureSuccessStatusCode();

        var afterEdits = await ReadState();
        Assert.That(afterEdits.UndoCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(afterEdits.CanUndo, Is.True);
        Assert.That(afterEdits.CanRedo, Is.False);

        // Undo the status change.
        var undoResp = await _client.PostAsync($"/api/projects/{project.Id}/issues/history/undo", null);
        undoResp.EnsureSuccessStatusCode();
        var undoBody = (await undoResp.Content.ReadFromJsonAsync<IssueHistoryOperationResponse>(JsonOptions))!;
        Assert.That(undoBody.Success, Is.True);
        Assert.That(undoBody.State.CanRedo, Is.True);

        // Fetch the issue to confirm reverted state.
        var refetched = await _client.GetAsync($"/api/issues/{created.Id}?projectId={project.Id}");
        refetched.EnsureSuccessStatusCode();
        var revertedIssue = (await refetched.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions))!;
        Assert.That(revertedIssue.Status, Is.EqualTo(IssueStatus.Open),
            "undo of the status change should restore Open");

        // Redo to re-apply.
        var redoResp = await _client.PostAsync($"/api/projects/{project.Id}/issues/history/redo", null);
        redoResp.EnsureSuccessStatusCode();
        var redoBody = (await redoResp.Content.ReadFromJsonAsync<IssueHistoryOperationResponse>(JsonOptions))!;
        Assert.That(redoBody.Success, Is.True);

        var refetchedAgain = await _client.GetAsync($"/api/issues/{created.Id}?projectId={project.Id}");
        refetchedAgain.EnsureSuccessStatusCode();
        var reappliedIssue = (await refetchedAgain.Content.ReadFromJsonAsync<IssueResponse>(JsonOptions))!;
        Assert.That(reappliedIssue.Status, Is.EqualTo(IssueStatus.Progress));
    }

    [Test]
    public async Task History_State_ProjectNotFound_Returns404()
    {
        var response = await _client.GetAsync("/api/projects/nonexistent-project-id/issues/history/state");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

}
