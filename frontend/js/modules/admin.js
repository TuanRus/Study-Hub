// VI: Trang quản trị (chỉ tài khoản Admin): tổng quan server, quản lý user, ẩn hồ sơ vi phạm.
//     Server tự kiểm tra quyền ở mọi endpoint /api/admin; việc ẩn menu ở đây chỉ là giao diện.
// EN: Admin page (Admin accounts only): server overview, user management, profile moderation.
//     The server checks the role on every /api/admin endpoint; hiding the menu here is only cosmetic.

import { api, isRemote, sessionUser } from '../utils/api.js';
import { showToast } from '../utils/toast.js';
import { publicProfileUrl } from './profile.js';

const PAGE_SIZE = 20;
const state = { page: 1, search: '', total: 0 };
let searchTimer;

const RANK = { User: 0, Admin: 1, SuperAdmin: 2 };

export function isAdmin() {
    return isRemote() && RANK[sessionUser()?.role] >= RANK.Admin;
}

// VI: Chỉ SuperAdmin thêm/bỏ Admin. Admin chỉ quản lý user thường. (Server cũng kiểm tra y như vậy.)
// EN: Only a SuperAdmin adds/removes Admins. Admins only manage regular users. (The server enforces the same.)
function isSuperAdmin() {
    return isRemote() && sessionUser()?.role === 'SuperAdmin';
}

function canManage(user) {
    const me = sessionUser();
    return !!me && me.id !== user.id && (RANK[user.role] ?? 0) < (RANK[me.role] ?? 0);
}

// VI: Hiện/ẩn mục Admin trên menu theo quyền hiện tại. EN: Show/hide the Admin menu item for the current role.
export function updateAdminNav() {
    const nav = document.getElementById('nav-admin');
    if (nav) nav.hidden = !isAdmin();
}

function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
}

function formatBytes(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    const units = ['KB', 'MB', 'GB'];
    let value = bytes / 1024;
    let i = 0;
    while (value >= 1024 && i < units.length - 1) { value /= 1024; i++; }
    return `${value.toFixed(value < 10 ? 1 : 0)} ${units[i]}`;
}

