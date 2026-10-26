// VI: Trang About dạng bio-link (giống guns.lol): nền tuỳ chỉnh, thẻ kính mờ, tên phát sáng, bio gõ chữ,
//     huy hiệu, link mạng xã hội, lượt xem, trạng thái học kiểu Discord. Hồ sơ sửa được và lưu localStorage.
//     Khi đăng nhập: hồ sơ lưu trên server (/api/profile) và có link công khai /@username với lượt xem thật.
// EN: Bio-link style About page (like guns.lol): custom background, glass card, glowing name, typewriter bio,
//     badges, social links, view counter and a Discord-like study status. Editable, saved in localStorage.
//     When logged in: stored on the server (/api/profile) with a public /@username link and real view counts.

import { getStorageData, setStorageData } from '../utils/storage.js';
import { getTotalFocusMinutes } from '../utils/focusLog.js';
import { api, isRemote, visitorId, publicBaseUrl, currentUserName } from '../utils/api.js';
import { SERVED_BY_BACKEND } from '../config.js';
import { showToast } from '../utils/toast.js';
import { getTodos } from './todo.js';
import { playMusic } from './music.js';

export const PROFILE_KEY = 'studyhub_profile';
const VIEWS_KEY = 'studyhub_profile_views';

const LINK_TYPES = {
    facebook: { label: 'Facebook', icon: 'ph-facebook-logo' },
    discord: { label: 'Discord', icon: 'ph-discord-logo' },
    github: { label: 'GitHub', icon: 'ph-github-logo' },
    linkedin: { label: 'LinkedIn', icon: 'ph-linkedin-logo' },
    instagram: { label: 'Instagram', icon: 'ph-instagram-logo' },
    tiktok: { label: 'TikTok', icon: 'ph-tiktok-logo' },
    youtube: { label: 'YouTube', icon: 'ph-youtube-logo' },
    spotify: { label: 'Spotify', icon: 'ph-spotify-logo' },
    website: { label: 'Website', icon: 'ph-globe' },
};

const BADGES = {
    student: { label: 'Student', icon: 'ph-student' },
    focus: { label: 'Focus master', icon: 'ph-fire' },
    night: { label: 'Night owl', icon: 'ph-moon-stars' },
    coder: { label: 'Developer', icon: 'ph-code' },
    music: { label: 'Music lover', icon: 'ph-headphones' },
    earlybird: { label: 'Early bird', icon: 'ph-sun-horizon' },
    bookworm: { label: 'Bookworm', icon: 'ph-books' },
    math: { label: 'Math lover', icon: 'ph-math-operations' },
    science: { label: 'Scientist', icon: 'ph-flask' },
    language: { label: 'Language learner', icon: 'ph-translate' },
    writer: { label: 'Writer', icon: 'ph-pen-nib' },
    artist: { label: 'Artist', icon: 'ph-palette' },
    gamer: { label: 'Gamer', icon: 'ph-game-controller' },
    athlete: { label: 'Athlete', icon: 'ph-barbell' },
    coffee: { label: 'Coffee addict', icon: 'ph-coffee' },
    streak: { label: 'On a streak', icon: 'ph-lightning' },
    goal: { label: 'Goal crusher', icon: 'ph-target' },
    exam: { label: 'Exam warrior', icon: 'ph-exam' },
    team: { label: 'Team player', icon: 'ph-users-three' },
    traveler: { label: 'Traveler', icon: 'ph-airplane-tilt' },
};

// VI: Hồ sơ mẫu (test) hiển thị khi chưa đăng nhập và chưa sửa hồ sơ. Không chứa thông tin cá nhân thật.
// EN: Sample (test) profile shown before signing in or editing. Contains no real personal data.
const DEFAULT_PROFILE = {
    name: 'Test User',
    handle: 'testuser',
    avatar: '',
    bio: 'Hello! This is my information page.\nStudying one Pomodoro at a time.\nWelcome to my Study Hub ✨',
    location: 'Vietnam',
    role: 'Developer',
    badges: ['student', 'coder', 'focus'],
    background: 'aurora',
    backgroundUrl: '',
    effect: 'stars',
    accent: '#8b7bff',
    song: '',
    links: {
        facebook: 'https://example.com/facebook/testuser',
        discord: 'https://example.com/discord/testuser',
    },
};

