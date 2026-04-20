const API_URL = 'https://localhost:44347/api'; // Update with your API URL
let currentUser = null;
let bugs = [];
let currentBugId = null;
let sortColumn = 'id';
let sortDesc = true;

// Initialize
document.addEventListener('DOMContentLoaded', () => {
    checkAuth();
    setupLoginForm();
    setupBugForm();
});

function checkAuth() {
    const token = localStorage.getItem('token');
    const user = localStorage.getItem('user');

    if (token && user) {
        currentUser = JSON.parse(user);
        showMainApp();
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
    document.getElementById('userDisplay').textContent = `${currentUser.userName} (${currentUser.role})`;

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
                document.getElementById('loginError').textContent = 'Invalid credentials';
            }
        } catch (error) {
            document.getElementById('loginError').textContent = 'Login failed. Please try again.';
        }
    });
}

function logout() {
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    currentUser = null;
    showLoginScreen();
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
            headers: { 'Authorization': `Bearer ${localStorage.getItem('token')}` }
        });

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
    if (bugs.length === 0) {
        tbody.innerHTML = '<tr><td colspan="11" style="text-align: center;">No bugs found</td></tr>';
        return;
    }

    tbody.innerHTML = bugs.$values.map(bug => `
                <tr>
                    <td>${bug.id}</td>
                    <td><strong>${bug.title}</strong><br><small>${bug.description.substring(0, 50)}...</small></td>
                    <td>${bug.module}</td>
                    <td>${bug.webPage}</td>
                    <td><span class="badge badge-${bug.severity.toLowerCase()}">${bug.severity}</span></td>
                    <td><span class="badge badge-${bug.status.toLowerCase().replace(' ', '')}">${bug.status}</span></td>
                    <td>${new Date(bug.dateReported).toLocaleDateString()}</td>
                    <td>${bug.dateResolved ? new Date(bug.dateResolved).toLocaleDateString() : '-'}</td>
                    <td>${bug.assignedTo}</td>
                    <td>${bug.eta}</td>
                    <td>
                        <button class="btn btn-primary" style="padding: 5px 10px;" onclick="editBug(${bug.id})">Edit</button>
                    </td>
                </tr>
            `).join('');
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
    document.getElementById('bugModal').classList.add('active');
}

function closeBugModal() {
    document.getElementById('bugModal').classList.remove('active');
}

async function editBug(id) {
    //if (currentUser.role === 'Guest') return;

    try {
        const response = await fetch(`${API_URL}/bugs/${id}`, {
            headers: { 'Authorization': `Bearer ${localStorage.getItem('token')}` }
        });

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

            if (currentUser.role === 'Guest') {
                document.getElementById('bugStatus').disabled = true;
                document.getElementById('addComments').style.display = 'none';
            }
            // Display existing screenshots
            const screenshotsContainer = document.getElementById('existingScreenshots');
            screenshotsContainer.innerHTML = bug.screenshots.$values.map(s => `
                        <div class="screenshot-thumb">
                            <img src="${API_URL.replace('/api', '')}/${s.filePath}" onclick="previewImage('${API_URL.replace('/api', '')}/${s.filePath}')">
                            <button class="delete-screenshot" onclick="deleteScreenshot(${s.id})" type="button">×</button>
                        </div>
                    `).join('');

            // Display comments
            document.getElementById('commentsSection').style.display = 'block';
            const commentsList = document.getElementById('commentsList');
            commentsList.innerHTML = bug.comments.$values.map(c => `
                        <div class="comment">
                            <div class="comment-header">
                                <span class="comment-author">${c.createdBy}</span>
                                <span class="comment-date">${new Date(c.createdAt).toLocaleString()}</span>
                            </div>
                            <div>${c.comment}</div>
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
        if (currentUser.role === 'Tester') return;
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
                headers: { 'Authorization': `Bearer ${localStorage.getItem('token')}` },
                body: formData
            });

            if (response.ok) {
                closeBugModal();
                loadBugs();
            }
        } catch (error) {
            console.error('Error saving bug:', error);
        }
    });
}

async function addComment() {
    const comment = document.getElementById('newComment').value;
    if (!comment || !currentBugId) return;

    try {
        const response = await fetch(`${API_URL}/bugs/${currentBugId}/comments`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${localStorage.getItem('token')}`
            },
            body: JSON.stringify({ comment })
        });

        if (response.ok) {
            document.getElementById('newComment').value = '';
            editBug(currentBugId);
        }
    } catch (error) {
        console.error('Error adding comment:', error);
    }
}

async function deleteScreenshot(id) {
    if (!confirm('Delete this screenshot?')) return;

    try {
        const response = await fetch(`${API_URL}/bugs/screenshots/${id}`, {
            method: 'DELETE',
            headers: { 'Authorization': `Bearer ${localStorage.getItem('token')}` }
        });

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