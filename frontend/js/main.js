// [EN] Main Entry Point
// [VN] Điểm truy cập chính khởi tạo ứng dụng

import { initAuth } from './modules/auth.js';
import { initTodo, fetchTodos } from './modules/todo.js';
import { initDeadlines, renderDeadlines } from './modules/deadlines.js';
import { initMusic } from './modules/music.js';
import { initPomodoro } from './modules/pomodoro.js';
import { initStopwatch } from './modules/stopwatch.js';
import { initTimer } from './modules/timer.js';
import { initDashboard, renderDashboard, updateGreeting } from './modules/dashboard.js';
import { initProfile, openProfile, initPublicProfile } from './modules/profile.js';
import { bootSync } from './modules/sync.js';
import { initAdmin, openAdmin, isAdmin } from './modules/admin.js';
import { initAccount } from './modules/account.js';
import { currentUserName } from './utils/api.js';

const VIEW_TITLES = {
    home: 'Today',
    pomodoro: 'Focus',
    deadlines: 'Deadlines',
    stopwatch: 'Stopwatch',
    timer: 'Timer',
    about: 'About',
    admin: 'Admin',
};

// [EN] Public profile link: /@username (when the backend serves the site) or ?u=username (Live Server)
// [VN] Link hồ sơ công khai: /@username (khi backend phục vụ trang) hoặc ?u=username (Live Server)
function getPublicSlug() {
    const fromPath = location.pathname.match(/\/@([A-Za-z0-9_.]{3,32})\/?$/);
    if (fromPath) return fromPath[1];
    const fromQuery = new URLSearchParams(location.search).get('u');
    return fromQuery && /^@?[A-Za-z0-9_.]{3,32}$/.test(fromQuery) ? fromQuery.replace(/^@/, '') : null;
}

// [EN] Visitors only see the About card, read-only
// [VN] Khách chỉ thấy thẻ About, không sửa được
function startPublicPage(slug) {
    document.body.classList.add('public-mode');
    document.querySelectorAll('.app-view').forEach(view => view.classList.toggle('active', view.id === 'view-about'));
    initTheme();
    initMusic();
    initPublicProfile(slug);
}