function formatDuration(seconds) {
    const d = Math.floor(seconds / 86400);
    const h = Math.floor((seconds % 86400) / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    return d ? `${d}d ${h}h` : h ? `${h}h ${m}m` : `${m}m`;
}

function formatDate(iso) {
    return iso ? new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : 'Never';
}

// ---------- Overview ----------
function statCard(icon, tone, value, label) {
    const card = el('div', 'card stat-card');
    const iconEl = el('span', `stat-icon ${tone}`);
    iconEl.innerHTML = `<i class="ph-fill ${icon}"></i>`;
    const body = el('div');
    body.append(el('span', 'stat-value', value), el('span', 'stat-label', label));
    card.append(iconEl, body);
    return card;
}

function renderStats(s) {
    document.getElementById('admin-stats').replaceChildren(
        statCard('ph-users', 'mode-work', String(s.users), `Users · ${s.newUsersLast7Days} new, ${s.activeUsersLast7Days} active this week`),
        statCard('ph-check-circle', 'mode-long', `${s.tasksCompleted}/${s.tasks}`, 'Tasks done'),
        statCard('ph-clock', 'mode-short', `${Math.round(s.focusMinutes / 60)}h`, `Focus time · ${s.focusSessions} sessions`),
        statCard('ph-eye', 'mode-due', s.profileViews.toLocaleString('en-US'), `Profile views · ${s.profileViewsToday} today`),
    );

    const rows = [
        ['Health', s.databaseHealthy ? 'Healthy' : 'Database unreachable'],
        ['Uptime', formatDuration(s.uptimeSeconds)],
        ['Started', new Date(s.startedAt).toLocaleString('en-US')],
        ['Database', formatBytes(s.databaseBytes)],
        ['Memory', formatBytes(s.memoryBytes)],
        ['Admins', `${s.admins} + ${s.superAdmins} super`],
        ['Disabled users', String(s.disabledUsers)],
        ['Profiles', `${s.profiles} (${s.suspendedProfiles} hidden)`],
        ['Playlists', `${s.playlists} · ${s.tracks} tracks`],
        ['Environment', s.environment],
        ['Runtime', s.runtime],
        ['OS', s.os],
    ];
    const list = document.getElementById('admin-server');
    list.replaceChildren(...rows.flatMap(([k, v]) => [el('dt', '', k), el('dd', '', v)]));
    document.getElementById('admin-server-line').textContent =
        `${s.databaseHealthy ? 'Server healthy' : 'Database problem'} · up ${formatDuration(s.uptimeSeconds)}`;
}

// ---------- Users ----------
async function act(request, message) {
    try {
        await request();
        showToast(message, 'success', 2000);
    } catch (err) {
        showToast(err.message, 'error', 5000);
    }
    loadUsers();
    loadStats();
}

function actionButton(icon, title, onClick, danger = false) {
    const btn = el('button', `icon-btn icon-btn-sm${danger ? ' btn-danger-icon' : ''}`);
    btn.title = title;
    btn.setAttribute('aria-label', title);
    btn.innerHTML = `<i class="ph ${icon}"></i>`;
    btn.addEventListener('click', onClick);
    return btn;
}

function userRow(user) {
    const manageable = canManage(user);
    const tr = el('tr', user.isDisabled ? 'is-disabled' : '');

    const who = el('td');
    who.append(el('span', 'admin-user-name', user.displayName || user.username),
        el('span', 'admin-muted', `@${user.username} · ${user.email}`));

    const role = el('td');
    const roleLabel = user.role === 'SuperAdmin' ? 'Super admin' : user.role;
    role.append(el('span', `admin-badge${user.role !== 'User' ? ' is-admin' : ''}`, roleLabel));
    if (user.isDisabled) role.append(' ', el('span', 'admin-badge is-off', 'Disabled'));

    const activity = el('td');
    activity.append(el('span', '', `${user.taskCount} tasks · ${user.focusSessionCount} sessions`),
        el('span', 'admin-muted', `Last login: ${formatDate(user.lastLoginAt)}`));

    const profile = el('td');
    if (user.profileSlug) {
        const link = el('a', '', `@${user.profileSlug}`);
        link.href = publicProfileUrl(user.profileSlug);
        link.target = '_blank';
        link.rel = 'noopener';
        profile.append(link, el('span', 'admin-muted', `${user.profileViews} views`));
        if (user.profileSuspended) profile.append(el('span', 'admin-badge is-off', 'Hidden'));
    }

    const joined = el('td', '', formatDate(user.createdAt));

    const actions = el('td');
    const wrap = el('div', 'admin-actions');
    if (manageable && isSuperAdmin()) {
        const makeAdmin = user.role === 'User';
        wrap.append(actionButton(makeAdmin ? 'ph-shield-plus' : 'ph-shield-slash', makeAdmin ? 'Make admin' : 'Remove admin', () =>
            act(() => api(`/admin/users/${user.id}/role`, { method: 'PUT', body: { role: makeAdmin ? 'Admin' : 'User' } }),
                makeAdmin ? 'User is now an admin.' : 'Admin role removed.')));
    }
    if (manageable) {
        wrap.append(actionButton(user.isDisabled ? 'ph-lock-open' : 'ph-lock', user.isDisabled ? 'Enable account' : 'Disable account', () => {
            if (!user.isDisabled && !confirm(`Disable @${user.username}? They will be signed out everywhere.`)) return;
            act(() => api(`/admin/users/${user.id}/status`, { method: 'PUT', body: { isDisabled: !user.isDisabled } }),
                user.isDisabled ? 'Account enabled.' : 'Account disabled.');
        }));
    }
    if (manageable && user.profileSlug) {
        wrap.append(actionButton(user.profileSuspended ? 'ph-eye' : 'ph-eye-slash',
            user.profileSuspended ? 'Restore public profile' : 'Hide public profile', () =>
                act(() => api(`/admin/profiles/${user.id}/moderation`, { method: 'PUT', body: { isSuspended: !user.profileSuspended } }),
                    user.profileSuspended ? 'Profile restored.' : 'Profile hidden.')));
    }
    if (manageable) {
        wrap.append(actionButton('ph-trash', 'Delete user', () => {
            if (!confirm(`Delete @${user.username} and all their data? This cannot be undone.`)) return;
            act(() => api(`/admin/users/${user.id}`, { method: 'DELETE' }), 'User deleted.');
        }, true));
    }
    actions.append(wrap);

    tr.append(who, role, activity, profile, joined, actions);
    return tr;
}

async function loadUsers() {
    const query = new URLSearchParams({ page: state.page, pageSize: PAGE_SIZE });
    if (state.search) query.set('search', state.search);
    try {
        const page = await api(`/admin/users?${query}`);
        state.total = page.total;
        const body = document.getElementById('admin-users');
        body.replaceChildren(...page.items.map(userRow));
        if (!page.items.length) {
            const tr = el('tr');
            const td = el('td', 'admin-muted', 'No users found.');
            td.colSpan = 6;
            tr.append(td);
            body.append(tr);
        }
        const pages = Math.max(1, Math.ceil(page.total / PAGE_SIZE));
        document.getElementById('admin-user-total').textContent = page.total;
        document.getElementById('admin-page').textContent = `${state.page} / ${pages}`;
        document.getElementById('admin-prev').disabled = state.page <= 1;
        document.getElementById('admin-next').disabled = state.page >= pages;
    } catch (err) {
        showToast(err.message, 'error');
    }
}

async function loadStats() {
    try {
        renderStats(await api('/admin/stats'));
    } catch (err) {
        showToast(err.message, 'error');
    }
}

export function openAdmin() {
    if (!isAdmin()) return;
    loadStats();
    loadUsers();
}

export function initAdmin() {
    document.getElementById('admin-refresh')?.addEventListener('click', openAdmin);
    document.getElementById('admin-search')?.addEventListener('input', (e) => {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(() => {
            state.search = e.target.value.trim();
            state.page = 1;
            loadUsers();
        }, 300);
    });
    document.getElementById('admin-prev')?.addEventListener('click', () => { state.page--; loadUsers(); });
    document.getElementById('admin-next')?.addEventListener('click', () => { state.page++; loadUsers(); });
    window.addEventListener('studyhub:auth', updateAdminNav);
    updateAdminNav();
}
