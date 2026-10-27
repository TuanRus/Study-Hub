// VI: Nhạc: playlist có tên (Favorites + playlist tự tạo), chia sẻ playlist bằng link, và trình phát nổi phát cả playlist theo thứ tự.
//     Đăng nhập thì playlist nằm trên server; chưa đăng nhập thì nằm trong localStorage (không chia sẻ được).
// EN: Music: named playlists (Favorites + your own), sharing a playlist by link, and a floating player that plays a whole playlist in order.
//     Logged in, playlists live on the server; logged out, they live in localStorage (no sharing).

import { getStorageData, setStorageData } from '../utils/storage.js';
import { showToast } from '../utils/toast.js';
import { api, isRemote, publicBaseUrl, sessionUser } from '../utils/api.js';
import { BACKEND_URL } from '../config.js';

// VI: Favorites khi chưa đăng nhập (giữ key cũ để không mất dữ liệu). EN: Logged-out Favorites (old key kept so no data is lost).
export const MUSIC_STORAGE_KEY = 'studyhub_music';
// VI: Các playlist khác khi chưa đăng nhập: [{ id, name, tracks: [{ id, name, url, type }] }].
// EN: Other logged-out playlists: [{ id, name, tracks: [{ id, name, url, type }] }].
export const PLAYLISTS_STORAGE_KEY = 'studyhub_playlists';
const LAST_PLAYLIST_KEY = 'studyhub_last_playlist';
const LOCAL_FAVORITES_ID = 'favorites';
const MAX_PLAYLISTS = 50;
const MAX_TRACKS = 500;

// VI: Khi đăng nhập: danh sách playlist trên server; tracks = null cho tới khi mở playlist đó. null = dùng localStorage.
// EN: Logged in: the server's playlists; tracks = null until that playlist is opened. null = use localStorage.
let remotePlaylists = null; // [{ id, name, isDefault, isShared, shareCode, count, tracks }]

function fromTrack(track) {
    return { id: track.id, name: track.title, url: track.sourceUrl, type: String(track.provider).toLowerCase() };
}

function fromSummary(p) {
    return { id: p.id, name: p.name, isDefault: p.isDefault, isShared: !!p.isShared, shareCode: p.shareCode || null, count: p.trackCount, tracks: null };
}

function fromDetail(p) {
    const tracks = p.tracks.map(fromTrack);
    return { id: p.id, name: p.name, isDefault: p.isDefault, isShared: !!p.isShared, shareCode: p.shareCode || null, count: tracks.length, tracks };
}

export async function loadRemoteMusic() {
    const list = await api('/playlists');
    remotePlaylists = list.map(fromSummary);
    nasEnabled = null;
    refreshLibrary();
}

export function clearRemoteMusic() {
    remotePlaylists = null;
    nasEnabled = null;
    nasArtists = null;
    nasArtist = null;
    refreshLibrary();
}

// ---------- NAS library (Navidrome, admins only) ----------
// VI: Kho nhạc riêng trên NAS, đi qua backend (mật khẩu Navidrome không xuống trình duyệt). Bài NAS có url "nas:<id>".
// EN: The private NAS library, through the backend (the Navidrome password never reaches the browser). NAS songs use the url "nas:<id>".
const NAS_VIEW = '__nas';
let nasEnabled = null; // null = not checked yet
let nasQuery = '';
let nasResults = null;
// VI: Tab "Songs" (tìm bài) hoặc "Artists" (duyệt theo nghệ sĩ). EN: "Songs" tab (search) or "Artists" tab (browse by artist).
let nasTab = 'songs';
let nasArtists = null;
let nasArtistFilter = '';
let nasArtist = null; // { id, name, songs } of the open artist
// VI: Mở hộp thoại "Lưu vào playlist"; được gán trong initMusic. EN: Opens the "Save to a playlist" dialog; set in initMusic.
let openSaveDialog = () => {};

function canUseNas() {
    return isRemote() && ['Admin', 'SuperAdmin'].includes(sessionUser()?.role);
}

async function checkNas() {
    if (!canUseNas()) return false;
    if (nasEnabled === null) {
        try {
            nasEnabled = (await api('/library/status')).enabled;
        } catch (e) {
            nasEnabled = false;
        }
    }
    return nasEnabled;
}

function isNasTrack(track) {
    return /^nas:[A-Za-z0-9_-]{1,64}$/.test(track?.url || '');
}

function fromNasSong(song) {
    const name = song.artist ? `${song.title} · ${song.artist}` : song.title;
    return { id: `nas-${song.id}`, name, url: `nas:${song.id}`, type: 'nas', album: song.album || '' };
}

// ---------- Links ----------
const YT_REGEX = /(?:youtu\.be\/|youtube\.com\/(?:embed\/|v\/|watch\?v=|watch\?.+&v=|live\/|shorts\/))([\w-]{11})/;

function getMusicType(link) {
    if (/^nas:/.test(link)) return 'nas';
    if (/^spotify:/.test(link) || link.includes('spotify.com')) return 'spotify';
    if (link.includes('youtube.com') || link.includes('youtu.be')) return 'youtube';
    return '';
}

// VI: Phân tích URL dựa theo signature của từng service.
// EN: Parsing URL based on each service's signature.
function getEmbedUrl(link) {
    const type = getMusicType(link);
    if (type === 'spotify') {
        const uri = link.match(/^spotify:([a-z]+):([A-Za-z0-9]{22})$/);
        if (uri) return `https://open.spotify.com/embed/${uri[1]}/${uri[2]}`;
        try {
            // VI: Bỏ tiền tố ngôn ngữ (vd: /intl-vi/track/...). EN: Strip locale prefix (e.g. /intl-vi/track/...).
            const path = new URL(link).pathname.replace(/^\/intl-[\w-]+/, '').replace(/^\/embed/, '');
            return `https://open.spotify.com/embed${path}`;
        } catch (e) {
            return '';
        }
    }
    if (type === 'youtube') {
        const origin = encodeURIComponent(window.location.origin);
        const match = link.match(YT_REGEX);
        if (match && match[1]) {
            return `https://www.youtube.com/embed/${match[1]}?autoplay=1&enablejsapi=1&origin=${origin}`;
        }
        // VI: Link playlist YouTube (…?list=…). EN: A YouTube playlist link (…?list=…).
        const list = link.match(/[?&]list=([\w-]{10,64})/);
        if (list) return `https://www.youtube.com/embed/videoseries?list=${list[1]}&autoplay=1&enablejsapi=1&origin=${origin}`;
    }
    return '';
}

function sameSong(a, b) {
    if (getMusicType(a) === 'nas' || getMusicType(b) === 'nas') return a === b;
    return getEmbedUrl(a).split('?')[0] === getEmbedUrl(b).split('?')[0];
}