document.addEventListener('DOMContentLoaded', () => {
    const publicSlug = getPublicSlug();
    if (publicSlug) {
        startPublicPage(publicSlug);
        return;
    }

    // 1. Initialize all modular features / Khởi tạo các tính năng
    initAuth();
    initTodo();
    initDeadlines();
    initMusic();
    initPomodoro();
    initStopwatch();
    initTimer();
    initDashboard();
    initProfile();
    initAccount();
    initAdmin();
    initTheme();

    // [EN] Switch to server data when logged in and the backend is up (localStorage otherwise)
    // [VN] Chuyển sang dữ liệu trên server khi đã đăng nhập và backend chạy (không thì dùng localStorage)
    bootSync();

    // 2. Navigation Routing Logic / Xử lý chuyển đổi View
    const pageTitleEl = document.getElementById('page-title');

    function navigateTo(viewName, { updateHash = true } = {}) {
        const requested = viewName;
        // [EN] The admin page needs an admin account (the server checks too)
        // [VN] Trang admin cần tài khoản admin (server cũng kiểm tra)
        if (viewName === 'admin' && !isAdmin()) viewName = 'home';
        // [EN] About shows your own profile, so it needs a login (otherwise it shows the sample profile)
        // [VN] About là hồ sơ của chính mình nên cần đăng nhập (không thì chỉ hiện hồ sơ mẫu)
        if (viewName === 'about' && !currentUserName()) viewName = 'home';
        const newView = document.getElementById('view-' + viewName);
        if (!newView) return;

        document.querySelectorAll('.nav-link').forEach(link =>
            link.classList.toggle('active', link.getAttribute('data-view') === viewName));
        document.querySelectorAll('.app-view').forEach(view =>
            view.classList.toggle('active', view === newView));

        if (pageTitleEl) pageTitleEl.textContent = VIEW_TITLES[viewName] || 'Study Hub';
        if (updateHash || viewName !== requested) history.replaceState(null, '', '#' + viewName);

        // [EN] Refresh data when its view is opened
        // [VN] Tải lại dữ liệu khi view tương ứng được mở
        if (viewName === 'home') renderDashboard();
        if (viewName === 'pomodoro') fetchTodos();
        if (viewName === 'deadlines') renderDeadlines();
        if (viewName === 'about') openProfile();
        if (viewName === 'admin') openAdmin();
    }

    document.querySelectorAll('.nav-link').forEach(link => {
        link.addEventListener('click', (e) => {
            e.preventDefault();
            navigateTo(link.getAttribute('data-view'));
        });
    });

    // [EN] Shortcut buttons (e.g. "Start a focus session" on Today)
    // [VN] Các nút chuyển nhanh (vd: "Start a focus session" ở trang Today)
    document.querySelectorAll('[data-goto]').forEach(btn => {
        btn.addEventListener('click', () => navigateTo(btn.getAttribute('data-goto')));
    });

    // [EN] Open the view from the URL hash (keeps the page on refresh)
    // [VN] Mở view theo hash trên URL (giữ nguyên trang khi refresh)
    // [EN] /admin (served by the backend) opens the admin page once the account has loaded
    // [VN] /admin (do backend phục vụ) mở trang admin sau khi tải xong tài khoản
    const initialView = /\/admin\/?$/.test(location.pathname) ? 'admin' : location.hash.slice(1);
    navigateTo(VIEW_TITLES[initialView] && initialView !== 'admin' ? initialView : 'home', { updateHash: false });
    if (initialView === 'admin') {
        window.addEventListener('studyhub:synced', () => { if (isAdmin()) navigateTo('admin'); }, { once: true });
    }
    // [EN] Show the About menu item only when logged in
    // [VN] Chỉ hiện mục About trên menu khi đã đăng nhập
    function updateAboutNav() {
        const nav = document.getElementById('nav-about');
        if (nav) nav.hidden = !currentUserName();
    }
    updateAboutNav();
    window.addEventListener('studyhub:auth', () => {
        updateAboutNav();
        if (document.getElementById('view-admin')?.classList.contains('active') && !isAdmin()) navigateTo('home');
        if (document.getElementById('view-about')?.classList.contains('active') && !currentUserName()) navigateTo('home');
    });
    window.addEventListener('hashchange', () => {
        const view = location.hash.slice(1);
        if (VIEW_TITLES[view]) navigateTo(view, { updateHash: false });
    });

    // 3. Real-time Clock Logic / Logic đồng hồ thời gian thực
    const timeEl = document.getElementById('clock-time');
    const dateEl = document.getElementById('clock-date');
    let lastMinute = -1;

    function updateClock() {
        const now = new Date();
        const hours = String(now.getHours()).padStart(2, '0');
        const minutes = String(now.getMinutes()).padStart(2, '0');
        const seconds = String(now.getSeconds()).padStart(2, '0');
        if(timeEl) timeEl.textContent = `${hours}:${minutes}:${seconds}`;

        const options = { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' };
        if(dateEl) dateEl.textContent = now.toLocaleDateString('en-US', options).toUpperCase();

        if (now.getMinutes() !== lastMinute) {
            lastMinute = now.getMinutes();
            updateGreeting();
        }
    }

    if (timeEl) {
        updateClock();
        setInterval(updateClock, 1000);
    }

    // 4. Keyboard shortcuts / Phím tắt
    document.addEventListener('keydown', (e) => {
        // [EN] Escape closes any open dialog
        // [VN] Esc đóng hộp thoại đang mở
        if (e.key === 'Escape') {
            document.querySelectorAll('.modal-overlay:not(.auth-modal-overlay):not(.hidden)').forEach(m => m.classList.add('hidden'));
            document.getElementById('auth-modal')?.classList.remove('active');
            return;
        }

        // [EN] Space starts/pauses the timer of the current view
        // [VN] Space bắt đầu/tạm dừng đồng hồ của view hiện tại
        if (e.code === 'Space' && !e.target.closest('input, textarea, select, button, a, summary')) {
            const target = document.querySelector('.app-view.active [data-space-target]');
            if (target) {
                e.preventDefault();
                target.click();
            }
        }
    });

    // [EN] Click on the dark backdrop closes simple dialogs
    // [VN] Bấm ra nền tối để đóng các hộp thoại đơn giản
    document.querySelectorAll('.modal-overlay:not(.auth-modal-overlay)').forEach(overlay => {
        overlay.addEventListener('click', (e) => {
            // [EN] Ignore the second click of a double-click that just opened it
            // [VN] Bỏ qua cú click thứ hai của double-click vừa mở hộp thoại
            const justOpened = Date.now() - Number(overlay.dataset.openedAt || 0) < 400;
            if (e.target === overlay && !justOpened) overlay.classList.add('hidden');
        });
    });
});

// 5. Light / dark theme / Giao diện sáng - tối
function initTheme() {
    const toggleBtn = document.getElementById('theme-toggle');
    const root = document.documentElement;

    function applyIcon() {
        const isDark = root.dataset.theme !== 'light';
        if (toggleBtn) toggleBtn.innerHTML = `<i class="ph ${isDark ? 'ph-sun' : 'ph-moon'}"></i>`;
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content', isDark ? '#0f1116' : '#f4f5f8');
    }

    if (toggleBtn) {
        toggleBtn.addEventListener('click', () => {
            root.dataset.theme = root.dataset.theme === 'light' ? 'dark' : 'light';
            try { localStorage.setItem('studyhub_theme', root.dataset.theme); } catch (e) { /* ignore */ }
            applyIcon();
        });
    }
    applyIcon();
}
