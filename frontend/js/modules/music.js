// VI: Nhạc: playlist có tên (Favorites + playlist tự tạo), chia sẻ playlist bằng link, và trình phát nổi phát cả playlist theo thứ tự.
//     Đăng nhập thì playlist nằm trên server; chưa đăng nhập thì nằm trong localStorage (không chia sẻ được).
// EN: Music: named playlists (Favorites + your own), sharing a playlist by link, and a floating player that plays a whole playlist in order.
//     Logged in, playlists live on the server; logged out, they live in localStorage (no sharing).

import { getStorageData, setStorageData } from '../utils/storage.js';
import { showToast } from '../utils/toast.js';
import { api, isRemote, publicBaseUrl } from '../utils/api.js';

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
    refreshLibrary();
}

export function clearRemoteMusic() {
    remotePlaylists = null;
    refreshLibrary();
}

// ---------- Links ----------
const YT_REGEX = /(?:youtu\.be\/|youtube\.com\/(?:embed\/|v\/|watch\?v=|watch\?.+&v=|live\/|shorts\/))([\w-]{11})/;

function getMusicType(link) {
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
// VI: control = { toggle } của nguồn đang phát (YouTube, Spotify); null khi chưa điều khiển được.
// EN: control = { toggle } of the current source (YouTube, Spotify); null while it can't be controlled yet.
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

function setPlaying(on) {
    player.playing = on;
    syncIsland();
}

const SOURCE_ICONS = { youtube: 'ph-youtube-logo', spotify: 'ph-spotify-logo' };

function syncIsland() {
    const container = document.getElementById('music-player-container');
    const track = player.tracks[player.index];
    const type = track ? getMusicType(track.url) : '';
    for (const t of ['youtube', 'spotify']) container?.classList.toggle(`is-${t}`, type === t);
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
    if (!getEmbedUrl(track.url)) {
        showToast(`Can't play "${track.name || track.url}". Skipping.`, 'error');
        if (index < player.tracks.length - 1) playAt(index + 1);
        return;
    }
    player.index = index;
    mountTrack(track);
    renderPlayerHead();
    container.classList.add('active');
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
        linkEl.textContent = music.url;
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
    const tracks = sharedPlaylist.tracks.map(fromTrack);
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

    saveMusicBtn?.addEventListener('click', () => {
        const link = musicLinkInput.value.trim();
        // VI: Early returns để validate input ngay từ đầu. EN: Early returns to validate input upfront.
        if (!link) return showToast('Please paste a link first.', 'error');
        if (!getEmbedUrl(link)) return showToast('Invalid link. Only Spotify or YouTube links are supported.', 'error');

        pendingLink = link;
        saveForm.reset();
        fillPlaylistSelect(saveSelect);
        syncNewName();
        openModal(saveModal);
        saveName.focus();
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
            openPlaylistId = null;
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
