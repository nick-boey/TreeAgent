// Gitgraph.js visualization module for Homespun
// Uses @gitgraph/js for rendering PRs and Issues as a git-like graph

const SVG_NAMESPACE = "http://www.w3.org/2000/svg";
const graphs = new Map();

// Load Gitgraph.js from CDN if not already loaded
async function ensureGitgraphLoaded() {
    if (window.GitgraphJS) {
        return window.GitgraphJS;
    }

    // Try to load from CDN
    return new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = 'https://cdn.jsdelivr.net/npm/@gitgraph/js@1.4.0/lib/gitgraph.umd.min.js';
        script.onload = () => {
            if (window.GitgraphJS) {
                resolve(window.GitgraphJS);
            } else {
                reject(new Error('GitgraphJS not available after loading'));
            }
        };
        script.onerror = () => reject(new Error('Failed to load GitgraphJS from CDN'));
        document.head.appendChild(script);
    });
}

// Create a diamond SVG path for issues
function createDiamondPath(size, color) {
    const path = document.createElementNS(SVG_NAMESPACE, 'path');
    // Diamond centered at (size, size)
    const d = `M ${size} 0 L ${size * 2} ${size} L ${size} ${size * 2} L 0 ${size} Z`;
    path.setAttribute('d', d);
    path.setAttribute('fill', color);
    return path;
}

// Create a custom render function for issue nodes (diamond shape)
function createIssueRenderDot(commit, data, dotNetRef) {
    const size = commit.style.dot.size;
    const color = data.color || commit.style.dot.color || '#6b7280';

    const g = document.createElementNS(SVG_NAMESPACE, 'g');
    g.classList.add('node-issue');
    g.setAttribute('data-node-id', data.hash);

    const diamond = createDiamondPath(size, color);
    g.appendChild(diamond);

    // Add click handler
    g.style.cursor = 'pointer';
    g.addEventListener('click', () => {
        dotNetRef.invokeMethodAsync('HandleNodeClick', data.nodeType, data.hash, data.pullRequestNumber, data.issueId);
    });

    return g;
}

// Create a custom render function for PR nodes (circle - default, but with click handler)
function createPRRenderDot(commit, data, dotNetRef) {
    const size = commit.style.dot.size;
    const color = data.color || commit.style.dot.color || '#6b7280';

    const g = document.createElementNS(SVG_NAMESPACE, 'g');
    g.classList.add('node-pr');
    g.setAttribute('data-node-id', data.hash);

    const circle = document.createElementNS(SVG_NAMESPACE, 'circle');
    circle.setAttribute('cx', size.toString());
    circle.setAttribute('cy', size.toString());
    circle.setAttribute('r', size.toString());
    circle.setAttribute('fill', color);
    g.appendChild(circle);

    // Add click handler
    g.style.cursor = 'pointer';
    g.addEventListener('click', () => {
        dotNetRef.invokeMethodAsync('HandleNodeClick', data.nodeType, data.hash, data.pullRequestNumber, data.issueId);
    });

    return g;
}

// Get theme colors from CSS custom properties
function getThemeColors() {
    const style = getComputedStyle(document.documentElement);
    return {
        main: style.getPropertyValue('--color-basalt').trim() || '#6b7280',
        branch1: style.getPropertyValue('--color-ocean').trim() || '#51A5C1',
        branch2: style.getPropertyValue('--color-lagoon').trim() || '#36A390',
        branch3: style.getPropertyValue('--color-wattle').trim() || '#E6B422',
        branch4: style.getPropertyValue('--status-merged').trim() || '#a855f7',
    };
}

// Initialize the graph
export async function initializeGraph(containerId, graphData, dotNetRef) {
    const container = document.getElementById(containerId);
    if (!container) {
        console.error('Container not found:', containerId);
        return;
    }

    // Clear any existing content
    container.innerHTML = '';

    try {
        const GitgraphJS = await ensureGitgraphLoaded();
        const themeColors = getThemeColors();

        // Create custom template
        const template = GitgraphJS.templateExtend(GitgraphJS.TemplateName.Metro, {
            colors: [themeColors.main, themeColors.branch1, themeColors.branch2, themeColors.branch3, themeColors.branch4],
            branch: {
                lineWidth: 2,
                spacing: 50,
                label: {
                    display: false  // Hide branch labels for cleaner look
                }
            },
            commit: {
                spacing: 50,
                dot: {
                    size: 8,
                    strokeWidth: 0
                },
                message: {
                    displayAuthor: false,
                    displayHash: false,
                    font: '14px sans-serif'
                }
            }
        });

        // Create the graph
        const gitgraph = GitgraphJS.createGitgraph(container, {
            template,
            orientation: GitgraphJS.Orientation.VerticalReverse,  // Oldest at top
            mode: GitgraphJS.Mode.Compact
        });

        // Track created branches
        const branches = new Map();

        // Create main branch first
        const mainBranch = gitgraph.branch(graphData.mainBranchName);
        branches.set(graphData.mainBranchName, mainBranch);

        // Create branches from the data
        for (const branchData of graphData.branches) {
            if (branchData.name === graphData.mainBranchName) continue;

            const parentBranch = branches.get(branchData.parentBranch || graphData.mainBranchName) || mainBranch;
            const branch = parentBranch.branch({
                name: branchData.name,
                style: branchData.color ? { color: branchData.color } : undefined
            });
            branches.set(branchData.name, branch);
        }

        // Add commits
        for (const commitData of graphData.commits) {
            let branch = branches.get(commitData.branch);
            if (!branch) {
                // Create branch on-the-fly if needed
                branch = mainBranch.branch(commitData.branch);
                branches.set(commitData.branch, branch);
            }

            const isIssue = commitData.nodeType.includes('Issue');

            branch.commit({
                subject: commitData.subject,
                hash: commitData.hash,
                style: {
                    dot: {
                        color: commitData.color || undefined
                    }
                },
                renderDot: isIssue
                    ? (commit) => createIssueRenderDot(commit, commitData, dotNetRef)
                    : (commit) => createPRRenderDot(commit, commitData, dotNetRef),
                renderMessage: (commit) => {
                    // Custom message rendering with click handler
                    const text = document.createElementNS(SVG_NAMESPACE, 'text');
                    text.setAttribute('alignment-baseline', 'central');
                    text.setAttribute('dominant-baseline', 'central');
                    text.setAttribute('fill', getComputedStyle(document.documentElement).getPropertyValue('--text-primary').trim() || '#000');
                    text.style.cursor = 'pointer';
                    text.textContent = commit.subject;

                    text.addEventListener('click', () => {
                        dotNetRef.invokeMethodAsync('HandleNodeClick', commitData.nodeType, commitData.hash, commitData.pullRequestNumber, commitData.issueId);
                    });

                    return text;
                }
            });
        }

        // Store reference for later updates
        graphs.set(containerId, { gitgraph, dotNetRef, graphData });

    } catch (error) {
        console.error('Failed to initialize graph:', error);
        container.innerHTML = `<div class="gitgraph-error">Failed to load graph: ${error.message}</div>`;
    }
}

// Highlight a specific node
export function highlightNode(containerId, nodeId) {
    const container = document.getElementById(containerId);
    if (!container) return;

    // Remove existing highlights
    container.querySelectorAll('.node-selected').forEach(el => el.classList.remove('node-selected'));

    if (nodeId) {
        // Add highlight to matching node
        const nodeElement = container.querySelector(`[data-node-id="${nodeId}"]`);
        if (nodeElement) {
            nodeElement.classList.add('node-selected');
        }
    }
}

// Cleanup
export function dispose(containerId) {
    graphs.delete(containerId);
}