// VI: Link chia sẻ: ưu tiên PUBLIC_BASE_URL của server, không thì thư mục hiện tại của trang (chạy được cả với Live Server).
// EN: Share link: prefer the server's PUBLIC_BASE_URL, otherwise the page's current folder (works with Live Server too).
function shareUrl(code) {
    const base = publicBaseUrl() ? `${publicBaseUrl()}/` : new URL('./', location.href.replace(/\/@[^/?#]*/, '/')).href.split(/[?#]/)[0];
    return `${base}?playlist=${encodeURIComponent(code)}`;
}

// ---------- Storage: one API for server and localStorage ----------
function localPlaylists() {
    const favorites = { id: LOCAL_FAVORITES_ID, name: 'Favorites', isDefault: true, tracks: getStorageData(MUSIC_STORAGE_KEY, []) };
    const others = getStorageData(PLAYLISTS_STORAGE_KEY, []).filter(p => p && p.id && Array.isArray(p.tracks));
    return [favorites, ...others.map(p => ({ ...p, isDefault: false }))]
        .map(p => ({ ...p, isShared: false, shareCode: null, count: p.tracks.length }));
}

function saveLocalPlaylist(playlist) {
    if (playlist.isDefault) {
        setStorageData(MUSIC_STORAGE_KEY, playlist.tracks);
        return;
    }
    const others = getStorageData(PLAYLISTS_STORAGE_KEY, []);
    const index = others.findIndex(p => p.id === playlist.id);
    const stored = { id: playlist.id, name: playlist.name, tracks: playlist.tracks };
    if (index >= 0) others[index] = stored;
    else others.push(stored);
    setStorageData(PLAYLISTS_STORAGE_KEY, others);
}

function findRemote(id) {
    const playlist = remotePlaylists?.find(p => String(p.id) === String(id));
    if (!playlist) throw new Error('This playlist no longer exists.');
    return playlist;
}

function replaceRemote(detail) {
    const updated = fromDetail(detail);
    const index = remotePlaylists.findIndex(p => p.id === updated.id);
    if (index >= 0) remotePlaylists[index] = updated;
    else remotePlaylists.push(updated);
    return updated;
}

const store = {
    list() {
        return remotePlaylists ? [...remotePlaylists] : localPlaylists();
    },

    async get(id) {
        if (!remotePlaylists) {
            const playlist = localPlaylists().find(p => String(p.id) === String(id));
            if (!playlist) throw new Error('This playlist no longer exists.');
            return playlist;
        }
        const playlist = findRemote(id);
        if (playlist.tracks) return playlist;
        return replaceRemote(await api(`/playlists/${playlist.id}`));
    },

    async create(name, tracks = []) {
        if (remotePlaylists) return replaceRemote(await api('/playlists', { method: 'POST', body: { name } }));
        if (localPlaylists().length >= MAX_PLAYLISTS) throw new Error(`You can have at most ${MAX_PLAYLISTS} playlists.`);
        const playlist = { id: `pl-${Date.now()}`, name, isDefault: false, isShared: false, shareCode: null, tracks };
        saveLocalPlaylist(playlist);
        return { ...playlist, count: tracks.length };
    },

    async rename(id, name) {
        if (remotePlaylists) return replaceRemote(await api(`/playlists/${id}`, { method: 'PUT', body: { name } }));
        const playlist = await this.get(id);
        saveLocalPlaylist({ ...playlist, name });
        return { ...playlist, name };
    },

    async remove(id) {
        if (remotePlaylists) {
            await api(`/playlists/${id}`, { method: 'DELETE' });
            remotePlaylists = remotePlaylists.filter(p => String(p.id) !== String(id));
            return;
        }
        setStorageData(PLAYLISTS_STORAGE_KEY, getStorageData(PLAYLISTS_STORAGE_KEY, []).filter(p => p.id !== id));
    },

    async addTrack(id, { name, url }) {
        if (remotePlaylists) {
            const playlist = await this.get(id);
            let track;
            try {
                track = await api(`/playlists/${playlist.id}/tracks`, { method: 'POST', body: { url, title: name } });
            } catch (err) {
                if (err.status === 409) throw new Error(`This song is already in "${playlist.name}".`);
                throw err;
            }
            playlist.tracks.push(fromTrack(track));
            playlist.count = playlist.tracks.length;
            return playlist;
        }
        const playlist = await this.get(id);
        if (playlist.tracks.some(t => sameSong(t.url, url))) throw new Error(`This song is already in "${playlist.name}".`);
        if (playlist.tracks.length >= MAX_TRACKS) throw new Error(`A playlist can hold at most ${MAX_TRACKS} songs.`);
        playlist.tracks.push({ id: Date.now(), name, url, type: getMusicType(url) });
        saveLocalPlaylist(playlist);
        return playlist;
    },

    async removeTrack(id, trackId) {
        const playlist = await this.get(id);
        if (remotePlaylists) await api(`/playlists/${playlist.id}/tracks/${trackId}`, { method: 'DELETE' });
        playlist.tracks = playlist.tracks.filter(t => String(t.id) !== String(trackId));
        playlist.count = playlist.tracks.length;
        if (!remotePlaylists) saveLocalPlaylist(playlist);
        return playlist;
    },

    async setShared(id, isShared) {
        if (!remotePlaylists) throw new Error('Log in to share playlists.');
        return replaceRemote(await api(`/playlists/${id}/share`, { method: 'PUT', body: { isShared } }));
    },
};

function lastPlaylistId() {
    try { return localStorage.getItem(LAST_PLAYLIST_KEY); } catch (e) { return null; }
}

function rememberPlaylist(id) {
    try { localStorage.setItem(LAST_PLAYLIST_KEY, String(id)); } catch (e) { /* ignore */ }
}

// ---------- Player: a queue played in order ----------
// VI: Trình phát là một "đảo" kiểu Dynamic Island: thu gọn chỉ hiện tên bài + play/pause, bấm vào để mở rộng.
//     Video YouTube chỉ hiện khi mở rộng và bật "Show video"; Spotify chỉ hiện khung khi mở rộng.
// EN: The player is a Dynamic Island-style "island": collapsed it shows the song name + play/pause, tap to expand.
//     YouTube video only shows when expanded with "Show video" on; Spotify's frame only shows when expanded.
const SHOW_VIDEO_KEY = 'studyhub_music_audio_only';
// VI: control = { toggle } của nguồn đang phát (YouTube, Spotify, <audio> NAS); null khi chưa điều khiển được.
// EN: control = { toggle } of the current source (YouTube, Spotify, NAS <audio>); null while it can't be controlled yet.
const player = { tracks: [], index: 0, name: '', playlistId: null, repeat: false, mountId: 0,
    showVideo: loadShowVideo(), forceVideo: false, control: null, playing: false, title: '' };

// VI: Mặc định chỉ nghe nhạc (ẩn video). EN: Audio only (video hidden) by default.
function loadShowVideo() {
    try { return localStorage.getItem(SHOW_VIDEO_KEY) === '0'; } catch (e) { return false; }
}

function setShowVideo(on) {
    player.showVideo = on;
    try { localStorage.setItem(SHOW_VIDEO_KEY, on ? '0' : '1'); } catch (e) { /* ignore */ }
    syncIsland();
}

function setExpanded(on) {
    const container = document.getElementById('music-player-container');
    if (!container) return;
    container.classList.toggle('expanded', on);
    const btn = document.getElementById('music-island-btn');
    btn?.setAttribute('aria-expanded', String(on));
    if (btn) btn.title = on ? 'Collapse player' : 'Show player';
}

// ---------- Dragging the island ----------
// VI: Kéo đảo đi đâu cũng được; vị trí được nhớ theo hai cạnh gần nhất (vd. phải + dưới), nên khi mở rộng đảo
//     nở về phía còn trống và không tràn khỏi màn hình. Chưa kéo lần nào thì CSS đặt ở góc dưới bên phải.
// EN: Drag the island anywhere; its spot is remembered against the two nearest edges (e.g. right + bottom), so when
//     it expands it grows towards the open space and stays on screen. Never dragged: CSS puts it bottom-right.
const ISLAND_POS_KEY = 'studyhub_music_island_pos';
const ISLAND_EDGE = 8;
const DRAG_THRESHOLD = 6;

function loadIslandPos() {
    try {
        const pos = JSON.parse(localStorage.getItem(ISLAND_POS_KEY));
        if (pos && ['left', 'right'].includes(pos.h) && ['top', 'bottom'].includes(pos.v)
            && Number.isFinite(pos.x) && Number.isFinite(pos.y)) return pos;
    } catch (e) { /* ignore */ }
    return null;
}

function saveIslandPos(pos) {
    try { localStorage.setItem(ISLAND_POS_KEY, JSON.stringify(pos)); } catch (e) { /* ignore */ }
}

// VI: Khung của đảo bỏ qua transform (hiệu ứng phóng to lúc hiện ra không làm lệch vị trí).
// EN: The island's box ignoring transforms (the pop-in scale animation doesn't shift the spot).
function islandBox(container) {
    return { left: container.offsetLeft, top: container.offsetTop, width: container.offsetWidth, height: container.offsetHeight };
}

function clampTo(value, max) {
    return Math.max(ISLAND_EDGE, Math.min(value, max));
}

// VI: Từ khung hiện tại của đảo ra vị trí bám hai cạnh gần nhất, luôn nằm trong màn hình.
// EN: From the island's current box to a spot held against the two nearest edges, always on screen.
function islandPosFromRect(rect) {
    const w = window.innerWidth;
    const h = window.innerHeight;
    const left = clampTo(rect.left, w - rect.width - ISLAND_EDGE);
    const top = clampTo(rect.top, h - rect.height - ISLAND_EDGE);
    const onLeft = left + rect.width / 2 < w / 2;
    const onTop = top + rect.height / 2 < h / 2;
    return {
        h: onLeft ? 'left' : 'right',
        x: Math.round(onLeft ? left : w - left - rect.width),
        v: onTop ? 'top' : 'bottom',
        y: Math.round(onTop ? top : h - top - rect.height),
    };
}

function applyIslandPos(container, pos) {
    const style = container.style;
    style.left = pos ? (pos.h === 'left' ? `${pos.x}px` : 'auto') : '';
    style.right = pos ? (pos.h === 'right' ? `${pos.x}px` : 'auto') : '';
    style.top = pos ? (pos.v === 'top' ? `${pos.y}px` : 'auto') : '';
    style.bottom = pos ? (pos.v === 'bottom' ? `${pos.y}px` : 'auto') : '';
}

// VI: Kéo lại vào trong màn hình (sau khi đổi cỡ cửa sổ hoặc mở rộng). EN: Pull back on screen (after a resize or expanding).
//     Giữ nguyên hai cạnh đang bám, để mở rộng rồi thu gọn thì đảo về đúng chỗ cũ.
//     Keeps the same two edges, so expanding then collapsing puts the island back where it was.
function keepIslandOnScreen(container) {
    const pos = loadIslandPos();
    if (!pos || !container.classList.contains('active')) return;
    const box = islandBox(container);
    const x = clampTo(pos.x, window.innerWidth - box.width - ISLAND_EDGE);
    const y = clampTo(pos.y, window.innerHeight - box.height - ISLAND_EDGE);
    applyIslandPos(container, { ...pos, x, y });
}

function makeIslandDraggable(container) {
    const head = container.querySelector('.music-player-head');
    if (!head) return;
    applyIslandPos(container, loadIslandPos());

    let drag = null;
    let justDragged = false;
    head.addEventListener('pointerdown', (e) => {
        // VI: Nút play/pause vẫn bấm bình thường. EN: The play/pause button still works as a button.
        if (e.button !== 0 || e.target.closest('.music-toggle')) return;
        const rect = islandBox(container);
        drag = { id: e.pointerId, startX: e.clientX, startY: e.clientY, left: rect.left, top: rect.top, moving: false };
    });
    head.addEventListener('pointermove', (e) => {
        if (!drag || e.pointerId !== drag.id) return;
        const dx = e.clientX - drag.startX;
        const dy = e.clientY - drag.startY;
        if (!drag.moving) {
            if (Math.hypot(dx, dy) < DRAG_THRESHOLD) return;
            drag.moving = true;
            head.setPointerCapture(e.pointerId);
            container.classList.add('dragging');
        }
        const rect = islandBox(container);
        applyIslandPos(container, {
            h: 'left', x: clampTo(drag.left + dx, window.innerWidth - rect.width - ISLAND_EDGE),
            v: 'top', y: clampTo(drag.top + dy, window.innerHeight - rect.height - ISLAND_EDGE),
        });
    });
    const endDrag = (e) => {
        if (!drag || e.pointerId !== drag.id) return;
        if (drag.moving) {
            container.classList.remove('dragging');
            const pos = islandPosFromRect(islandBox(container));
            applyIslandPos(container, pos);
            saveIslandPos(pos);
            // VI: Thả tay sau khi kéo không tính là bấm (không mở rộng đảo). EN: Letting go after a drag is not a tap (no expand).
            justDragged = true;
            setTimeout(() => { justDragged = false; }, 0);
        }
        drag = null;
    };
    head.addEventListener('pointerup', endDrag);
    head.addEventListener('pointercancel', endDrag);
    container.addEventListener('click', (e) => {
        if (!justDragged) return;
        e.stopPropagation();
        e.preventDefault();
    }, true);

    // VI: Nhấp đúp để đưa đảo về góc mặc định. EN: Double-click to send the island back to its default corner.
    head.addEventListener('dblclick', (e) => {
        if (e.target.closest('.music-toggle')) return;
        try { localStorage.removeItem(ISLAND_POS_KEY); } catch (err) { /* ignore */ }
        applyIslandPos(container, null);
    });

    window.addEventListener('resize', () => keepIslandOnScreen(container));
    container.addEventListener('transitionend', (e) => {
        if (e.target === container && e.propertyName === 'width') keepIslandOnScreen(container);
    });
}

function setPlaying(on) {
    player.playing = on;
    syncIsland();
}

const SOURCE_ICONS = { youtube: 'ph-youtube-logo', spotify: 'ph-spotify-logo', nas: 'ph-hard-drives' };

function syncIsland() {
    const container = document.getElementById('music-player-container');
    const track = player.tracks[player.index];
    const type = track ? getMusicType(track.url) : '';
    for (const t of ['youtube', 'spotify', 'nas']) container?.classList.toggle(`is-${t}`, type === t);
    container?.classList.toggle('show-video', player.showVideo || player.forceVideo);
    container?.classList.toggle('is-playing', player.playing);

    const art = document.getElementById('music-art');
    if (art) art.innerHTML = `<i class="ph-fill ${SOURCE_ICONS[type] || 'ph-music-notes'}"></i>`;

    const videoBtn = document.getElementById('music-video-btn');
    if (videoBtn) {
        const label = player.showVideo ? 'Hide video (audio only)' : 'Show video';
        videoBtn.hidden = type !== 'youtube';
        videoBtn.title = label;
        videoBtn.setAttribute('aria-label', label);
        videoBtn.setAttribute('aria-pressed', String(player.showVideo));
        videoBtn.innerHTML = `<i class="ph-bold ${player.showVideo ? 'ph-video-camera-slash' : 'ph-video-camera'}"></i>`;
    }

    const toggleBtn = document.getElementById('music-toggle-btn');
    if (toggleBtn) {
        const label = player.playing ? 'Pause' : 'Play';
        toggleBtn.hidden = !player.control;
        toggleBtn.title = label;
        toggleBtn.setAttribute('aria-label', label);
        toggleBtn.innerHTML = `<i class="ph-fill ${player.playing ? 'ph-pause' : 'ph-play'}"></i>`;
    }
}

let youTubeApi = null;
function loadYouTubeApi() {
    youTubeApi ||= new Promise((resolve, reject) => {
        if (window.YT?.Player) return resolve(window.YT);
        const previous = window.onYouTubeIframeAPIReady;
        window.onYouTubeIframeAPIReady = () => {
            if (typeof previous === 'function') previous();
            resolve(window.YT);
        };
        const script = document.createElement('script');
        script.src = 'https://www.youtube.com/iframe_api';
        script.onerror = () => { youTubeApi = null; reject(new Error('YouTube API blocked')); };
        document.head.appendChild(script);
    });
    return youTubeApi;
}

let spotifyApi = null;
function loadSpotifyApi() {
    spotifyApi ||= new Promise((resolve, reject) => {
        window.onSpotifyIframeApiReady = (IFrameAPI) => resolve(IFrameAPI);
        const script = document.createElement('script');
        script.src = 'https://open.spotify.com/embed/iframe-api/v1';
        script.onerror = () => { spotifyApi = null; reject(new Error('Spotify API blocked')); };
        document.head.appendChild(script);
        setTimeout(() => reject(new Error('Spotify API timed out')), 6000);
    });
    return spotifyApi;
}

function plainIframe(src) {
    const iframe = document.createElement('iframe');
    iframe.src = src;
    iframe.allow = 'autoplay; encrypted-media; clipboard-write';
    iframe.allowFullscreen = true;
    return iframe;
}

// VI: Link phát bài NAS: chủ playlist (admin) lấy từ kho nhạc; khách lấy qua mã chia sẻ của playlist.
//     Nhạc hồ sơ trên trang bio thì lấy qua slug của hồ sơ.
// EN: NAS stream link: the owner (admin) gets it from the library; visitors get it through the playlist's share code.
//     A profile song on a bio page gets it through the profile's slug.
function nasStreamUrl(track) {
    if (track.profileSlug) return api(`/profiles/${encodeURIComponent(track.profileSlug)}/song/stream-url`);
    if (track.shareCode) {
        return api(`/shared-playlists/${encodeURIComponent(track.shareCode)}/tracks/${encodeURIComponent(track.id)}/stream-url`);
    }
    return api(`/library/songs/${encodeURIComponent(track.url.slice(4))}/stream-url`);
}

// VI: Gắn bài vào khung phát và nối nút play/pause của đảo với nguồn đó. Có hàng đợi thì nghe sự kiện "hết bài" để tự chuyển.
//     API YouTube/Spotify không tải được (bị chặn) thì mở rộng đảo để dùng nút của chính trình phát.
// EN: Mounts a track and wires the island's play/pause to that source. With a queue, listens for "ended" to move on.
//     If the YouTube/Spotify API can't load (blocked), the island expands so the embed's own buttons can be used.
function mountTrack(track) {
    const frame = document.getElementById('music-frame');
    const embedUrl = getEmbedUrl(track.url);
    const mountId = ++player.mountId;
    const isCurrent = () => mountId === player.mountId;
    const autoAdvance = player.tracks.length > 1 || player.repeat;
    let ended = false;
    const onEnded = () => {
        if (ended || !isCurrent()) return;
        ended = true;
        playNext({ auto: true });
    };
    player.control = null;
    player.playing = false;
    player.title = '';
    player.forceVideo = false;

    // VI: Bài NAS: <audio> thường, link phát có token ngắn hạn. EN: NAS song: a plain <audio>, stream link with a short-lived token.
    if (isNasTrack(track)) {
        const audio = document.createElement('audio');
        audio.controls = true;
        audio.preload = 'auto';
        audio.addEventListener('play', () => { if (isCurrent()) setPlaying(true); });
        audio.addEventListener('pause', () => { if (isCurrent()) setPlaying(false); });
        audio.addEventListener('ended', () => { if (autoAdvance) onEnded(); });
        audio.addEventListener('error', () => {
            if (isCurrent() && audio.getAttribute('src')) showToast(`Can't play "${track.name}" from the NAS.`, 'error');
        });
        frame.replaceChildren(audio);
        player.control = { toggle: () => (audio.paused ? audio.play().catch(() => {}) : audio.pause()) };
        nasStreamUrl(track).then((link) => {
            if (!isCurrent()) return;
            audio.src = BACKEND_URL + link.url;
            audio.play().catch(() => {});
            if (link.title && !track.name) {
                player.title = link.title;
                renderPlayerHead();
            }
        }).catch((err) => {
            if (isCurrent()) showToast(err.status === 404 ? 'This NAS song is no longer available.' : (err.message || 'Could not reach the NAS music library.'), 'error');
        });
        return;
    }

    // VI: YouTube luôn dùng API (kể cả bài lẻ) để play/pause được khi video bị ẩn.
    // EN: YouTube always uses the API (even for a single song) so play/pause works while the video is hidden.
    if (embedUrl.includes('youtube.com')) {
        const iframe = plainIframe(embedUrl);
        frame.replaceChildren(iframe);
        const isSeries = embedUrl.includes('videoseries');
        loadYouTubeApi().then((YT) => {
            if (!isCurrent()) return;
            const yt = new YT.Player(iframe, {
                events: {
                    onReady: () => {
                        if (!isCurrent()) return;
                        player.control = { toggle: () => (player.playing ? yt.pauseVideo() : yt.playVideo()) };
                        syncIsland();
                    },
                    // VI: Video lỗi (bị xóa, chặn nhúng...) thì mở rộng và hiện video để thấy thông báo của YouTube.
                    // EN: Video error (removed, embedding blocked...): expand and show the video so YouTube's message is visible.
                    onError: () => {
                        if (!isCurrent()) return;
                        player.forceVideo = true;
                        syncIsland();
                        setExpanded(true);
                        showToast("This YouTube video can't be played here.", 'error');
                    },
                    onStateChange: (e) => {
                        if (!isCurrent()) return;
                        // VI: Link lẻ (ô dán link, nhạc hồ sơ) không có tên, nên lấy tên video. EN: Single links have no name, so use the video title.
                        const videoTitle = yt.getVideoData?.()?.title;
                        if (videoTitle && !track.name && player.title !== videoTitle) {
                            player.title = videoTitle;
                            renderPlayerHead();
                        }
                        if (e.data !== YT.PlayerState.ENDED) {
                            setPlaying(e.data === YT.PlayerState.PLAYING || e.data === YT.PlayerState.BUFFERING);
                            return;
                        }
                        setPlaying(false);
                        if (!autoAdvance) return;
                        // VI: Playlist YouTube: chỉ chuyển khi hết video cuối. EN: YouTube playlist: only move on after its last video.
                        if (isSeries) {
                            const list = yt.getPlaylist?.() || [];
                            if (list.length && yt.getPlaylistIndex() < list.length - 1) return;
                        }
                        onEnded();
                    },
                },
            });
        }).catch(() => {
            if (!isCurrent()) return;
            player.forceVideo = true;
            syncIsland();
            setExpanded(true);
        });
        return;
    }

    // VI: Spotify: dùng iFrame API để play/pause từ đảo. Chỉ bài hát / tập podcast mới biết lúc kết thúc;
    //     album, playlist Spotify tự phát tiếp nên bấm Next để qua.
    // EN: Spotify: the iFrame API lets the island play/pause. Only a track/episode has a clear end;
    //     albums and Spotify playlists keep playing, so press Next to move on.
    const spotify = embedUrl.match(/open\.spotify\.com\/embed\/([a-z]+)\/([A-Za-z0-9]{22})/);
    if (!spotify) {
        frame.replaceChildren(plainIframe(embedUrl));
        setExpanded(true);
        return;
    }
    const holder = document.createElement('div');
    frame.replaceChildren(holder);
    loadSpotifyApi().then((IFrameAPI) => {
        if (!isCurrent()) return;
        IFrameAPI.createController(holder, { uri: `spotify:${spotify[1]}:${spotify[2]}`, width: '100%', height: '100%' }, (controller) => {
            const hasEnd = spotify[1] === 'track' || spotify[1] === 'episode';
            let furthest = 0;
            controller.addListener('ready', () => {
                if (!isCurrent()) return;
                player.control = { toggle: () => controller.togglePlay() };
                syncIsland();
                controller.play();
            });
            controller.addListener('playback_update', (e) => {
                if (!isCurrent()) return;
                const { isPaused, position, duration } = e.data || {};
                if (player.playing === !!isPaused) setPlaying(!isPaused);
                if (!duration || !hasEnd || !autoAdvance) return;
                furthest = Math.max(furthest, position);
                if (position >= duration - 300 || (isPaused && furthest >= duration - 2000)) onEnded();
            });
        });
    }).catch(() => {
        if (!isCurrent()) return;
        frame.replaceChildren(plainIframe(embedUrl));
        setExpanded(true);
    });
}

function renderPlayerHead() {
    const track = player.tracks[player.index];
    const many = player.tracks.length > 1;
    const title = document.getElementById('music-now-title');
    const sub = document.getElementById('music-now-sub');
    if (title) title.textContent = track?.name || player.title || 'Now playing';
    if (sub) {
        sub.textContent = many ? `${player.name || 'Queue'} · ${player.index + 1}/${player.tracks.length}` : '';
        sub.hidden = !many;
    }
    for (const id of ['music-prev-btn', 'music-next-btn', 'music-repeat-btn']) {
        const btn = document.getElementById(id);
        if (btn) btn.hidden = !many;
    }
    const repeat = document.getElementById('music-repeat-btn');
    if (repeat) repeat.setAttribute('aria-pressed', String(player.repeat));
    syncIsland();
}

function playAt(index) {
    const container = document.getElementById('music-player-container');
    const track = player.tracks[index];
    if (!container || !track) return;
    if (!getEmbedUrl(track.url) && !isNasTrack(track)) {
        showToast(`Can't play "${track.name || track.url}". Skipping.`, 'error');
        if (index < player.tracks.length - 1) playAt(index + 1);
        return;
    }
    player.index = index;
    mountTrack(track);
    renderPlayerHead();
    container.classList.add('active');
    keepIslandOnScreen(container);
}

function playNext({ auto = false } = {}) {
    if (player.index < player.tracks.length - 1) return playAt(player.index + 1);
    if (player.repeat || !auto) return playAt(0);
    showToast(`Finished "${player.name || 'playlist'}".`, 'info');
}

function playPrev() {
    playAt(player.index > 0 ? player.index - 1 : player.tracks.length - 1);
}

// VI: Phát cả danh sách, bắt đầu từ startIndex. EN: Plays a whole list, starting at startIndex.
export function playQueue(tracks, startIndex = 0, name = '', playlistId = null) {
    const playable = tracks.filter(t => t && t.url);
    if (!playable.length) {
        showToast('This playlist has no songs yet.', 'info');
        return;
    }
    player.tracks = playable;
    player.name = name;
    player.playlistId = playlistId;
    setExpanded(false);
    playAt(Math.min(Math.max(startIndex, 0), playable.length - 1));
}

// VI: Phát một link lẻ (ô dán link, nhạc của hồ sơ). EN: Plays a single link (the paste box, a profile song).
export function playMusic(link) {
    if (!getEmbedUrl(link)) {
        showToast('Invalid link. Please try a valid Spotify or YouTube link.', 'error');
        return;
    }
    player.repeat = false;
    playQueue([{ id: 'single', name: '', url: link }]);
}

// VI: Phát nhạc hồ sơ: link YouTube/Spotify, hoặc bài NAS ("nas:<id>"). Trên trang bio của người khác thì
//     truyền slug để khách nghe bài NAS qua hồ sơ; hồ sơ của chính mình (admin) thì lấy thẳng từ kho nhạc.
// EN: Plays a profile song: a YouTube/Spotify link, or a NAS song ("nas:<id>"). On someone's bio page pass the
//     slug so visitors hear the NAS song through the profile; your own profile (admin) streams from the library.
export function playProfileSong(link, slug = null) {
    if (!isNasTrack({ url: link })) return playMusic(link);
    player.repeat = false;
    playQueue([{ id: 'profile-song', name: '', url: link, type: 'nas', profileSlug: slug }]);
}

function stopPlayer() {
    player.mountId++;
    player.tracks = [];
    player.control = null;
    player.playing = false;
    document.getElementById('music-frame')?.replaceChildren();
    document.getElementById('music-player-container')?.classList.remove('active', 'is-playing');
    setExpanded(false);
}

// ---------- Library popover ----------
let openPlaylistId = null; // null = list of playlists

function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
}

function iconButton(icon, title, className = 'icon-btn icon-btn-xs') {
    const btn = el('button', className);
    btn.type = 'button';
    btn.title = title;
    btn.setAttribute('aria-label', title);
    btn.innerHTML = `<i class="${icon}"></i>`;
    return btn;
}

function songCount(n) {
    return `${n} song${n === 1 ? '' : 's'}`;
}

function libraryIsOpen() {
    const library = document.getElementById('music-library');
    return library && !library.hidden;
}

function refreshLibrary() {
    if (libraryIsOpen()) renderLibrary();
}

async function renderLibrary() {
    const title = document.getElementById('music-library-title');
    const actions = document.getElementById('music-library-actions');
    const back = document.getElementById('music-library-back');
    const body = document.getElementById('music-library-body');
    if (!body) return;

    if (openPlaylistId === null) {
        back.hidden = true;
        title.innerHTML = '<i class="ph ph-playlist"></i> Playlists';
        const add = el('button', 'btn btn-ghost btn-sm');
        add.type = 'button';
        add.innerHTML = '<i class="ph ph-plus"></i> New';
        add.addEventListener('click', () => openNameModal());
        actions.replaceChildren(add);
        renderPlaylistList(body);
        return;
    }

    if (openPlaylistId === NAS_VIEW && nasArtist) {
        back.hidden = false;
        title.replaceChildren(el('span', 'music-library-name', nasArtist.name));
        title.title = nasArtist.name;
        const play = iconButton('ph-fill ph-play', 'Play all', 'icon-btn icon-btn-xs music-play-all');
        play.disabled = !nasArtist.songs?.length;
        play.addEventListener('click', () => {
            playQueue(nasArtist.songs, 0, `NAS: ${nasArtist.name}`);
            setLibraryOpen(false);
        });
        actions.replaceChildren(play);
        renderArtistSongs(body);
        return;
    }

    if (openPlaylistId === NAS_VIEW) {
        back.hidden = false;
        title.innerHTML = '<i class="ph ph-hard-drives"></i> NAS library';
        title.title = '';
        actions.replaceChildren();
        renderNasView(body);
        return;
    }

    let playlist;
    try {
        playlist = await store.get(openPlaylistId);
    } catch (err) {
        openPlaylistId = null;
        if (err.message) showToast(err.message, 'error');
        return renderLibrary();
    }
    back.hidden = false;
    title.replaceChildren(el('span', 'music-library-name', playlist.name));
    title.title = playlist.name;

    const play = iconButton('ph-fill ph-play', 'Play all', 'icon-btn icon-btn-xs music-play-all');
    play.disabled = !playlist.tracks.length;
    play.addEventListener('click', () => {
        playQueue(playlist.tracks, 0, playlist.name, playlist.id);
        setLibraryOpen(false);
    });
    const share = iconButton(playlist.isShared ? 'ph-fill ph-share-network' : 'ph ph-share-network',
        playlist.isShared ? 'Shared: manage link' : 'Share');
    share.classList.toggle('is-shared', playlist.isShared);
    share.addEventListener('click', () => openShareModal(playlist.id));
    const rename = iconButton('ph ph-pencil-simple', 'Rename');
    rename.addEventListener('click', () => openNameModal(playlist));
    const buttons = [play, share, rename];
    if (!playlist.isDefault) {
        const del = iconButton('ph ph-trash', 'Delete playlist', 'icon-btn icon-btn-xs btn-danger-icon');
        del.addEventListener('click', () => deletePlaylist(playlist));
        buttons.push(del);
    }
    actions.replaceChildren(...buttons);
    renderTracks(body, playlist);
}

function renderPlaylistList(body) {
    const list = el('ul', 'playlist-list');
    for (const playlist of store.list()) {
        const li = el('li', 'playlist-row');
        const open = el('button', 'playlist-open');
        open.type = 'button';
        const icon = el('i', playlist.isDefault ? 'ph-fill ph-heart' : 'ph-fill ph-playlist');
        const text = el('span', 'playlist-text');
        text.append(el('strong', '', playlist.name),
            el('small', '', songCount(playlist.count) + (playlist.isShared ? ' · Shared' : '')));
        open.append(icon, text);
        open.addEventListener('click', () => { openPlaylistId = playlist.id; renderLibrary(); });

        const play = iconButton('ph-fill ph-play', `Play "${playlist.name}"`, 'play-music-item-btn');
        play.disabled = playlist.count === 0;
        play.addEventListener('click', async () => {
            try {
                const full = await store.get(playlist.id);
                playQueue(full.tracks, 0, full.name, full.id);
                setLibraryOpen(false);
            } catch (err) {
                showToast(err.message, 'error');
            }
        });
        li.append(open, play);
        list.appendChild(li);
    }
    const hint = isRemote() ? null : el('p', 'music-library-hint', 'Log in to share playlists and keep them on every device.');
    body.replaceChildren(list, ...(hint ? [hint] : []));

    // VI: Admin + server đã cấu hình Navidrome thì thêm dòng "NAS library" ở cuối. EN: Admin + Navidrome configured: add a "NAS library" row.
    checkNas().then((enabled) => {
        if (!enabled || openPlaylistId !== null || !list.isConnected) return;
        const li = el('li', 'playlist-row');
        const open = el('button', 'playlist-open');
        open.type = 'button';
        const text = el('span', 'playlist-text');
        text.append(el('strong', '', 'NAS library'), el('small', '', 'Your music on the NAS · only you'));
        open.append(el('i', 'ph-fill ph-hard-drives'), text);
        open.addEventListener('click', () => { openPlaylistId = NAS_VIEW; nasArtist = null; renderLibrary(); });
        li.append(open);
        list.appendChild(li);
    });
}

// VI: Hai tab: "Songs" (tìm bài) và "Artists" (duyệt theo nghệ sĩ). EN: Two tabs: "Songs" (search) and "Artists" (browse).
function renderNasView(body) {
    const tabs = el('div', 'nas-tabs');
    tabs.setAttribute('role', 'tablist');
    const content = el('div');
    for (const [key, icon, label] of [['songs', 'ph-music-notes', 'Songs'], ['artists', 'ph-microphone-stage', 'Artists']]) {
        const tab = el('button', 'nas-tab');
        tab.type = 'button';
        tab.setAttribute('role', 'tab');
        tab.setAttribute('aria-selected', String(nasTab === key));
        tab.innerHTML = `<i class="ph ${icon}"></i> ${label}`;
        tab.addEventListener('click', () => {
            if (nasTab === key) return;
            nasTab = key;
            renderNasView(body);
        });
        tabs.append(tab);
    }
    body.replaceChildren(tabs, content);
    if (nasTab === 'artists') renderArtistsTab(content);
    else renderSongsTab(content);
}

// VI: Tìm trong kho NAS; ô trống = bài ngẫu nhiên. Bấm một bài để phát cả danh sách kết quả từ bài đó.
// EN: Search the NAS library; empty = random songs. Clicking a song plays the result list from there.
function renderSongsTab(body) {
    const form = el('form', 'nas-search');
    const input = el('input');
    input.type = 'search';
    input.placeholder = 'Search songs, artists, albums...';
    input.setAttribute('aria-label', 'Search the NAS library');
    input.maxLength = 200;
    input.value = nasQuery;
    const submit = iconButton('ph ph-magnifying-glass', 'Search', 'icon-btn icon-btn-xs');
    submit.type = 'submit';
    form.append(input, submit);
    const results = el('div');
    form.addEventListener('submit', (e) => {
        e.preventDefault();
        nasQuery = input.value.trim();
        searchNas(results);
    });
    body.replaceChildren(form, results);
    if (nasResults) renderNasResults(results);
    else searchNas(results);
    input.focus();
}

async function searchNas(results) {
    results.innerHTML = '<p class="music-library-hint">Loading...</p>';
    try {
        nasResults = (await api(`/library/songs?q=${encodeURIComponent(nasQuery)}`)).map(fromNasSong);
    } catch (err) {
        nasResults = null;
        results.replaceChildren(el('p', 'music-library-hint', err.message || 'Could not reach the NAS music library.'));
        return;
    }
    if (results.isConnected) renderNasResults(results);
}

function renderNasResults(results) {
    renderNasSongs(results, nasResults, nasQuery ? `NAS: ${nasQuery}` : 'NAS library');
}

// VI: Danh sách nghệ sĩ, lọc ngay trên máy (không gọi lại server). EN: The artist list, filtered locally (no new request).
function renderArtistsTab(body) {
    const filter = el('input', 'nas-filter');
    filter.type = 'search';
    filter.placeholder = 'Filter artists...';
    filter.setAttribute('aria-label', 'Filter artists');
    filter.maxLength = 100;
    filter.value = nasArtistFilter;
    const results = el('div');
    filter.addEventListener('input', () => {
        nasArtistFilter = filter.value;
        if (nasArtists) renderArtistList(results);
    });
    body.replaceChildren(filter, results);
    if (nasArtists) renderArtistList(results);
    else loadArtists(results);
    filter.focus();
}

async function loadArtists(results) {
    results.innerHTML = '<p class="music-library-hint">Loading...</p>';
    try {
        nasArtists = await api('/library/artists');
    } catch (err) {
        nasArtists = null;
        results.replaceChildren(el('p', 'music-library-hint', err.message || 'Could not reach the NAS music library.'));
        return;
    }
    if (results.isConnected) renderArtistList(results);
}

function renderArtistList(results) {
    const term = nasArtistFilter.trim().toLowerCase();
    const shown = nasArtists.filter((a) => !term || a.name.toLowerCase().includes(term));
    if (!shown.length) {
        results.innerHTML = `<p class="empty-state"><i class="ph ph-microphone-stage"></i>${nasArtists.length ? 'No artists match.' : 'No artists on the NAS yet.'}</p>`;
        return;
    }
    const list = el('ul', 'playlist-list');
    for (const artist of shown) {
        const li = el('li', 'playlist-row');
        const open = el('button', 'playlist-open');
        open.type = 'button';
        const text = el('span', 'playlist-text');
        text.append(el('strong', '', artist.name),
            el('small', '', `${artist.albumCount} album${artist.albumCount === 1 ? '' : 's'}`));
        open.append(el('i', 'ph-fill ph-microphone-stage'), text);
        open.addEventListener('click', () => openArtist(artist));
        li.append(open);
        list.appendChild(li);
    }
    results.replaceChildren(list);
}

async function openArtist(artist) {
    nasArtist = { id: artist.id, name: artist.name, songs: null };
    renderLibrary();
    try {
        const songs = (await api(`/library/artists/${encodeURIComponent(artist.id)}/songs`)).map(fromNasSong);
        if (nasArtist?.id !== artist.id) return;
        nasArtist.songs = songs;
    } catch (err) {
        if (nasArtist?.id !== artist.id) return;
        nasArtist = null;
        showToast(err.message || 'Could not reach the NAS music library.', 'error');
    }
    if (openPlaylistId === NAS_VIEW) refreshLibrary();
}

function renderArtistSongs(body) {
    if (!nasArtist.songs) {
        body.innerHTML = '<p class="music-library-hint">Loading...</p>';
        return;
    }
    renderNasSongs(body, nasArtist.songs, `NAS: ${nasArtist.name}`);
}

function renderNasSongs(results, songs, queueName) {
    if (!songs.length) {
        results.innerHTML = '<p class="empty-state"><i class="ph ph-music-notes-simple"></i>No songs found.</p>';
        return;
    }
    const list = el('ul', 'music-list');
    songs.forEach((song, index) => {
        const li = el('li', 'music-item');
        li.classList.toggle('is-playing', player.tracks[player.index]?.id === song.id);
        const info = el('div', 'music-item-info');
        const text = el('div');
        text.append(el('span', '', song.name));
        if (song.album) text.append(el('small', '', song.album));
        info.append(el('i', 'ph-fill ph-hard-drives'), text);
        const play = iconButton('ph-fill ph-play', 'Play from here', 'play-music-item-btn');
        play.addEventListener('click', () => {
            playQueue(songs, index, queueName);
            setLibraryOpen(false);
        });
        // VI: Lưu bài NAS vào playlist của mình (chia sẻ playlist thì khách nghe được qua link). EN: Save a NAS song to your playlist (sharing the playlist lets visitors hear it).
        const save = iconButton('ph ph-heart', 'Save to a playlist', 'play-music-item-btn');
        save.addEventListener('click', () => {
            setLibraryOpen(false);
            openSaveDialog(song.url, song.name);
        });
        // VI: Dùng làm nhạc hồ sơ (bio); profile.js lưu lại. EN: Use as the profile (bio) song; profile.js saves it.
        const bio = iconButton('ph ph-user-sound', 'Use as profile song', 'play-music-item-btn');
        bio.addEventListener('click', () => {
            window.dispatchEvent(new CustomEvent('studyhub:profile-song', { detail: { url: song.url, name: song.name } }));
        });
        const controls = el('div', 'music-item-controls');
        controls.append(bio, save, play);
        li.append(info, controls);
        list.appendChild(li);
    });
    results.replaceChildren(list);
}

// VI: Dùng textContent để chặn XSS. EN: Uses textContent to prevent XSS.
function renderTracks(body, playlist) {
    if (!playlist.tracks.length) {
        body.innerHTML = '<p class="empty-state"><i class="ph ph-music-notes-simple"></i>No songs yet. Paste a link above and tap the heart to add one.</p>';
        return;
    }
    const list = el('ul', 'music-list');
    playlist.tracks.forEach((music, index) => {
        const li = el('li', 'music-item');
        li.classList.toggle('is-playing', player.playlistId === playlist.id && player.tracks[player.index]?.id === music.id);
        li.innerHTML = `
            <div class="music-item-info">
                <i class="ph-fill ${SOURCE_ICONS[music.type] || 'ph-music-notes'}"></i>
                <div>
                    <span></span>
                    <a target="_blank" rel="noopener"></a>
                </div>
            </div>
            <div class="music-item-controls">
                <button type="button" class="play-music-item-btn" title="Play from here"><i class="ph-fill ph-play"></i></button>
                <button type="button" class="delete-music-btn" title="Remove from playlist"><i class="ph ph-trash"></i></button>
            </div>
        `;
        li.querySelector('.music-item-info span').textContent = music.name;
        const linkEl = li.querySelector('.music-item-info a');
        linkEl.textContent = music.type === 'nas' ? 'NAS library' : music.url;
        if (/^https?:\/\//i.test(music.url)) linkEl.href = music.url;
        li.querySelector('.play-music-item-btn').addEventListener('click', () => {
            playQueue(playlist.tracks, index, playlist.name, playlist.id);
            setLibraryOpen(false);
        });
        li.querySelector('.delete-music-btn').addEventListener('click', () => removeTrack(playlist, music));
        list.appendChild(li);
    });
    body.replaceChildren(list);
}

async function removeTrack(playlist, music) {
    if (!confirm(`Remove "${music.name}" from "${playlist.name}"?`)) return;
    try {
        await store.removeTrack(playlist.id, music.id);
    } catch (err) {
        showToast(err.message, 'error');
        if (remotePlaylists) loadRemoteMusic().catch(() => {});
    }
    refreshLibrary();
}

async function deletePlaylist(playlist) {
    if (!confirm(`Delete the playlist "${playlist.name}" and its ${songCount(playlist.tracks.length)}?`)) return;
    try {
        await store.remove(playlist.id);
        openPlaylistId = null;
        showToast(`Deleted "${playlist.name}".`, 'success');
    } catch (err) {
        showToast(err.message, 'error');
    }
    refreshLibrary();
}

function setLibraryOpen(open) {
    const library = document.getElementById('music-library');
    const btn = document.getElementById('music-library-btn');
    if (!library) return;
    library.hidden = !open;
    btn?.setAttribute('aria-expanded', String(open));
    if (open) renderLibrary();
}

function openModal(modal) {
    modal.dataset.openedAt = Date.now();
    modal.classList.remove('hidden');
}

// ---------- Create / rename ----------
let renaming = null;

function openNameModal(playlist = null) {
    const modal = document.getElementById('playlist-name-modal');
    const input = document.getElementById('playlist-name-input');
    renaming = playlist;
    document.getElementById('playlist-name-heading').textContent = playlist ? 'Rename playlist' : 'New playlist';
    input.value = playlist ? playlist.name : '';
    setLibraryOpen(false);
    openModal(modal);
    input.focus();
    input.select();
}

// ---------- Share ----------
let sharingId = null;

function renderShareModal(playlist) {
    document.getElementById('playlist-share-name').textContent = playlist.name;
    const toggle = document.getElementById('playlist-share-toggle');
    toggle.checked = playlist.isShared;
    document.getElementById('playlist-share-text').textContent = playlist.isShared
        ? 'Shared: anyone with the link can listen'
        : 'Private: only you can see it';
    const row = document.getElementById('playlist-share-link-row');
    row.hidden = !playlist.isShared;
    document.getElementById('playlist-share-link').value = playlist.isShared ? shareUrl(playlist.shareCode) : '';
}

async function openShareModal(id) {
    if (!remotePlaylists) {
        showToast('Log in to share playlists.', 'info');
        return;
    }
    const playlist = findRemote(id);
    sharingId = playlist.id;
    renderShareModal(playlist);
    setLibraryOpen(false);
    openModal(document.getElementById('playlist-share-modal'));
}

// ---------- Save a link to a playlist ----------
function fillPlaylistSelect(select) {
    const playlists = store.list();
    const last = lastPlaylistId();
    select.replaceChildren(...playlists.map(p => {
        const option = el('option', '', p.name);
        option.value = p.id;
        return option;
    }));
    const newOption = el('option', '', '+ New playlist');
    newOption.value = '__new';
    select.appendChild(newOption);
    select.value = playlists.some(p => String(p.id) === last) ? last : String(playlists[0]?.id ?? '__new');
}

// ---------- Shared playlist (/?playlist=code) ----------
let sharedPlaylist = null;

export async function openSharedPlaylist(code) {
    const modal = document.getElementById('shared-playlist-modal');
    if (!modal || !/^[A-Za-z0-9]{10}$/.test(code)) return;
    try {
        sharedPlaylist = await api(`/shared-playlists/${encodeURIComponent(code)}`);
    } catch (err) {
        showToast(err.status === 404 ? 'This playlist is no longer shared.' : 'Could not load the shared playlist.', 'error', 5000);
        return;
    }
    // VI: Bài NAS nghe qua mã chia sẻ. EN: NAS songs play through the share code.
    const tracks = sharedPlaylist.tracks.map(fromTrack)
        .map(t => (t.type === 'nas' ? { ...t, shareCode: sharedPlaylist.shareCode } : t));
    sharedPlaylist.items = tracks;
    document.getElementById('shared-playlist-name').textContent = sharedPlaylist.name;

    const owner = document.getElementById('shared-playlist-owner');
    owner.replaceChildren('Shared by ');
    if (sharedPlaylist.ownerSlug) {
        const link = el('a', '', sharedPlaylist.ownerName);
        link.href = publicBaseUrl() ? `${publicBaseUrl()}/@${sharedPlaylist.ownerSlug}`
            : `${new URL('./', location.href).href.split(/[?#]/)[0]}?u=${encodeURIComponent(sharedPlaylist.ownerSlug)}`;
        link.target = '_blank';
        link.rel = 'noopener';
        owner.appendChild(link);
    } else {
        owner.appendChild(el('strong', '', sharedPlaylist.ownerName));
    }
    owner.append(` · ${songCount(tracks.length)}`);

    const list = document.getElementById('shared-playlist-tracks');
    list.replaceChildren(...tracks.map((music, index) => {
        const li = el('li', 'music-item');
        const info = el('div', 'music-item-info');
        const text = el('div');
        text.append(el('span', '', music.name));
        info.append(el('i', `ph-fill ${SOURCE_ICONS[music.type] || 'ph-music-notes'}`), text);
        const play = iconButton('ph-fill ph-play', 'Play from here', 'play-music-item-btn');
        play.addEventListener('click', () => {
            playQueue(tracks, index, sharedPlaylist.name);
            modal.classList.add('hidden');
        });
        li.append(info, play);
        return li;
    }));
    if (!tracks.length) list.innerHTML = '<li class="empty-state"><i class="ph ph-music-notes-simple"></i>This playlist is empty.</li>';
    document.getElementById('shared-playlist-play').disabled = !tracks.length;
    openModal(modal);
}

async function saveSharedPlaylist() {
    if (!sharedPlaylist) return;
    const btn = document.getElementById('shared-playlist-save');
    btn.disabled = true;
    try {
        if (remotePlaylists) {
            replaceRemote(await api(`/shared-playlists/${sharedPlaylist.shareCode}/copy`, { method: 'POST' }));
        } else {
            await store.create(sharedPlaylist.name, sharedPlaylist.items.map((t, i) => ({ ...t, id: Date.now() + i })));
        }
        showToast(`Saved "${sharedPlaylist.name}" to your playlists.`, 'success');
        document.getElementById('shared-playlist-modal').classList.add('hidden');
    } catch (err) {
        showToast(err.message, 'error');
    } finally {
        btn.disabled = false;
    }
}

// ---------- Wiring ----------
export function initMusic() {
    const musicLinkInput = document.getElementById('music-link-input');
    const saveMusicBtn = document.getElementById('save-music-btn');
    const playMusicBtn = document.getElementById('play-music-btn');
    const container = document.getElementById('music-player-container');
    const islandBtn = document.getElementById('music-island-btn');
    const closeBtn = document.getElementById('music-close-btn');
    const saveModal = document.getElementById('music-name-modal');
    const saveForm = document.getElementById('music-name-form');
    const saveName = document.getElementById('music-name-input');
    const saveSelect = document.getElementById('music-playlist-select');
    const saveNewName = document.getElementById('music-new-playlist-input');
    const syncNewName = () => {
        saveNewName.closest('.input-group').hidden = saveSelect.value !== '__new';
        saveNewName.required = saveSelect.value === '__new';
    };
    saveSelect?.addEventListener('change', () => {
        syncNewName();
        if (saveSelect.value === '__new') saveNewName.focus();
    });
    const saveCancel = document.getElementById('music-name-cancel');
    const libraryBtn = document.getElementById('music-library-btn');
    const libraryEl = document.getElementById('music-library');

    let pendingLink = '';

    openSaveDialog = (link, name = '') => {
        pendingLink = link;
        saveForm.reset();
        saveName.value = name;
        fillPlaylistSelect(saveSelect);
        syncNewName();
        openModal(saveModal);
        saveName.focus();
    };

    saveMusicBtn?.addEventListener('click', () => {
        const link = musicLinkInput.value.trim();
        // VI: Early returns để validate input ngay từ đầu. EN: Early returns to validate input upfront.
        if (!link) return showToast('Please paste a link first.', 'error');
        if (!getEmbedUrl(link)) return showToast('Invalid link. Only Spotify or YouTube links are supported.', 'error');
        openSaveDialog(link);
    });

    saveForm?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const name = saveName.value.trim();
        if (!name || !pendingLink) return;
        const link = pendingLink;
        const submit = saveForm.querySelector('[type="submit"]');
        submit.disabled = true;
        try {
            let playlistId = saveSelect.value;
            if (playlistId === '__new') {
                const listName = saveNewName.value.trim();
                if (!listName) {
                    saveNewName.focus();
                    return showToast('Give the new playlist a name.', 'error');
                }
                playlistId = (await store.create(listName)).id;
            }
            const playlist = await store.addTrack(playlistId, { name, url: link });
            rememberPlaylist(playlist.id);
            saveModal.classList.add('hidden');
            pendingLink = '';
            showToast(`Saved to "${playlist.name}".`, 'success');
            refreshLibrary();
        } catch (err) {
            showToast(err.message, 'error');
        } finally {
            submit.disabled = false;
        }
    });

    saveCancel?.addEventListener('click', () => saveModal.classList.add('hidden'));

    if (playMusicBtn) {
        playMusicBtn.addEventListener('click', () => {
            const link = musicLinkInput.value.trim();
            if (link) playMusic(link);
        });
        musicLinkInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') playMusicBtn.click();
        });
    }

    // VI: Bấm đảo để mở rộng/thu gọn; thu gọn vẫn giữ iframe nên nhạc vẫn phát. Đóng thì gỡ iframe để dừng hẳn.
    // EN: Tap the island to expand/collapse; collapsed keeps the iframe so audio continues. Close removes it to stop playback.
    islandBtn?.addEventListener('click', () => setExpanded(!container.classList.contains('expanded')));
    if (container) makeIslandDraggable(container);
    document.addEventListener('click', (e) => {
        if (container.classList.contains('expanded') && e.target.isConnected && !container.contains(e.target)) setExpanded(false);
    });
    closeBtn?.addEventListener('click', stopPlayer);

    document.getElementById('music-video-btn')?.addEventListener('click', () => setShowVideo(!player.showVideo));
    document.getElementById('music-toggle-btn')?.addEventListener('click', () => player.control?.toggle());

    document.getElementById('music-prev-btn')?.addEventListener('click', playPrev);
    document.getElementById('music-next-btn')?.addEventListener('click', () => playNext());
    document.getElementById('music-repeat-btn')?.addEventListener('click', () => {
        player.repeat = !player.repeat;
        renderPlayerHead();
        showToast(player.repeat ? 'Repeat playlist: on' : 'Repeat playlist: off', 'info', 1500);
    });

    // VI: Mở/đóng danh sách playlist; bấm ra ngoài hoặc Esc để đóng.
    // EN: Toggle the playlists popover; click outside or press Esc to close.
    if (libraryBtn && libraryEl) {
        libraryBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            setLibraryOpen(libraryEl.hidden);
        });
        document.addEventListener('click', (e) => {
            // VI: Bỏ qua phần tử vừa bị vẽ lại (vd. nút xóa). EN: Ignore targets removed by a re-render (e.g. delete).
            if (!libraryEl.hidden && e.target.isConnected && !libraryEl.contains(e.target)) setLibraryOpen(false);
        });
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') setLibraryOpen(false);
        });
        document.getElementById('music-library-back')?.addEventListener('click', () => {
            // VI: Đang xem một nghệ sĩ thì lùi về danh sách nghệ sĩ. EN: From an artist, go back to the artist list.
            if (openPlaylistId === NAS_VIEW && nasArtist) nasArtist = null;
            else openPlaylistId = null;
            renderLibrary();
        });
    }

    // Create / rename form
    const nameModal = document.getElementById('playlist-name-modal');
    document.getElementById('playlist-name-form')?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const name = document.getElementById('playlist-name-input').value.trim();
        if (!name) return;
        try {
            const playlist = renaming ? await store.rename(renaming.id, name) : await store.create(name);
            nameModal.classList.add('hidden');
            showToast(renaming ? 'Playlist renamed.' : `Created "${playlist.name}".`, 'success');
            openPlaylistId = playlist.id;
            setLibraryOpen(true);
        } catch (err) {
            showToast(err.message, 'error');
        }
    });
    document.getElementById('playlist-name-cancel')?.addEventListener('click', () => nameModal.classList.add('hidden'));

    // Share dialog
    const shareModal = document.getElementById('playlist-share-modal');
    const shareToggle = document.getElementById('playlist-share-toggle');
    shareToggle?.addEventListener('change', async () => {
        if (!sharingId) return;
        if (!shareToggle.checked && !confirm('Stop sharing? The current link will stop working.')) {
            shareToggle.checked = true;
            return;
        }
        shareToggle.disabled = true;
        try {
            renderShareModal(await store.setShared(sharingId, shareToggle.checked));
        } catch (err) {
            shareToggle.checked = !shareToggle.checked;
            showToast(err.message, 'error');
        } finally {
            shareToggle.disabled = false;
        }
    });
    document.getElementById('playlist-share-copy')?.addEventListener('click', async () => {
        const input = document.getElementById('playlist-share-link');
        try {
            await navigator.clipboard.writeText(input.value);
            showToast('Link copied.', 'success', 1500);
        } catch (e) {
            input.select();
            showToast('Press Ctrl+C to copy the link.', 'info');
        }
    });
    document.getElementById('playlist-share-close')?.addEventListener('click', () => shareModal.classList.add('hidden'));

    // Shared playlist dialog
    const sharedModal = document.getElementById('shared-playlist-modal');
    document.getElementById('shared-playlist-play')?.addEventListener('click', () => {
        if (!sharedPlaylist) return;
        playQueue(sharedPlaylist.items, 0, sharedPlaylist.name);
        sharedModal.classList.add('hidden');
    });
    document.getElementById('shared-playlist-save')?.addEventListener('click', saveSharedPlaylist);
    document.getElementById('shared-playlist-close')?.addEventListener('click', () => sharedModal.classList.add('hidden'));
}
