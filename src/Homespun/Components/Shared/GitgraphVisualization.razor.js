// Gitgraph.js visualization module for Homespun
// Uses @gitgraph/js for rendering PRs and Issues as a git-like graph

const SVG_NAMESPACE = "http://www.w3.org/2000/svg";
const graphs = new Map();

// Tooltip element (shared across all graphs)
let tooltip = null;

function createTooltip() {
    tooltip = document.createElement('div');
    tooltip.className = 'graph-tooltip';
    tooltip.style.cssText = `
        position: fixed;
        background: var(--bg-primary, #1a1a2e);
        border: 1px solid var(--text-muted, #6c757d);
        border-radius: 8px;
        padding: 12px;
        max-width: 350px;
        box-shadow: 0 4px 12px rgba(0,0,0,0.3);
        z-index: 1000;
        pointer-events: none;
        opacity: 0;
        transition: opacity 0.15s ease;
        font-size: 13px;
        line-height: 1.4;
    `;
    document.body.appendChild(tooltip);
}

function showTooltip(data, event) {
    if (!tooltip) createTooltip();

    let content = '';

    if (data.nodeType.includes('PullRequest')) {
        content = `
            <div style="font-weight: 600; margin-bottom: 8px; color: var(--text-primary, #eaeaea);">
                PR #${data.pullRequestNumber}: ${data.subject.replace(/^#\d+:\s*/, '')}
            </div>
            <div style="color: var(--text-secondary, #a0a0a0); margin-bottom: 6px;">
                <span style="display: inline-block; padding: 2px 8px; border-radius: 4px; background: ${data.color}; color: white; font-size: 11px; margin-right: 8px;">
                    ${data.status}
                </span>
                Branch: <code style="background: var(--bg-secondary, #16213e); padding: 2px 4px; border-radius: 3px;">${data.branch}</code>
            </div>
            <div style="color: var(--text-secondary, #a0a0a0);">${data.description || data.subject}</div>
        `;
    } else {
        content = `
            <div style="font-weight: 600; margin-bottom: 8px; color: var(--text-primary, #eaeaea);">
                ${data.issueId}: ${data.subject.replace(/^\[.*?\]\s*/, '')}
            </div>
            <div style="color: var(--text-secondary, #a0a0a0); margin-bottom: 6px;">
                <span style="display: inline-block; padding: 2px 8px; border-radius: 4px; background: ${data.color}; color: white; font-size: 11px; margin-right: 8px;">
                    ${data.tag || 'issue'}
                </span>
            </div>
            <div style="color: var(--text-secondary, #a0a0a0);">${data.description || data.subject}</div>
        `;
    }

    tooltip.innerHTML = content;
    tooltip.style.opacity = '1';

    // Position tooltip near cursor
    const x = event.clientX + 15;
    const y = event.clientY + 15;

    // Adjust if tooltip would go off screen
    const rect = tooltip.getBoundingClientRect();
    const maxX = window.innerWidth - 360;
    const maxY = window.innerHeight - rect.height - 20;

    tooltip.style.left = Math.min(x, maxX) + 'px';
    tooltip.style.top = Math.min(y, maxY) + 'px';
}