// VI: Các trường của hồ sơ trống (tài khoản mới), người dùng tự điền sau.
// EN: Fields of a blank profile (new account); the user fills them in later.
const BLANK_FIELDS = {
    bio: '', location: '', role: '', avatar: '', badges: [], background: 'aurora',
    backgroundUrl: '', effect: 'stars', accent: '#8b7bff', song: '', links: {},
};

let typewriterRun = 0;
let presenceTimer;
// VI: Hồ sơ trên server (ProfileDto), null = dùng localStorage. EN: Server profile (ProfileDto), null = use localStorage.
let remoteProfile = null;

// VI: Đổi ProfileDto của server sang dạng hồ sơ của FE. EN: Map the server's ProfileDto to the FE profile shape.
function fromServer(dto) {
    return {
        name: dto.displayName,
        handle: dto.slug,
        avatar: dto.avatarUrl || '',
        bio: dto.bio || '',
        location: dto.location || '',
        role: dto.role || '',
        badges: dto.badges || [],
        background: dto.background,
        backgroundUrl: dto.backgroundUrl || '',
        effect: dto.effect,
        accent: dto.accentColor,
        song: dto.songUrl || '',
        links: { ...(dto.links || {}) },
    };
}

function toServer(profile, isPublic = true) {
    return {
        slug: profile.handle,
        displayName: profile.name,
        avatarUrl: profile.avatar || null,
        bio: profile.bio,
        location: profile.location || null,
        role: profile.role || null,
        badges: profile.badges,
        background: profile.background,
        backgroundUrl: profile.backgroundUrl || null,
        effect: profile.effect,
        accentColor: profile.accent,
        songUrl: profile.song || null,
        links: profile.links,
        isPublic,
    };
}

// VI: Hồ sơ trống cho tài khoản mới: chỉ có tên và username. EN: Blank profile for a new account: just name and username.
function blankProfile(username) {
    return {
        ...DEFAULT_PROFILE, ...BLANK_FIELDS,
        name: username,
        handle: username.toLowerCase().replace(/[^a-z0-9_.]/g, ''),
    };
}

function getProfile() {
    if (remoteProfile) return fromServer(remoteProfile);
    const saved = getStorageData(PROFILE_KEY, null);
    const user = currentUserName();
    if (!saved && user) return blankProfile(user);
    return { ...DEFAULT_PROFILE, ...(saved || {}), links: { ...(saved ? saved.links : DEFAULT_PROFILE.links) } };
}

// VI: Ưu tiên PUBLIC_BASE_URL của server (domain thật), sau đó origin hiện tại (đúng domain khi mở qua proxy).
// EN: Prefer the server's PUBLIC_BASE_URL (the real domain), then the current origin (right domain when opened via the proxy).
export function publicProfileUrl(slug) {
    if (publicBaseUrl()) return `${publicBaseUrl()}/@${slug}`;
    const base = location.origin + location.pathname.replace(/\/@[^/]*$|\/index\.html$/, '/').replace(/\/?$/, '/');
    return SERVED_BY_BACKEND ? `${location.origin}/@${slug}` : `${base}?u=${encodeURIComponent(slug)}`;
}

export async function loadRemoteProfile() {
    remoteProfile = await api('/profile');
    renderProfile();
    return remoteProfile;
}

export function clearRemoteProfile() {
    remoteProfile = null;
    renderProfile();
}

// VI: Chỉ nhận URL http(s) để tránh javascript: hoặc chèn CSS. EN: Only accept http(s) URLs (no javascript: or CSS injection).
function safeUrl(value) {
    try {
        const url = new URL(value);
        return /^https?:$/.test(url.protocol) ? url.href : '';
    } catch (e) {
        return '';
    }
}

function cssUrl(value) {
    const url = safeUrl(value);
    return url ? `url("${url.replace(/["\\\n]/g, '')}")` : '';
}

// VI: Avatar là link http(s) hoặc ảnh tải lên từ máy (data:image PNG/JPEG/WebP, server cũng kiểm tra lại).
// EN: An avatar is an http(s) link or a photo uploaded from the device (data:image PNG/JPEG/WebP; the server checks it too).
const AVATAR_DATA_URL = /^data:image\/(png|jpeg|webp);base64,[A-Za-z0-9+/]+={0,2}$/;
const AVATAR_SIZE = 256;
const AVATAR_MAX_CHARS = 150000;

