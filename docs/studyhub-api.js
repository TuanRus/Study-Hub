// VI: Client mẫu cho frontend (ES module). Chép vào frontend/js/utils/ khi muốn chuyển từ localStorage sang API.
// EN: Sample frontend client (ES module). Copy into frontend/js/utils/ when moving from localStorage to the API.

const API_BASE = 'http://localhost:5080/api';
const SESSION_KEY = 'studyhub_session';

function getSession() {
    try {
        return JSON.parse(localStorage.getItem(SESSION_KEY)) || null;
    } catch {
        return null;
    }
}

function setSession(auth) {
    if (auth) localStorage.setItem(SESSION_KEY, JSON.stringify(auth));
    else localStorage.removeItem(SESSION_KEY);
    window.dispatchEvent(new CustomEvent('studyhub:auth'));
}

async function refreshTokens() {
    const session = getSession();
    if (!session) return false;
    const res = await fetch(`${API_BASE}/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: session.refreshToken })
    });
    if (!res.ok) {
        setSession(null);
        return false;
    }
    setSession(await res.json());
    return true;
}

// VI: Tự gắn token, tự refresh một lần khi gặp 401. Lỗi ném ra kèm ProblemDetails từ server.
// EN: Adds the token, refreshes once on 401. Errors are thrown with the server's ProblemDetails.
export async function api(path, { method = 'GET', body, retry = true } = {}) {
    const session = getSession();
    const headers = { 'Content-Type': 'application/json' };
    if (session) headers.Authorization = `Bearer ${session.accessToken}`;

    const res = await fetch(`${API_BASE}${path}`, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body)
    });

    if (res.status === 401 && retry && session && await refreshTokens()) {
        return api(path, { method, body, retry: false });
    }
    if (!res.ok) {
        const problem = await res.json().catch(() => ({ title: res.statusText }));
        throw Object.assign(new Error(problem.detail || problem.title), { status: res.status, problem });
    }
    return res.status === 204 ? null : res.json();
}

export const auth = {
    current: () => getSession()?.user ?? null,
    async register(username, email, password) {
        setSession(await api('/auth/register', { method: 'POST', body: { username, email, password } }));
    },
    async login(usernameOrEmail, password) {
        setSession(await api('/auth/login', { method: 'POST', body: { usernameOrEmail, password } }));
    },
    async logout() {
        const session = getSession();
        if (session) await api('/auth/logout', { method: 'POST', body: { refreshToken: session.refreshToken } }).catch(() => {});
        setSession(null);
    }
};

const tz = Intl.DateTimeFormat().resolvedOptions().timeZone;

export const tasks = {
    list: (status = 'All') => api(`/tasks?status=${status}&timeZone=${encodeURIComponent(tz)}`),
    calendar: (from, to) => api(`/tasks/calendar?from=${from}&to=${to}&timeZone=${encodeURIComponent(tz)}`),
    upcoming: (days = 7) => api(`/tasks/upcoming?days=${days}&timeZone=${encodeURIComponent(tz)}`),
    create: (task) => api('/tasks', { method: 'POST', body: task }),
    update: (id, task) => api(`/tasks/${id}`, { method: 'PUT', body: task }),
    schedule: (id, startAt, endAt, isAllDay = false) =>
        api(`/tasks/${id}/schedule`, { method: 'PATCH', body: { startAt, endAt, isAllDay } }),
    setCompleted: (id, isCompleted) => api(`/tasks/${id}/completion`, { method: 'PATCH', body: { isCompleted } }),
    remove: (id) => api(`/tasks/${id}`, { method: 'DELETE' })
};

export const focus = {
    log: (kind, startedAt, endedAt, completed = true, taskId = null) =>
        api('/focus-sessions', { method: 'POST', body: { kind, startedAt, endedAt, completed, taskId } }),
    stats: (days = 7) => api(`/stats/summary?days=${days}&timeZone=${encodeURIComponent(tz)}`),
    getSettings: () => api('/pomodoro/settings'),
    saveSettings: (settings) => api('/pomodoro/settings', { method: 'PUT', body: settings })
};

export const music = {
    resolve: (url) => api('/music/resolve', { method: 'POST', body: { url } }),
    playlists: () => api('/playlists'),
    playlist: (id) => api(`/playlists/${id}`),
    addTrack: (playlistId, url, title) => api(`/playlists/${playlistId}/tracks`, { method: 'POST', body: { url, title } }),
    removeTrack: (playlistId, trackId) => api(`/playlists/${playlistId}/tracks/${trackId}`, { method: 'DELETE' })
};

// VI: Đẩy dữ liệu localStorage cũ lên tài khoản (gọi một lần sau khi đăng nhập; gọi lại cũng không bị trùng).
// EN: Upload old localStorage data to the account (call once after login; repeating it creates no duplicates).
export function importLocalData() {
    const read = (key, fallback) => {
        try { return JSON.parse(localStorage.getItem(key)) ?? fallback; } catch { return fallback; }
    };
    return api('/import/local', {
        method: 'POST',
        body: {
            todos: read('studyhub_todos', []),
            music: read('studyhub_music', []),
            focusLog: read('studyhub_focus_log', {})
        }
    });
}