function hideTooltip() {
    if (tooltip) {
        tooltip.style.opacity = '0';
    }
}

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

    // Tooltip on hover
    g.addEventListener('mouseenter', (e) => showTooltip(data, e));
    g.addEventListener('mousemove', (e) => showTooltip(data, e));
    g.addEventListener('mouseleave', hideTooltip);

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

    // Tooltip on hover
    g.addEventListener('mouseenter', (e) => showTooltip(data, e));
    g.addEventListener('mousemove', (e) => showTooltip(data, e));
    g.addEventListener('mouseleave', hideTooltip);

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

        // Build branch color lookup from data (needed for template render functions)
        const branchColors = {};
        for (const branchData of graphData.branches) {
            branchColors[branchData.name] = branchData.color;
        }

        // Create custom template - settings match tested values
        const template = GitgraphJS.templateExtend(GitgraphJS.TemplateName.Metro, {
            colors: [themeColors.main, themeColors.branch1, themeColors.branch2, themeColors.branch3, themeColors.branch4],
            branch: {
                lineWidth: 1.5,
                spacing: 15,
                label: {
                    display: true,
                    font: '12px sans-serif',
                    borderRadius: 4,
                    strokeColor: themeColors.main,
                    bgColor: 'transparent',
                    // Custom render function for outline-only labels
                    render: (branchName) => {
                        const g = document.createElementNS(SVG_NAMESPACE, 'g');

                        // Create text first to measure it
                        const text = document.createElementNS(SVG_NAMESPACE, 'text');
                        text.textContent = branchName;
                        text.setAttribute('font-size', '12');
                        text.setAttribute('font-family', 'sans-serif');
                        text.setAttribute('dominant-baseline', 'middle');
                        text.setAttribute('text-anchor', 'start');

                        // Get color from branch lookup or use default
                        const color = branchColors[branchName] || themeColors.main;
                        text.setAttribute('fill', color);

                        // Position text with padding
                        const paddingX = 6;
                        const paddingY = 4;
                        text.setAttribute('x', paddingX.toString());
                        text.setAttribute('y', '10');

                        g.appendChild(text);

                        // Measure text (approximate)
                        const textWidth = branchName.length * 7;
                        const textHeight = 14;

                        // Create outline rectangle
                        const rect = document.createElementNS(SVG_NAMESPACE, 'rect');
                        rect.setAttribute('x', '0');
                        rect.setAttribute('y', '0');
                        rect.setAttribute('width', (textWidth + paddingX * 2).toString());
                        rect.setAttribute('height', (textHeight + paddingY * 2).toString());
                        rect.setAttribute('rx', '4');
                        rect.setAttribute('ry', '4');
                        rect.setAttribute('fill', 'transparent');
                        rect.setAttribute('stroke', color);
                        rect.setAttribute('stroke-width', '1');

                        // Insert rect before text so text appears on top
                        g.insertBefore(rect, text);

                        return g;
                    }
                }
            },
            commit: {
                spacing: 25,
                dot: {
                    size: 5,
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
            orientation: GitgraphJS.Orientation.VerticalReverse  // Oldest at top
        });

        // Track created branches
        const branches = new Map();

        // Create main branch first
        const mainBranch = gitgraph.branch(graphData.mainBranchName);
        branches.set(graphData.mainBranchName, mainBranch);

        // Get text color for messages
        const textColor = getComputedStyle(document.documentElement).getPropertyValue('--text-primary').trim() || '#000';

        // Helper to create "Load More" button render function
        function createLoadMoreRenderDot(commit, dotNetRef) {
            const size = commit.style.dot.size;
            const g = document.createElementNS(SVG_NAMESPACE, 'g');
            g.classList.add('node-load-more');
            g.setAttribute('data-node-id', 'load-more-past-prs');

            // Create a clickable circle with "+" symbol
            const circle = document.createElementNS(SVG_NAMESPACE, 'circle');
            circle.setAttribute('cx', size.toString());
            circle.setAttribute('cy', size.toString());
            circle.setAttribute('r', (size * 1.2).toString());
            circle.setAttribute('fill', themeColors.branch1);
            circle.setAttribute('stroke', '#fff');
            circle.setAttribute('stroke-width', '2');
            g.appendChild(circle);

            // Add "+" text
            const text = document.createElementNS(SVG_NAMESPACE, 'text');
            text.setAttribute('x', size.toString());
            text.setAttribute('y', size.toString());
            text.setAttribute('font-size', '12');
            text.setAttribute('font-weight', 'bold');
            text.setAttribute('fill', '#fff');
            text.setAttribute('text-anchor', 'middle');
            text.setAttribute('dominant-baseline', 'middle');
            text.textContent = '+';
            g.appendChild(text);

            // Add click handler
            g.style.cursor = 'pointer';
            g.addEventListener('click', () => {
                dotNetRef.invokeMethodAsync('LoadMorePastPRs');
            });

            return g;
        }

        // Helper to create commit options
        function createCommitOptions(commitData) {
            const isIssue = commitData.nodeType.includes('Issue');
            return {
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
                    // Custom message rendering with click handler and proper vertical alignment
                    const text = document.createElementNS(SVG_NAMESPACE, 'text');
                    text.setAttribute('dominant-baseline', 'middle');
                    text.setAttribute('dy', '0.35em');  // Fine-tune vertical alignment
                    text.setAttribute('fill', textColor);
                    text.style.cursor = 'pointer';
                    text.textContent = commit.subject;

                    text.addEventListener('click', () => {
                        dotNetRef.invokeMethodAsync('HandleNodeClick', commitData.nodeType, commitData.hash, commitData.pullRequestNumber, commitData.issueId);
                    });

                    // Tooltip on hover
                    text.addEventListener('mouseenter', (e) => showTooltip(commitData, e));
                    text.addEventListener('mousemove', (e) => showTooltip(commitData, e));
                    text.addEventListener('mouseleave', hideTooltip);

                    return text;
                }
            };
        }

        // Add "Load More" button at the top if there are more past PRs
        if (graphData.hasMorePastPRs) {
            mainBranch.commit({
                subject: '▼ Load more past PRs',
                hash: 'load-more-past-prs',
                style: {
                    dot: {
                        color: themeColors.branch1
                    }
                },
                renderDot: (commit) => createLoadMoreRenderDot(commit, dotNetRef),
                renderMessage: (commit) => {
                    const text = document.createElementNS(SVG_NAMESPACE, 'text');
                    text.setAttribute('dominant-baseline', 'middle');
                    text.setAttribute('dy', '0.35em');
                    text.setAttribute('fill', themeColors.branch1);
                    text.setAttribute('font-weight', 'bold');
                    text.style.cursor = 'pointer';
                    text.textContent = commit.subject;

                    text.addEventListener('click', () => {
                        dotNetRef.invokeMethodAsync('LoadMorePastPRs');
                    });

                    return text;
                }
            });
        }

        // Process commits - create branches lazily when first needed
        // This ensures branches are created at the right point in the graph
        for (const commitData of graphData.commits) {
            let branch = branches.get(commitData.branch);

            // If branch doesn't exist yet, create it from main (at current HEAD)
            if (!branch) {
                const color = branchColors[commitData.branch];
                branch = mainBranch.branch({
                    name: commitData.branch,
                    style: color ? { color: color } : undefined
                });
                branches.set(commitData.branch, branch);
            }

            branch.commit(createCommitOptions(commitData));
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