function safeAvatar(value) {
    return AVATAR_DATA_URL.test(value || '') ? value : safeUrl(value);
}

function avatarCssUrl(value) {
    const url = safeAvatar(value);
    return url ? `url("${url.replace(/["\\\n]/g, '')}")` : '';
}

// VI: Cắt ảnh thành hình vuông 256px ở giữa rồi nén JPEG, đủ nhỏ để lưu cùng hồ sơ.
// EN: Crops the photo to a centred 256px square and compresses it to JPEG, small enough to store with the profile.
async function resizeAvatar(file) {
    const url = URL.createObjectURL(file);
    try {
        const img = await new Promise((resolve, reject) => {
            const image = new Image();
            image.onload = () => resolve(image);
            image.onerror = () => reject(new Error('That file is not an image we can read.'));
            image.src = url;
        });
        const side = Math.min(img.naturalWidth, img.naturalHeight);
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = AVATAR_SIZE;
        const ctx = canvas.getContext('2d');
        ctx.fillStyle = '#ffffff';
        ctx.fillRect(0, 0, AVATAR_SIZE, AVATAR_SIZE);
        ctx.drawImage(img, (img.naturalWidth - side) / 2, (img.naturalHeight - side) / 2, side, side, 0, 0, AVATAR_SIZE, AVATAR_SIZE);
        for (const quality of [0.85, 0.7, 0.55, 0.4]) {
            const data = canvas.toDataURL('image/jpeg', quality);
            if (data.length <= AVATAR_MAX_CHARS) return data;
        }
        throw new Error('That photo is too detailed to use. Try another one.');
    } finally {
        URL.revokeObjectURL(url);
    }
}

function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
}

