// VI: Client gọi Study Hub API: giữ phiên đăng nhập (JWT + refresh token), tự refresh khi gặp 401,
//     kiểm tra backend có chạy không, và hàng đợi ghi để các thay đổi gửi lên đúng thứ tự.
// EN: Study Hub API client: keeps the session (JWT + refresh token), refreshes on 401,
//     checks whether the backend is up, and a write queue so changes reach the server in order.

import { BACKEND_URL } from '../config.js';

const SESSION_KEY = 'studyhub_session';
const VISITOR_KEY = 'studyhub_visitor_id';
const HEALTH_TIMEOUT_MS = 2500;

export const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';

let backendOnline = false;
let sitePublicBaseUrl = '';
let refreshing = null;
let onSessionExpired = () => {};

export class ApiError extends Error {
    constructor(message, status, problem) {
        super(message);
        this.status = status;
        this.problem = problem;
        this.offline = status === 0;
    }
}

// ---------- Session ----------
export function getSession() {
    try {
        return JSON.parse(localStorage.getItem(SESSION_KEY)) || null;
    } catch (e) {
        return null;
    }
}

export function setSession(auth) {
    try {
        if (auth) localStorage.setItem(SESSION_KEY, JSON.stringify(auth));
        else localStorage.removeItem(SESSION_KEY);
    } catch (e) { /* ignore */ }
}

export function sessionUser() {
    return getSession()?.user ?? null;
}

// VI: Tên hiển thị của người đang đăng nhập (tài khoản server hoặc tài khoản offline trên máy).
// EN: Display name of whoever is signed in (server account or the offline on-device account).
export function currentUserName() {
    const user = sessionUser();
    if (user) return user.displayName || user.username;
    return localStorage.getItem('studyhub_currentUser');
}

export function setSessionExpiredHandler(handler) {
    onSessionExpired = handler;
}

// VI: true = đang dùng dữ liệu trên server. false = dùng localStorage (chưa đăng nhập hoặc backend tắt).
// EN: true = using server data. false = using localStorage (logged out or backend down).
export function isRemote() {
    return backendOnline && !!getSession();
}

export function isBackendOnline() {
    return backendOnline;
}

export async function checkBackend() {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), HEALTH_TIMEOUT_MS);
    try {
        const res = await fetch(`${BACKEND_URL}/health`, { signal: controller.signal, cache: 'no-store' });
        backendOnline = res.ok;
        if (backendOnline) await loadSiteInfo();
    } catch (e) {
        backendOnline = false;
    } finally {
        clearTimeout(timer);
    }
    return backendOnline;
}

// VI: PUBLIC_BASE_URL của server (vd. https://studyhub.example.com), rỗng nếu chưa đặt. Lỗi thì bỏ qua.
// EN: The server's PUBLIC_BASE_URL (e.g. https://studyhub.example.com), empty when unset. Errors are ignored.
async function loadSiteInfo() {
    try {
        const res = await fetch(`${BACKEND_URL}/api/site`, { cache: 'no-store' });
        if (res.ok) sitePublicBaseUrl = ((await res.json()).publicBaseUrl || '').replace(/\/+$/, '');
    } catch (e) { /* ignore */ }
}

export function publicBaseUrl() {
    return sitePublicBaseUrl;
}

// VI: Id ngẫu nhiên cố định cho trình duyệt này, để server đếm mỗi khách một lần mỗi ngày.
// EN: A stable random id for this browser so the server counts each visitor once per day.
export function visitorId() {
    try {
        let id = localStorage.getItem(VISITOR_KEY);
        if (!id) {
            id = crypto.randomUUID ? crypto.randomUUID() : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;
            localStorage.setItem(VISITOR_KEY, id);
        }
        return id;
    } catch (e) {
        return '';
    }
}

// ---------- Requests ----------
function problemMessage(problem, fallback) {
    if (problem?.errors) {
        const first = Object.values(problem.errors).flat()[0];
        if (first) return first;
    }
    return problem?.detail || problem?.title || fallback;
}

async function refreshTokens() {
    const session = getSession();
    if (!session?.refreshToken) return false;
    // VI: Nhiều request cùng gặp 401 thì chỉ refresh một lần. EN: Several 401s at once share one refresh.
    refreshing ||= (async () => {
        try {
            const res = await fetch(`${BACKEND_URL}/api/auth/refresh`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ refreshToken: session.refreshToken }),
            });
            if (res.status === 401) {
                setSession(null);
                onSessionExpired();
                return false;
            }
            if (!res.ok) return false;
            setSession(await res.json());
            return true;
        } catch (e) {
            return false;
        } finally {
            setTimeout(() => { refreshing = null; }, 0);
        }
    })();
    return refreshing;
}

export async function api(path, { method = 'GET', body, headers = {}, retry = true } = {}) {
    const session = getSession();
    const allHeaders = { ...headers };
    if (body !== undefined) allHeaders['Content-Type'] = 'application/json';
    if (session?.accessToken) allHeaders.Authorization = `Bearer ${session.accessToken}`;

    let res;
    try {
        res = await fetch(`${BACKEND_URL}/api${path}`, {
            method,
            headers: allHeaders,
            body: body === undefined ? undefined : JSON.stringify(body),
        });
    } catch (e) {
        backendOnline = false;
        throw new ApiError('Cannot reach the Study Hub server.', 0, null);
    }

    if (res.status === 401 && retry && session && await refreshTokens()) {
        return api(path, { method, body, headers, retry: false });
    }
    if (!res.ok) {
        const problem = await res.json().catch(() => null);
        throw new ApiError(problemMessage(problem, `Request failed (${res.status}).`), res.status, problem);
    }
    return res.status === 204 ? null : res.json();
}

// VI: Hàng đợi ghi: chạy lần lượt từng thao tác; lỗi được báo qua onError nhưng không chặn thao tác sau.
// EN: Write queue: runs operations one at a time; errors go to onError and don't block later operations.
let queue = Promise.resolve();

export function enqueue(operation, onError) {
    queue = queue.then(operation).catch((err) => {
        if (onError) onError(err);
        else console.error(err);
    });
    return queue;
}

// ---------- Auth ----------
export const authApi = {
    async login(usernameOrEmail, password) {
        const auth = await api('/auth/login', { method: 'POST', body: { usernameOrEmail, password }, retry: false });
        setSession(auth);
        return auth.user;
    },
    async register(username, email, password) {
        const auth = await api('/auth/register', { method: 'POST', body: { username, email, password, timeZone }, retry: false });
        setSession(auth);
        return auth.user;
    },
    async logout() {
        const session = getSession();
        setSession(null);
        if (session?.refreshToken) {
            await fetch(`${BACKEND_URL}/api/auth/logout`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ refreshToken: session.refreshToken }),
            }).catch(() => {});
        }
    },
};
