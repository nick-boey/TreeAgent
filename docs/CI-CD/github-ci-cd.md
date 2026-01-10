# GitHub CI/CD Workflows

This project uses GitHub Actions for Continuous Integration (CI) and Continuous Deployment (CD).

## Workflows

### 1. CI (Build and Test)
**File:** `.github/workflows/ci.yml`

This workflow triggers on:
- Pull Requests targeting the `main` branch.
- Manual triggers (`workflow_dispatch`).

**Steps:**
1.  **Checkout Code:** Retrieves the latest code.
2.  **Setup .NET:** Installs .NET SDK 10.0.
3.  **Restore:** Restores NuGet packages.
4.  **Build:** Builds the solution in Release configuration.
5.  **Test:** Runs unit tests to ensure code quality.

### 2. CD (Build and Push Container)
**File:** `.github/workflows/cd.yml`

This workflow triggers on:
- **GitHub Releases:** When a new release is **published**.

**Steps:**
1.  **Checkout Code:** Retrieves the source code.
2.  **Login to GHCR:** Authenticates with GitHub Container Registry using the automatic `GITHUB_TOKEN`.
3.  **Extract Metadata:** Generates Docker tags based on the release version (e.g., `v1.0.0`, `1.0`, `latest`).
4.  **Build and Push:**
    - Builds the Docker image.
    - **Injects Version:** Passes the release version (e.g., `1.0.0`) into the build process, ensuring the application assembly version matches the release tag.
    - Pushes the image and tags to GHCR.

## Setup & Configuration

### Prerequisites
1.  **GitHub Actions Enabled:** Ensure Actions are enabled in the repository settings.
2.  **GHCR Permissions:** The workflow uses `GITHUB_TOKEN` to push to GHCR. Ensure the repository settings allow:
    - **Actions:** Read and write permissions.
    - OR, explicitly in **Package Settings** (if the package already exists), ensure the repository has "Write" access.

### Creating a Release
To trigger the deployment:
1.  Go to the **Releases** section on GitHub.
2.  Draft a new release.
3.  Create a tag (e.g., `v1.0.0`). **Note:** The workflow expects tags to start with `v`.
4.  Publish the release.

The workflow will automatically:
- Strip the `v` prefix (result: `1.0.0`).
- Bake this version into the .NET application assembly.
- Push the Docker image tagged as `v1.0.0` and `latest`.

### Access Tokens
No manual secrets are required for standard GHCR integration. The workflow uses the built-in `GITHUB_TOKEN`.

If pushing to an external registry (e.g., Docker Hub), you would need to:
1.  Add secrets to the repository (`DOCKER_USERNAME`, `DOCKER_PASSWORD`).
2.  Update `cd.yml` to use those secrets instead of `GITHUB_TOKEN`.