// ---------- Rendering ----------
function renderBackground(profile) {
    const bg = document.getElementById('profile-bg');
    const stage = document.getElementById('profile-stage');
    if (!bg || !stage) return;
    bg.className = `profile-bg bg-${profile.background}`;
    bg.style.backgroundImage = profile.background === 'custom' ? cssUrl(profile.backgroundUrl) : '';
    stage.style.setProperty('--profile-accent', /^#[0-9a-f]{6}$/i.test(profile.accent) ? profile.accent : DEFAULT_PROFILE.accent);

    const particles = document.getElementById('profile-particles');
    if (!particles) return;
    particles.innerHTML = '';
    particles.className = `profile-particles effect-${profile.effect}`;
    if (profile.effect === 'none') return;
    const count = profile.effect === 'snow' ? 40 : 26;
    for (let i = 0; i < count; i++) {
        const p = el('span');
        p.style.left = `${Math.random() * 100}%`;
        p.style.animationDelay = `${-Math.random() * 30}s`;
        p.style.animationDuration = `${18 + Math.random() * 16}s`;
        p.style.setProperty('--size', `${2 + Math.random() * (profile.effect === 'snow' ? 4 : 3)}px`);
        p.style.setProperty('--drift', `${(Math.random() - 0.5) * 80}px`);
        particles.appendChild(p);
    }
}

function renderCard(profile) {
    const avatar = document.getElementById('profile-avatar');
    if (avatar) {
        const img = avatarCssUrl(profile.avatar);
        avatar.style.backgroundImage = img;
        avatar.textContent = img ? '' : (profile.name.trim()[0] || '?').toUpperCase();
    }
    document.getElementById('profile-name').textContent = profile.name;
    document.getElementById('profile-name').dataset.text = profile.name;
    const handle = document.getElementById('profile-handle');
    handle.textContent = profile.handle ? `@${profile.handle.replace(/^@/, '')}` : '';
    const role = document.getElementById('profile-role');
    role.textContent = profile.role || '';
    role.hidden = !profile.role;

    const badges = document.getElementById('profile-badges');
    badges.innerHTML = '';
    profile.badges.filter(key => BADGES[key]).forEach(key => {
        const badge = el('span', 'profile-badge');
        badge.innerHTML = `<i class="ph-fill ${BADGES[key].icon}"></i>`;
        badge.dataset.tip = BADGES[key].label;
        badge.setAttribute('aria-label', BADGES[key].label);
        badges.appendChild(badge);
    });

    const locWrap = document.getElementById('profile-location-wrap');
    document.getElementById('profile-location').textContent = profile.location;
    locWrap.hidden = !profile.location;

    const links = document.getElementById('profile-links');
    links.innerHTML = '';
    Object.entries(LINK_TYPES).forEach(([key, type]) => {
        const url = safeUrl(profile.links[key] || '');
        if (!url) return;
        const a = el('a', 'profile-link');
        a.href = url;
        a.target = '_blank';
        a.rel = 'noopener';
        a.dataset.tip = type.label;
        a.setAttribute('aria-label', type.label);
        a.innerHTML = `<i class="ph-fill ${type.icon}"></i>`;
        links.appendChild(a);
    });
}

function formatFocus(minutes) {
    const h = Math.floor(minutes / 60);
    return h ? `${h}h ${minutes % 60}m` : `${minutes}m`;
}

function renderStats(stats = null) {
    const minutes = stats ? stats.totalFocusMinutes : getTotalFocusMinutes();
    const done = stats ? stats.tasksCompleted : getTodos().filter(todo => todo.completed).length;
    document.getElementById('profile-focus-total').textContent = formatFocus(minutes);
    document.getElementById('profile-tasks-done').textContent = done;
}

function renderViews(count, title) {
    document.getElementById('profile-views').textContent = count.toLocaleString('en-US');
    document.getElementById('profile-views').parentElement.title = title;
}

// VI: Trạng thái kiểu Discord, đọc từ Pomodoro / Stopwatch đang chạy. EN: Discord-like presence read from the running Pomodoro / Stopwatch.
function renderPresence() {
    const pomoCard = document.getElementById('pomo-card');
    const dot = document.getElementById('profile-status-dot');
    const icon = document.getElementById('presence-icon');
    const label = document.getElementById('presence-label');
    const title = document.getElementById('presence-title');
    const sub = document.getElementById('presence-sub');
    const stopwatchRunning = document.querySelector('#stopwatch-start-pause .ph-pause');

    let status = 'idle';
    if (pomoCard && pomoCard.classList.contains('is-running')) {
        const mode = pomoCard.dataset.mode;
        status = mode === 'work' ? 'busy' : 'online';
        label.textContent = mode === 'work' ? 'Studying' : 'On a break';
        title.textContent = mode === 'work' ? 'Deep focus session' : (mode === 'longBreak' ? 'Long break' : 'Short break');
        sub.textContent = `${document.getElementById('pomo-time').textContent} left`;
        icon.innerHTML = `<i class="ph-fill ${mode === 'work' ? 'ph-book-open-text' : 'ph-coffee'}"></i>`;
    } else if (stopwatchRunning) {
        status = 'busy';
        label.textContent = 'Studying';
        title.textContent = 'Tracking time';
        sub.textContent = `${document.getElementById('stopwatch-display').textContent} elapsed`;
        icon.innerHTML = '<i class="ph-fill ph-clock-counter-clockwise"></i>';
    } else {
        const next = getTodos().filter(todo => !todo.completed && todo.deadline)
            .sort((a, b) => `${a.deadline}${a.time}`.localeCompare(`${b.deadline}${b.time}`))[0];
        label.textContent = 'Status';
        title.textContent = 'Taking a break';
        sub.textContent = next ? `Next deadline: ${next.text}` : 'No deadlines coming up';
        icon.innerHTML = '<i class="ph-fill ph-moon-stars"></i>';
    }
    dot.className = `profile-status-dot status-${status}`;
    dot.title = { busy: 'Do not disturb', online: 'Online', idle: 'Idle' }[status];
    icon.dataset.status = status;
}

// ---------- Typewriter bio ----------
function startTypewriter(lines) {
    const target = document.getElementById('profile-bio');
    const view = document.getElementById('view-about');
    if (!target) return;
    // VI: Chưa có bio thì ẩn hẳn dòng bio (kể cả con trỏ nhấp nháy). EN: No bio: hide the line (and its caret).
    target.parentElement.hidden = !lines.length;
    const run = ++typewriterRun;
    const items = lines.length ? lines : [''];
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduced || items.length === 1) {
        target.textContent = items[0];
        return;
    }
    let line = 0, char = 0, deleting = false;

    function tick() {
        if (run !== typewriterRun) return;
        // VI: Tạm dừng khi rời trang About. EN: Pause while the About page is hidden.
        if (!view.classList.contains('active')) return setTimeout(tick, 500);
        const text = items[line];
        if (!deleting) {
            char++;
            target.textContent = text.slice(0, char);
            if (char >= text.length) { deleting = true; return setTimeout(tick, 3500); }
            return setTimeout(tick, 70);
        }
        char--;
        target.textContent = text.slice(0, char);
        if (char <= 0) { deleting = false; line = (line + 1) % items.length; return setTimeout(tick, 600); }
        setTimeout(tick, 35);
    }
    tick();
}

