// VI: Trang quản trị (chỉ tài khoản Admin): tổng quan server, quản lý user, ẩn hồ sơ vi phạm,
//     đánh dấu sao tài khoản lạm dụng, dọn dữ liệu rác và ban IP.
//     Server tự kiểm tra quyền ở mọi endpoint /api/admin; việc ẩn menu ở đây chỉ là giao diện.
// EN: Admin page (Admin accounts only): server overview, user management, profile moderation,
//     starring abusive accounts, cleaning up junk data and IP bans.
//     The server checks the role on every /api/admin endpoint; hiding the menu here is only cosmetic.

import { api, isRemote, sessionUser } from '../utils/api.js';
import { showToast } from '../utils/toast.js';
import { publicProfileUrl } from './profile.js';

const PAGE_SIZE = 20;
const state = { page: 1, search: '', role: '', status: '', total: 0 };
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
function renderStats(s) {
    const rows = [
        ['Health', s.databaseHealthy ? 'Healthy' : 'Database unreachable'],
        ['Uptime', formatDuration(s.uptimeSeconds)],
        ['Started', new Date(s.startedAt).toLocaleString('en-US')],
        ['Users', `${s.users} (${s.newUsersLast7Days} new this week)`],
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
        const result = await request();
        showToast(typeof message === 'function' ? message(result) : message, 'success', 2500);
    } catch (err) {
        showToast(err.message, 'error', 5000);
    }
    loadUsers();
    loadStats();
    loadBans();
    loadReports();
}

function actionButton(icon, title, onClick, danger = false) {
    const btn = el('button', `icon-btn icon-btn-sm${danger ? ' btn-danger-icon' : ''}`);
    btn.title = title;
    btn.setAttribute('aria-label', title);
    btn.innerHTML = `<i class="${icon.startsWith('ph-fill') ? icon : `ph ${icon}`}"></i>`;
    btn.addEventListener('click', onClick);
    return btn;
}

// VI: Chỉ một menu mở tại một thời điểm; bấm ra ngoài hoặc Esc để đóng. EN: One menu open at a time; click outside or Esc closes it.
function closeMenus() {
    document.querySelectorAll('.admin-menu.open').forEach((m) => m.classList.remove('open'));
}

function moreMenu(items) {
    const box = el('div', 'admin-more');
    const toggle = actionButton('ph-dots-three', 'More actions', (e) => {
        e.stopPropagation();
        const open = menu.classList.contains('open');
        closeMenus();
        if (open) return;
        // VI: Menu dùng position: fixed để không bị khung cuộn của bảng cắt; mở lên trên khi gần đáy màn hình.
        // EN: The menu is position: fixed so the table's scroll box cannot clip it; it opens upwards near the bottom.
        menu.classList.add('open');
        const r = toggle.getBoundingClientRect();
        const h = menu.offsetHeight;
        const top = r.bottom + 4 + h > window.innerHeight ? r.top - 4 - h : r.bottom + 4;
        menu.style.top = `${Math.max(8, top)}px`;
        menu.style.left = `${Math.max(8, r.right - menu.offsetWidth)}px`;
    });
    const menu = el('div', 'admin-menu');
    menu.setAttribute('role', 'menu');
    for (const [icon, label, onClick, danger] of items) {
        const item = el('button', `admin-menu-item${danger ? ' is-danger' : ''}`);
        item.type = 'button';
        item.setAttribute('role', 'menuitem');
        item.innerHTML = `<i class="ph ${icon}"></i>`;
        item.append(label);
        item.addEventListener('click', () => { closeMenus(); onClick(); });
        menu.append(item);
    }
    box.append(toggle, menu);
    return box;
}

