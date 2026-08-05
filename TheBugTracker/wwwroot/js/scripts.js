const API_URL = 'https://localhost:44347/api'; // Update with your API URL
let currentUser = null;
let bugs = [];
let currentBugId = null;
let sortColumn = 'id';
let sortDesc = true;

document.addEventListener('DOMContentLoaded', () => {
    checkAuth();
    setupLoginForm();
    setupBugForm();
});

function escapeHtml(value) {
    if (value == null) return '';
    return String(value)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function escapeAttr(value) {
    return escapeHtml(value).replace(/`/g, '&#96;');
}

function checkAuth() {
    const token = localStorage.getItem('token');
    const user = localStorage.getItem('user');

    if (token && user) {
        try {
            currentUser = JSON.parse(user);
            showMainApp();
        } catch {
            logout();
        }
    } else {
        showLoginScreen();
    }
}

function showLoginScreen() {
    document.getElementById('loginScreen').classList.remove('hidden');
    document.getElementById('mainApp').classList.add('hidden');
}

function showMainApp() {
    document.getElementById('loginScreen').classList.add('hidden');
    document.getElementById('mainApp').classList.remove('hidden');
    document.getElementById('userDisplay').textContent =
        `${currentUser.userName} (${currentUser.role})`;

    if (currentUser.role === 'Guest') {
        document.getElementById('addBugBtn').style.display = 'none';
    }

    loadBugs();
}

function setupLoginForm() {
    document.getElementById('loginForm').addEventListener('submit', async (e) => {
        e.preventDefault();
        const username = document.getElementById('loginUsername').value;
        const password = document.getElementById('loginPassword').value;
        const loginError = document.getElementById('loginError');
        loginError.textContent = '';

        try {
            const response = await fetch(`${API_URL}/auth/login`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ userName: username, password: password })
            });

            if (response.ok) {
                const data = await response.json();
                localStorage.setItem('token', data.token);
                localStorage.setItem('user', JSON.stringify(data.user));
                currentUser = data.user;
                showMainApp();
            } else {
                loginError.textContent = 'Invalid credentials';
            }
        } catch (error) {
            loginError.textContent = 'Login failed. Please try again.';
        }
    });
}

function logout() {
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    currentUser = null;
    showLoginScreen();
}

function authHeaders(extra = {}) {
    return {
        ...extra,
        Authorization: `Bearer ${localStorage.getItem('token')}`
    };
}

async function loadBugs() {
    try {
        const params = new URLSearchParams();
        const search = document.getElementById('searchInput').value;
        const severity = document.getElementById('severityFilter').value;
        const status = document.getElementById('statusFilter').value;

        if (search) params.append('search', search);
        if (severity) params.append('severity', severity);
        if (status) params.append('status', status);
        params.append('sortBy', sortColumn);
        params.append('desc', sortDesc);

        const response = await fetch(`${API_URL}/bugs?${params}`, {
            headers: authHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (response.ok) {
            bugs = await response.json();
            renderBugsTable();
        }
    } catch (error) {
        console.error('Error loading bugs:', error);
    }
}

function renderBugsTable() {
    const tbody = document.getElementById('bugsTableBody');
    const rows = bugs.$values || bugs;
    if (!rows || rows.length === 0) {
        tbody.innerHTML = '<tr><td colspan="11" style="text-align: center;">No bugs found</td></tr>';
        return;
    }

    tbody.innerHTML = rows.map(bug => {
        const severityClass = escapeAttr(String(bug.severity || '').toLowerCase());
        const statusClass = escapeAttr(String(bug.status || '').toLowerCase().replace(' ', ''));
        const description = String(bug.description || '');
        const shortDesc = description.length > 50 ? `${description.substring(0, 50)}...` : description;

        return `
                <tr>
                    <td>${escapeHtml(bug.id)}</td>
                    <td><strong>${escapeHtml(bug.title)}</strong><br><small>${escapeHtml(shortDesc)}</small></td>
                    <td>${escapeHtml(bug.module)}</td>
                    <td>${escapeHtml(bug.webPage)}</td>
                    <td><span class="badge badge-${severityClass}">${escapeHtml(bug.severity)}</span></td>
                    <td><span class="badge badge-${statusClass}">${escapeHtml(bug.status)}</span></td>
                    <td>${escapeHtml(new Date(bug.dateReported).toLocaleDateString())}</td>
                    <td>${bug.dateResolved ? escapeHtml(new Date(bug.dateResolved).toLocaleDateString()) : '-'}</td>
                    <td>${escapeHtml(bug.assignedTo)}</td>
                    <td>${escapeHtml(bug.eta)}</td>
                    <td>
                        <button class="btn btn-primary" style="padding: 5px 10px;" data-bug-id="${escapeAttr(bug.id)}" data-action="edit">Edit</button>
                    </td>
                </tr>
            `;
    }).join('');

    tbody.querySelectorAll('[data-action="edit"]').forEach(btn => {
        btn.addEventListener('click', () => editBug(Number(btn.dataset.bugId)));
    });
}

function sortTable(column) {
    if (sortColumn === column) {
        sortDesc = !sortDesc;
    } else {
        sortColumn = column;
        sortDesc = false;
    }
    loadBugs();
}

function applyFilters() {
    loadBugs();
}

function openBugModal() {
    if (currentUser.role === 'Guest') return;

    currentBugId = null;
    document.getElementById('modalTitle').textContent = 'Add Bug';
    document.getElementById('bugForm').reset();
    document.getElementById('bugId').value = '';
    document.getElementById('bugDateReported').value = new Date().toISOString().split('T')[0];
    document.getElementById('existingScreenshots').innerHTML = '';
    document.getElementById('commentsSection').style.display = 'none';
    document.getElementById('bugStatus').disabled = false;
    document.getElementById('addComments').style.display = '';
    document.getElementById('bugModal').classList.add('active');
}

function closeBugModal() {
    document.getElementById('bugModal').classList.remove('active');
}

function isSafeUploadPath(filePath) {
    return typeof filePath === 'string'
        && /^uploads\/[A-Za-z0-9._-]+$/.test(filePath);
}

async function editBug(id) {
    try {
        const response = await fetch(`${API_URL}/bugs/${id}`, {
            headers: authHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (response.ok) {
            const bug = await response.json();
            currentBugId = id;

            document.getElementById('modalTitle').textContent = 'Edit Bug';
            document.getElementById('bugId').value = bug.id;
            document.getElementById('bugTitle').value = bug.title;
            document.getElementById('bugDescription').value = bug.description;
            document.getElementById('bugModule').value = bug.module;
            document.getElementById('bugWebPage').value = bug.webPage;
            document.getElementById('bugSeverity').value = bug.severity;
            document.getElementById('bugStatus').value = bug.status;
            document.getElementById('bugDateReported').value = bug.dateReported.split('T')[0];
            document.getElementById('bugDateResolved').value = bug.dateResolved ? bug.dateResolved.split('T')[0] : '';
            document.getElementById('bugAssignedTo').value = bug.assignedTo;
            document.getElementById('bugETA').value = bug.eta;

            const isGuest = currentUser.role === 'Guest';
            document.getElementById('bugStatus').disabled = isGuest;
            document.getElementById('addComments').style.display = isGuest ? 'none' : '';

            const screenshotsContainer = document.getElementById('existingScreenshots');
            const screenshots = bug.screenshots?.$values || bug.screenshots || [];
            screenshotsContainer.innerHTML = '';
            screenshots.forEach(s => {
                if (!isSafeUploadPath(s.filePath)) return;

                const base = API_URL.replace(/\/api\/?$/, '');
                const url = `${base}/${s.filePath}`;

                const wrap = document.createElement('div');
                wrap.className = 'screenshot-thumb';

                const img = document.createElement('img');
                img.src = url;
                img.alt = 'Screenshot';
                img.addEventListener('click', () => previewImage(url));
                wrap.appendChild(img);

                if (!isGuest) {
                    const del = document.createElement('button');
                    del.type = 'button';
                    del.className = 'delete-screenshot';
                    del.textContent = '×';
                    del.addEventListener('click', () => deleteScreenshot(s.id));
                    wrap.appendChild(del);
                }

                screenshotsContainer.appendChild(wrap);
            });

            document.getElementById('commentsSection').style.display = 'block';
            const commentsList = document.getElementById('commentsList');
            const comments = bug.comments?.$values || bug.comments || [];
            commentsList.innerHTML = comments.map(c => `
                        <div class="comment">
                            <div class="comment-header">
                                <span class="comment-author">${escapeHtml(c.createdBy)}</span>
                                <span class="comment-date">${escapeHtml(new Date(c.createdAt).toLocaleString())}</span>
                            </div>
                            <div>${escapeHtml(c.comment)}</div>
                        </div>
                    `).join('');

            document.getElementById('bugModal').classList.add('active');
        }
    } catch (error) {
        console.error('Error loading bug:', error);
    }
}

function setupBugForm() {
    document.getElementById('bugForm').addEventListener('submit', async (e) => {
        e.preventDefault();
        if (currentUser.role === 'Guest') return;

        const formData = new FormData();
        formData.append('title', document.getElementById('bugTitle').value);
        formData.append('description', document.getElementById('bugDescription').value);
        formData.append('module', document.getElementById('bugModule').value);
        formData.append('webPage', document.getElementById('bugWebPage').value);
        formData.append('severity', document.getElementById('bugSeverity').value);
        formData.append('status', document.getElementById('bugStatus').value);
        formData.append('dateReported', document.getElementById('bugDateReported').value);
        formData.append('dateResolved', document.getElementById('bugDateResolved').value);
        formData.append('assignedTo', document.getElementById('bugAssignedTo').value);
        formData.append('eta', document.getElementById('bugETA').value);

        const files = document.getElementById('bugScreenshots').files;
        for (let i = 0; i < files.length; i++) {
            formData.append('screenshots', files[i]);
        }

        try {
            const url = currentBugId ? `${API_URL}/bugs/${currentBugId}` : `${API_URL}/bugs`;
            const method = currentBugId ? 'PUT' : 'POST';

            const response = await fetch(url, {
                method: method,
                headers: authHeaders(),
                body: formData
            });

            if (response.status === 401) {
                logout();
                return;
            }

            if (response.ok) {
                closeBugModal();
                loadBugs();
            } else {
                const err = await response.json().catch(() => ({}));
                alert(err.message || 'Failed to save bug.');
            }
        } catch (error) {
            console.error('Error saving bug:', error);
        }
    });
}

async function addComment() {
    if (currentUser.role === 'Guest') return;

    const comment = document.getElementById('newComment').value;
    if (!comment || !currentBugId) return;

    try {
        const response = await fetch(`${API_URL}/bugs/${currentBugId}/comments`, {
            method: 'POST',
            headers: authHeaders({ 'Content-Type': 'application/json' }),
            body: JSON.stringify({ comment })
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (response.ok) {
            document.getElementById('newComment').value = '';
            editBug(currentBugId);
        }
    } catch (error) {
        console.error('Error adding comment:', error);
    }
}

async function deleteScreenshot(id) {
    if (currentUser.role === 'Guest') return;
    if (!confirm('Delete this screenshot?')) return;

    try {
        const response = await fetch(`${API_URL}/bugs/screenshots/${id}`, {
            method: 'DELETE',
            headers: authHeaders()
        });

        if (response.status === 401) {
            logout();
            return;
        }

        if (response.ok) {
            editBug(currentBugId);
        }
    } catch (error) {
        console.error('Error deleting screenshot:', error);
    }
}

function previewImage(url) {
    document.getElementById('previewImage').src = url;
    document.getElementById('imagePreviewModal').classList.add('active');
}

function closeImagePreview() {
    document.getElementById('imagePreviewModal').classList.remove('active');
}