function renderProfile() {
    const profile = getProfile();
    renderBackground(profile);
    renderCard(profile);
    renderStats();
    renderPresence();
    startTypewriter(profile.bio.split('\n').map(line => line.trim()).filter(Boolean));
}

// VI: Gọi mỗi lần mở trang About. EN: Called whenever the About page opens.
export function openProfile() {
    if (remoteProfile) {
        // VI: Lượt xem thật do khách mở link công khai; chủ hồ sơ xem không tính. EN: Real views come from visitors; the owner never counts.
        renderViews(remoteProfile.viewCount, 'Profile views from your public link');
        api('/profile').then(dto => {
            if (!remoteProfile) return;
            remoteProfile = dto;
            renderViews(dto.viewCount, 'Profile views from your public link');
        }).catch(() => {});
    } else {
        const views = (parseInt(localStorage.getItem(VIEWS_KEY)) || 0) + 1;
        try { localStorage.setItem(VIEWS_KEY, String(views)); } catch (e) { /* ignore */ }
        renderViews(views, 'Profile views on this device');
    }
    renderStats();
    renderPresence();
    clearInterval(presenceTimer);
    presenceTimer = setInterval(() => {
        if (document.getElementById('view-about').classList.contains('active')) renderPresence();
        else clearInterval(presenceTimer);
    }, 1000);
}

// ---------- Card tilt ----------
function initTilt() {
    const stage = document.getElementById('profile-stage');
    const card = document.getElementById('profile-card');
    if (!stage || !card || window.matchMedia('(hover: none), (prefers-reduced-motion: reduce)').matches) return;
    stage.addEventListener('pointermove', (e) => {
        const rect = card.getBoundingClientRect();
        const x = (e.clientX - rect.left) / rect.width - 0.5;
        const y = (e.clientY - rect.top) / rect.height - 0.5;
        card.style.transform = `perspective(900px) rotateX(${(-y * 7).toFixed(2)}deg) rotateY(${(x * 7).toFixed(2)}deg)`;
    });
    stage.addEventListener('pointerleave', () => { card.style.transform = ''; });
}

// ---------- Edit dialog ----------
function fillForm(profile) {
    document.getElementById('pf-name').value = profile.name;
    document.getElementById('pf-handle').value = profile.handle;
    const uploaded = AVATAR_DATA_URL.test(profile.avatar || '');
    document.getElementById('pf-avatar').value = uploaded ? '' : profile.avatar;
    document.getElementById('pf-avatar-data').value = uploaded ? profile.avatar : '';
    updateAvatarPreview();
    document.getElementById('pf-bio').value = profile.bio;
    document.getElementById('pf-location').value = profile.location;
    document.getElementById('pf-role').value = profile.role || '';
    document.getElementById('pf-bg').value = profile.background;
    document.getElementById('pf-bg-url').value = profile.backgroundUrl;
    document.getElementById('pf-effect').value = profile.effect;
    document.getElementById('pf-accent').value = profile.accent;
    document.getElementById('pf-song').value = profile.song;
    document.getElementById('pf-bg-url-wrap').hidden = profile.background !== 'custom';
    renderPublicLink(profile.handle);
    fillVisibility(remoteProfile ? remoteProfile.isPublic : true);

    const badgePicker = document.getElementById('pf-badges');
    const badgeCount = document.getElementById('pf-badges-count');
    const updateBadgeCount = () => {
        const n = badgePicker.querySelectorAll('.badge-option.active').length;
        badgeCount.textContent = n ? `${n} selected` : '';
    };
    badgePicker.innerHTML = '';
    Object.entries(BADGES).forEach(([key, badge]) => {
        const active = profile.badges.includes(key);
        const btn = el('button', `badge-option ${active ? 'active' : ''}`);
        btn.type = 'button';
        btn.dataset.badge = key;
        btn.dataset.tip = badge.label;
        btn.setAttribute('aria-label', badge.label);
        btn.setAttribute('aria-pressed', active);
        btn.innerHTML = `<i class="ph-fill ${badge.icon}"></i>`;
        btn.addEventListener('click', () => {
            btn.setAttribute('aria-pressed', btn.classList.toggle('active'));
            updateBadgeCount();
        });
        badgePicker.appendChild(btn);
    });
    updateBadgeCount();

    const linksEl = document.getElementById('pf-links');
    linksEl.innerHTML = '';
    Object.entries(LINK_TYPES).forEach(([key, type]) => {
        const label = el('label', 'input-group');
        const span = el('span');
        span.innerHTML = `<i class="ph-fill ${type.icon}"></i> `;
        span.append(type.label);
        const input = el('input');
        input.type = 'url';
        input.placeholder = 'https://...';
        input.dataset.link = key;
        input.value = profile.links[key] || '';
        label.append(span, input);
        linksEl.appendChild(label);
    });
}