function userRow(user) {
    const manageable = canManage(user);
    const tr = el('tr', user.isDisabled ? 'is-disabled' : '');

    const who = el('td');
    const name = el('span', 'admin-user-name', user.displayName || user.username);
    if (user.isFlagged) {
        const star = el('i', 'ph-fill ph-star admin-star');
        star.dataset.tip = user.flagReason ? `Starred: ${user.flagReason}` : 'Starred';
        star.setAttribute('aria-label', star.dataset.tip);
        name.prepend(star);
    }
    who.append(name, el('span', 'admin-muted', `@${user.username} · ${user.email}`));

    const role = el('td');
    const roleLabel = user.role === 'SuperAdmin' ? 'Super admin' : user.role;
    role.append(el('span', `admin-badge${user.role !== 'User' ? ' is-admin' : ''}`, roleLabel));
    if (user.isDisabled) role.append(' ', el('span', 'admin-badge is-off', 'Disabled'));
    if (user.isSuspicious) role.append(' ', el('span', 'admin-badge is-warn', 'Suspicious'));
    if (user.ipBanned) role.append(' ', el('span', 'admin-badge is-off', 'IP banned'));
    if (user.openReports) {
        role.append(' ', el('span', 'admin-badge is-warn', `${user.openReports} report${user.openReports > 1 ? 's' : ''}`));
    }

    const activity = el('td');
    activity.append(el('span', 'admin-nowrap', `${user.taskCount} tasks · ${user.focusSessionCount} sessions`));
    if (user.tasksLast24h) {
        activity.append(el('span', `admin-muted admin-nowrap${user.isSuspicious ? ' is-warn' : ''}`, `${user.tasksLast24h} new in 24h`));
    }
    activity.append(el('span', 'admin-muted admin-nowrap', `Last login: ${formatDate(user.lastLoginAt)}`));
    if (user.lastIp) {
        const ip = el('span', 'admin-muted admin-ip admin-nowrap', `IP ${user.lastIp}`);
        if (user.registerIp && user.registerIp !== user.lastIp) ip.title = `Signed up from ${user.registerIp}`;
        activity.append(ip);
    }

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
    if (manageable) {
        const starBtn = actionButton(user.isFlagged ? 'ph-fill ph-star' : 'ph-star', user.isFlagged ? 'Remove star' : 'Star as abusive', () => {
            if (user.isFlagged) {
                act(() => api(`/admin/users/${user.id}/flag`, { method: 'PUT', body: { isFlagged: false } }), 'Star removed.');
            } else {
                openReason('flag', user);
            }
        });
        starBtn.classList.toggle('admin-starred-btn', user.isFlagged);
        wrap.append(starBtn);
        wrap.append(actionButton(user.isDisabled ? 'ph-lock-open' : 'ph-lock', user.isDisabled ? 'Enable account' : 'Disable account', () => {
            if (!user.isDisabled && !confirm(`Disable @${user.username}? They will be signed out everywhere.`)) return;
            act(() => api(`/admin/users/${user.id}/status`, { method: 'PUT', body: { isDisabled: !user.isDisabled } }),
                user.isDisabled ? 'Account enabled.' : 'Account disabled.');
        }));

        // VI: Thao tác ít dùng gom vào menu "..." cho bảng gọn. EN: Less common actions live in a "..." menu to keep the table tidy.
        const items = [];
        if (isSuperAdmin()) {
            const makeAdmin = user.role === 'User';
            items.push([makeAdmin ? 'ph-shield-plus' : 'ph-shield-slash', makeAdmin ? 'Make admin' : 'Remove admin', () =>
                act(() => api(`/admin/users/${user.id}/role`, { method: 'PUT', body: { role: makeAdmin ? 'Admin' : 'User' } }),
                    makeAdmin ? 'User is now an admin.' : 'Admin role removed.')]);
        }
        if (user.profileSlug) {
            items.push([user.profileSuspended ? 'ph-eye' : 'ph-eye-slash',
                user.profileSuspended ? 'Restore public profile' : 'Hide public profile', () =>
                    act(() => api(`/admin/profiles/${user.id}/moderation`, { method: 'PUT', body: { isSuspended: !user.profileSuspended } }),
                        user.profileSuspended ? 'Profile restored.' : 'Profile hidden.')]);
        }
        items.push(['ph-broom', 'Clean up junk data', () => openCleanup(user)]);
        if (!user.ipBanned) items.push(['ph-prohibit', 'Ban IP', () => openReason('ban', user), true]);
        items.push(['ph-trash', 'Delete user', () => {
            if (!confirm(`Delete @${user.username} and all their data? This cannot be undone.`)) return;
            act(() => api(`/admin/users/${user.id}`, { method: 'DELETE' }), 'User deleted.');
        }, true]);
        wrap.append(moreMenu(items));
    }
    actions.append(wrap);

    tr.append(who, role, activity, profile, joined, actions);
    return tr;
}