// VI: Link công khai chỉ có khi đăng nhập tài khoản server. EN: The public link only exists with a server account.
// VI: Công khai / riêng tư chỉ có ý nghĩa với tài khoản server (hồ sơ trên máy không có link công khai).
// EN: Public / private only matters for a server account (an on-device profile has no public link).
function fillVisibility(isPublic) {
    const input = document.getElementById('pf-public');
    input.checked = isPublic;
    input.disabled = !remoteProfile;
    document.getElementById('pf-public-wrap').classList.toggle('disabled', !remoteProfile);
    updateVisibilityText();
}

function updateVisibilityText() {
    const input = document.getElementById('pf-public');
    document.getElementById('pf-public-text').textContent = !remoteProfile
        ? 'Needs a server account'
        : input.checked ? 'Public: anyone with the link can view' : 'Private: only you can see it';
}

function renderPublicLink(slug) {
    const box = document.getElementById('pf-public-link');
    if (!box) return;
    box.replaceChildren();
    if (!remoteProfile) {
        box.append(el('i', 'ph ph-info'), ' Log in (with the backend running) to get a public link others can visit.');
        return;
    }
    const url = publicProfileUrl(slug || remoteProfile.slug);
    const link = el('a', '', url);
    link.href = url;
    link.target = '_blank';
    link.rel = 'noopener';
    const copy = el('button', 'btn btn-ghost btn-sm', 'Copy');
    copy.type = 'button';
    copy.addEventListener('click', () => {
        navigator.clipboard?.writeText(url).then(() => showToast('Link copied!', 'success', 2000))
            .catch(() => showToast(url, 'info'));
    });
    box.append(el('i', 'ph ph-link'), ' Public link: ', link, ' ', copy);
}

function initEditor() {
    const modal = document.getElementById('profile-modal');
    const form = document.getElementById('profile-form');
    if (!modal || !form) return;

    document.getElementById('profile-edit-btn').addEventListener('click', () => {
        fillForm(getProfile());
        modal.dataset.openedAt = Date.now();
        modal.classList.remove('hidden');
        document.getElementById('pf-name').focus();
    });
    document.getElementById('profile-close-btn').addEventListener('click', () => modal.classList.add('hidden'));
    initAvatarPicker();
    document.getElementById('pf-bg').addEventListener('change', (e) => {
        document.getElementById('pf-bg-url-wrap').hidden = e.target.value !== 'custom';
    });

    document.getElementById('pf-public').addEventListener('change', updateVisibilityText);
    document.getElementById('pf-handle').addEventListener('input', (e) => {
        if (remoteProfile) renderPublicLink(e.target.value.trim().replace(/^@/, '').toLowerCase());
    });

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const links = {};
        form.querySelectorAll('[data-link]').forEach(input => {
            const url = safeUrl(input.value.trim());
            if (url) links[input.dataset.link] = url;
        });
        const profile = {
            name: document.getElementById('pf-name').value.trim() || DEFAULT_PROFILE.name,
            handle: document.getElementById('pf-handle').value.trim().replace(/^@/, '') || (remoteProfile ? remoteProfile.slug : ''),
            avatar: document.getElementById('pf-avatar-data').value || safeUrl(document.getElementById('pf-avatar').value.trim()),
            bio: document.getElementById('pf-bio').value,
            location: document.getElementById('pf-location').value.trim(),
            role: document.getElementById('pf-role').value.trim(),
            badges: [...form.querySelectorAll('.badge-option.active')].map(btn => btn.dataset.badge),
            background: document.getElementById('pf-bg').value,
            backgroundUrl: safeUrl(document.getElementById('pf-bg-url').value.trim()),
            effect: document.getElementById('pf-effect').value,
            accent: document.getElementById('pf-accent').value,
            song: safeUrl(document.getElementById('pf-song').value.trim()),
            links,
        };
        if (remoteProfile) {
            const submitBtn = form.querySelector('[type="submit"]');
            submitBtn.disabled = true;
            const saved = await saveRemote(profile);
            submitBtn.disabled = false;
            if (!saved) return;
        } else {
            setStorageData(PROFILE_KEY, profile);
        }
        modal.classList.add('hidden');
        renderProfile();
    });
}

function updateAvatarPreview() {
    const preview = document.getElementById('pf-avatar-preview');
    if (!preview) return;
    const value = document.getElementById('pf-avatar-data').value || document.getElementById('pf-avatar').value.trim();
    preview.style.backgroundImage = avatarCssUrl(value);
}

function initAvatarPicker() {
    const urlInput = document.getElementById('pf-avatar');
    const fileInput = document.getElementById('pf-avatar-file');
    const dataInput = document.getElementById('pf-avatar-data');
    if (!urlInput || !fileInput || !dataInput) return;

    // VI: Gõ link thì bỏ ảnh đã tải lên. EN: Typing a link replaces the uploaded photo.
    urlInput.addEventListener('input', () => {
        dataInput.value = '';
        updateAvatarPreview();
    });
    fileInput.addEventListener('change', async () => {
        const file = fileInput.files[0];
        fileInput.value = '';
        if (!file) return;
        if (!file.type.startsWith('image/')) return showToast('Please choose an image file.', 'error');
        if (file.size > 15 * 1024 * 1024) return showToast('That photo is larger than 15 MB.', 'error');
        try {
            dataInput.value = await resizeAvatar(file);
            urlInput.value = '';
            updateAvatarPreview();
        } catch (err) {
            showToast(err.message, 'error', 5000);
        }
    });
    document.getElementById('pf-avatar-clear').addEventListener('click', () => {
        dataInput.value = '';
        urlInput.value = '';
        updateAvatarPreview();
    });
}

async function saveRemote(profile) {
    try {
        remoteProfile = await api('/profile', { method: 'PUT', body: toServer(profile, document.getElementById('pf-public').checked) });
        showToast('Profile saved.', 'success', 2000);
        return true;
    } catch (err) {
        showToast(err.status === 409 ? 'That username is already taken. Try another one.' : err.message, 'error', 5000);
        return false;
    }
}

// VI: Tooltip đi theo con trỏ chuột: hiện tên badge/link ngay phía trên chuột (không bị con trỏ che).
// EN: Cursor tooltip: shows the badge/link name right above the mouse (so the cursor never covers it).
function initCursorTip() {
    if (document.getElementById('cursor-tip')) return;
    const tip = el('div', 'cursor-tip');
    tip.id = 'cursor-tip';
    tip.setAttribute('role', 'tooltip');
    document.body.appendChild(tip);

    let target = null;
    const place = (x, y) => {
        const pad = 8;
        const { width, height } = tip.getBoundingClientRect();
        let left = x - width / 2;
        let top = y - height - 14;
        left = Math.min(Math.max(pad, left), window.innerWidth - width - pad);
        if (top < pad) top = y + 22;
        tip.style.transform = `translate(${Math.round(left)}px, ${Math.round(top)}px)`;
    };
    const show = (node, x, y) => {
        target = node;
        tip.textContent = node.dataset.tip;
        tip.classList.add('visible');
        place(x, y);
    };
    const hide = () => {
        target = null;
        tip.classList.remove('visible');
    };

    document.addEventListener('pointermove', e => {
        if (e.pointerType !== 'mouse') return;
        const node = e.target.closest?.('[data-tip]');
        if (!node) return target && hide();
        if (node !== target) show(node, e.clientX, e.clientY);
        else place(e.clientX, e.clientY);
    });
    document.addEventListener('pointerdown', e => e.pointerType !== 'mouse' && hide());
    document.addEventListener('scroll', hide, true);
    // VI: Bàn phím: hiện tooltip ngay dưới phần tử đang focus. EN: Keyboard: show it under the focused element.
    document.addEventListener('focusin', e => {
        const node = e.target.closest?.('[data-tip]');
        if (!node || !node.matches(':focus-visible')) return;
        const r = node.getBoundingClientRect();
        show(node, r.left + r.width / 2, r.top + 4);
    });
    document.addEventListener('focusout', hide);
}