async function loadUsers() {
    const query = new URLSearchParams({ page: state.page, pageSize: PAGE_SIZE });
    if (state.search) query.set('search', state.search);
    if (state.role) query.set('role', state.role);
    if (state.status) query.set('status', state.status);
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

// ---------- Star / ban / cleanup dialogs ----------
let dialogUser = null;
let reasonMode = 'flag';

function closeModal(id) {
    document.getElementById(id)?.classList.add('hidden');
    dialogUser = null;
}

function openReason(mode, user) {
    dialogUser = user;
    reasonMode = mode;
    const ban = mode === 'ban';
    const ips = [...new Set([user.lastIp, user.registerIp].filter(Boolean))];
    document.getElementById('admin-reason-title').innerHTML = ban
        ? '<i class="ph-fill ph-prohibit admin-danger-icon"></i> Ban IP'
        : '<i class="ph-fill ph-star"></i> Star account';
    document.getElementById('admin-reason-text').textContent = ban
        ? (ips.length
            ? `Block every request from ${ips.join(' and ')} (@${user.username}). New accounts from that network are blocked too.`
            : `No IP recorded for @${user.username} yet. It is saved at their next sign-in.`)
        : `Mark @${user.username} as abusing the system. It is only a marker you can filter by.`;
    document.getElementById('admin-reason-disable-wrap').hidden = !ban;
    document.getElementById('admin-reason-submit').textContent = ban ? 'Ban IP' : 'Star';
    document.getElementById('admin-reason-submit').classList.toggle('btn-danger-fill', ban);
    document.getElementById('admin-reason').value = !ban || !user.flagReason ? '' : user.flagReason;
    document.getElementById('admin-reason-disable').checked = true;
    document.getElementById('admin-reason-modal').classList.remove('hidden');
    document.getElementById('admin-reason').focus();
}

function openCleanup(user) {
    dialogUser = user;
    const form = document.getElementById('admin-cleanup-form');
    form.reset();
    document.getElementById('admin-cleanup-text').textContent = `Delete junk created by @${user.username}.`;
    document.getElementById('admin-cleanup-tasks').textContent = `(${user.taskCount} total, ${user.tasksLast24h} in 24h)`;
    document.getElementById('admin-cleanup-sessions').textContent = `(${user.focusSessionCount})`;
    document.getElementById('admin-cleanup-modal').classList.remove('hidden');
}

function initDialogs() {
    for (const id of ['admin-reason-modal', 'admin-cleanup-modal']) {
        const modal = document.getElementById(id);
        modal?.querySelectorAll('[data-close]').forEach((btn) => btn.addEventListener('click', () => closeModal(id)));
        modal?.addEventListener('click', (e) => { if (e.target === modal) closeModal(id); });
    }

    document.getElementById('admin-reason-form')?.addEventListener('submit', (e) => {
        e.preventDefault();
        const user = dialogUser;
        if (!user) return;
        const reason = document.getElementById('admin-reason').value.trim() || null;
        if (reasonMode === 'ban') {
            const disableAccount = document.getElementById('admin-reason-disable').checked;
            act(() => api(`/admin/users/${user.id}/ban-ip`, { method: 'POST', body: { reason, disableAccount } }), 'IP banned.');
        } else {
            act(() => api(`/admin/users/${user.id}/flag`, { method: 'PUT', body: { isFlagged: true, reason } }), 'Account starred.');
        }
        closeModal('admin-reason-modal');
    });

    document.getElementById('admin-cleanup-form')?.addEventListener('submit', (e) => {
        e.preventDefault();
        const user = dialogUser;
        if (!user) return;
        const form = e.currentTarget;
        const body = Object.fromEntries(['tasks', 'focusSessions', 'tracks', 'profile'].map((k) => [k, form.elements[k].checked]));
        if (!Object.values(body).some(Boolean)) {
            showToast('Pick at least one kind of data.', 'error');
            return;
        }
        const hours = document.getElementById('admin-cleanup-range').value;
        body.lastHours = hours ? Number(hours) : null;
        closeModal('admin-cleanup-modal');
        act(() => api(`/admin/users/${user.id}/cleanup`, { method: 'POST', body }), (r) => {
            const parts = [`${r.tasks} tasks`, `${r.focusSessions} sessions`, `${r.tracks} tracks`];
            if (r.profile) parts.push('profile');
            return `Deleted ${parts.join(', ')}.`;
        });
    });
}

// ---------- Reports from users ----------
const REASONS = {
    Spam: 'Spam',
    Abuse: 'Abusing the system',
    Inappropriate: 'Inappropriate profile',
    Impersonation: 'Impersonation',
    Other: 'Other',
};
let reportFilter = 'Open';

async function loadReports() {
    const list = document.getElementById('admin-reports');
    if (!list) return;
    try {
        const page = await api(`/admin/reports${reportFilter ? `?status=${reportFilter}` : ''}`);
        document.getElementById('admin-report-open').textContent = page.open;
        list.replaceChildren(...page.items.map((r) => {
            const li = el('li', 'admin-ban admin-report');
            const info = el('div');
            const target = el('a', 'admin-report-target', `@${r.reportedSlug || r.reportedUsername}`);
            target.href = r.reportedSlug ? publicProfileUrl(r.reportedSlug) : '#';
            target.target = '_blank';
            target.rel = 'noopener';
            const head = el('span', 'admin-report-head');
            head.append(target, el('span', 'admin-badge is-warn', REASONS[r.reason] || r.reason));
            info.append(head);
            if (r.details) info.append(el('span', 'admin-report-details', r.details));
            const meta = [`by @${r.reporterUsername}`, formatDate(r.createdAt)];
            if (r.status !== 'Open') meta.push(`${r.status.toLowerCase()} by ${r.handledBy}`);
            info.append(el('span', 'admin-muted', meta.join(' · ')));

            const actions = el('div', 'admin-actions');
            // VI: Tìm user bị báo cáo trong bảng Users. EN: Show the reported user in the Users table.
            actions.append(actionButton('ph-magnifying-glass', 'Show in users', () => {
                const search = document.getElementById('admin-search');
                search.value = r.reportedUsername;
                state.search = r.reportedUsername;
                state.page = 1;
                loadUsers();
                search.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }));
            const setStatus = (status, msg) => act(() => api(`/admin/reports/${r.id}`, { method: 'PUT', body: { status } }), msg);
            if (r.status === 'Open') {
                actions.append(actionButton('ph-check', 'Mark resolved', () => setStatus('Resolved', 'Report resolved.')),
                    actionButton('ph-x', 'Dismiss', () => setStatus('Dismissed', 'Report dismissed.')));
            } else {
                actions.append(actionButton('ph-arrow-counter-clockwise', 'Reopen', () => setStatus('Open', 'Report reopened.')));
            }
            li.append(info, actions);
            return li;
        }));
        if (!page.items.length) list.append(el('li', 'admin-muted', reportFilter === 'Open' ? 'No open reports.' : 'No reports.'));
    } catch (err) {
        showToast(err.message, 'error');
    }
}

// ---------- Auto star settings ----------
const SETTING_FIELDS = [
    ['abuse-star-tasks', 'suspiciousTasksPerDay'],
    ['abuse-reports', 'reportsToFlag'],
    ['abuse-max-tasks', 'maxTasksPerDay'],
];

async function loadSettings() {
    const form = document.getElementById('admin-settings-form');
    if (!form) return;
    try {
        const s = await api('/admin/settings/abuse');
        for (const [id, key] of SETTING_FIELDS) {
            const input = document.getElementById(id);
            input.value = s[key];
            input.disabled = !isSuperAdmin();
        }
        document.getElementById('abuse-settings-save').hidden = !isSuperAdmin();
        document.getElementById('abuse-settings-note').textContent = [
            '0 turns a rule off.',
            s.updatedBy ? `Changed by ${s.updatedBy}, ${formatDate(s.updatedAt)}.` : 'Using the default values.',
            isSuperAdmin() ? '' : 'Only super admins can change these.',
        ].filter(Boolean).join(' ');
    } catch (err) {
        showToast(err.message, 'error');
    }
}

// ---------- Banned IPs ----------
async function loadBans() {
    const list = document.getElementById('admin-bans');
    if (!list) return;
    try {
        const bans = await api('/admin/bans');
        document.getElementById('admin-ban-total').textContent = bans.length;
        list.replaceChildren(...bans.map((ban) => {
            const li = el('li', 'admin-ban');
            const info = el('div');
            info.append(el('span', 'admin-ip', ban.ip),
                el('span', 'admin-muted', [ban.username && `@${ban.username}`, ban.reason, `by ${ban.bannedBy}`, formatDate(ban.createdAt)]
                    .filter(Boolean).join(' · ')));
            li.append(info, actionButton('ph-arrow-counter-clockwise', 'Unban', () =>
                act(() => api(`/admin/bans/${ban.id}`, { method: 'DELETE' }), 'IP unbanned.')));
            return li;
        }));
        if (!bans.length) list.append(el('li', 'admin-muted', 'No banned IPs.'));
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
    loadBans();
    loadReports();
    loadSettings();
}

export function initAdmin() {
    initDialogs();
    document.addEventListener('click', (e) => { if (!e.target.closest?.('.admin-more')) closeMenus(); });
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeMenus(); });
    document.addEventListener('scroll', closeMenus, true);
    window.addEventListener('resize', closeMenus);
    document.getElementById('admin-refresh')?.addEventListener('click', openAdmin);
    document.getElementById('admin-search')?.addEventListener('input', (e) => {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(() => {
            state.search = e.target.value.trim();
            state.page = 1;
            loadUsers();
        }, 300);
    });
    for (const [id, key] of [['admin-role-filter', 'role'], ['admin-status-filter', 'status']]) {
        document.getElementById(id)?.addEventListener('change', (e) => {
            state[key] = e.target.value;
            state.page = 1;
            loadUsers();
        });
    }
    document.getElementById('admin-report-filter')?.addEventListener('change', (e) => {
        reportFilter = e.target.value;
        loadReports();
    });
    document.getElementById('admin-settings-form')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const body = Object.fromEntries(SETTING_FIELDS.map(([id, key]) => [key, Number(document.getElementById(id).value)]));
        try {
            await api('/admin/settings/abuse', { method: 'PUT', body });
            showToast('Auto star settings saved.', 'success', 2500);
            loadSettings();
            loadUsers();
        } catch (err) {
            showToast(err.message, 'error', 5000);
        }
    });
    document.getElementById('admin-ban-form')?.addEventListener('submit', (e) => {
        e.preventDefault();
        const input = document.getElementById('admin-ban-ip');
        const ip = input.value.trim();
        if (!ip) return;
        act(async () => {
            await api('/admin/bans', { method: 'POST', body: { ip } });
            input.value = '';
        }, 'IP banned.');
    });
    document.getElementById('admin-prev')?.addEventListener('click', () => { state.page--; loadUsers(); });
    document.getElementById('admin-next')?.addEventListener('click', () => { state.page++; loadUsers(); });
    window.addEventListener('studyhub:auth', updateAdminNav);
    updateAdminNav();
}