export function initProfile() {
    initCursorTip();
    const enterBtn = document.getElementById('profile-enter');
    if (enterBtn) {
        // VI: Màn "click to enter" như guns.lol; trình duyệt chỉ cho phát nhạc sau một cú click.
        // EN: guns.lol-style "click to enter"; browsers only allow audio after a user click.
        enterBtn.addEventListener('click', () => {
            document.getElementById('profile-stage').classList.add('entered');
            const song = getProfile().song;
            if (song) playMusic(song);
        });
    }
    initTilt();
    initEditor();
    renderProfile();
}

// ---------- Public page (/@username or ?u=username) ----------
function timeAgo(iso) {
    const minutes = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.round(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.round(hours / 24);
    return `${days} day${days > 1 ? 's' : ''} ago`;
}

// VI: Khách không thấy được đồng hồ đang chạy, nên hiện lần học gần nhất và thời gian học tuần này.
// EN: Visitors can't see the running timer, so show the last session and this week's focus time.
function renderPublicPresence(stats) {
    const recent = stats.lastFocusAt && Date.now() - new Date(stats.lastFocusAt).getTime() < 30 * 60000;
    const status = recent ? 'busy' : (stats.lastFocusAt ? 'online' : 'idle');
    document.getElementById('presence-label').textContent = recent ? 'Studying' : 'Status';
    document.getElementById('presence-title').textContent = stats.lastFocusAt
        ? `Last focus session ${timeAgo(stats.lastFocusAt)}`
        : 'No focus sessions yet';
    document.getElementById('presence-sub').textContent = `${formatFocus(stats.weekFocusMinutes)} focused this week` +
        (stats.currentStreak ? ` · ${stats.currentStreak}-day streak` : '');
    const icon = document.getElementById('presence-icon');
    icon.innerHTML = `<i class="ph-fill ${recent ? 'ph-book-open-text' : 'ph-moon-stars'}"></i>`;
    icon.dataset.status = status;
    const dot = document.getElementById('profile-status-dot');
    dot.className = `profile-status-dot status-${status}`;
    dot.title = { busy: 'Studying', online: 'Studied recently', idle: 'Idle' }[status];
}

function renderPublicMessage(title, message) {
    renderBackground({ ...DEFAULT_PROFILE, effect: 'none' });
    renderCard({ ...DEFAULT_PROFILE, name: title, handle: '', badges: [], location: '', role: '', links: {}, avatar: '' });
    document.getElementById('profile-avatar').textContent = '?';
    document.querySelector('.profile-meta').hidden = true;
    document.getElementById('presence-card').hidden = true;
    document.getElementById('profile-views').parentElement.hidden = true;
    document.getElementById('profile-stage').classList.add('entered');
    startTypewriter([message]);
}

// VI: Trang hồ sơ chỉ đọc cho khách. Mỗi lần mở được server đếm (mỗi khách một lần/ngày).
// EN: Read-only profile page for visitors. Each visit is counted by the server (once per visitor per day).
export async function initPublicProfile(slug) {
    initCursorTip();
    document.title = `@${slug} | Study Hub`;
    initTilt();
    let dto;
    try {
        dto = await api(`/profiles/${encodeURIComponent(slug)}`, { headers: { 'X-Visitor-Id': visitorId() } });
    } catch (err) {
        if (err.status === 404) renderPublicMessage('Profile not found', `No public profile called @${slug}.`);
        else renderPublicMessage('Server unavailable', 'Could not reach the Study Hub server. Try again later.');
        return;
    }

    const profile = fromServer(dto);
    document.title = `${profile.name} (@${profile.handle}) | Study Hub`;
    renderBackground(profile);
    renderCard(profile);
    renderStats(dto.stats);
    renderPublicPresence(dto.stats);
    renderViews(dto.viewCount, 'Profile views');
    startTypewriter(profile.bio.split('\n').map(line => line.trim()).filter(Boolean));

    document.getElementById('profile-enter')?.addEventListener('click', () => {
        document.getElementById('profile-stage').classList.add('entered');
        if (profile.song) playMusic(profile.song);
    });
}
