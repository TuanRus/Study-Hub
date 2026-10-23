// VI: Module quản lý Playlist, ứng dụng Factory/Strategy pattern ngầm để phân loại nền tảng. 
// EN: Playlist management module, implicitly applying Factory/Strategy pattern to classify platforms.

import { getStorageData, setStorageData } from '../utils/storage.js';
import { showToast } from '../utils/toast.js';
import { api, enqueue } from '../utils/api.js';

export const MUSIC_STORAGE_KEY = 'studyhub_music';

// VI: Khi đăng nhập, danh sách yêu thích là playlist "Favorites" trên server. null = dùng localStorage.
// EN: When logged in, favorites are the server's "Favorites" playlist. null = use localStorage.
let remoteFavorites = null; // { id, tracks: [{ id, name, url, type }] }

function fromTrack(track) {
    return { id: track.id, name: track.title, url: track.sourceUrl, type: String(track.provider).toLowerCase() };
}

export async function loadRemoteMusic() {
    const playlists = await api('/playlists');
    const favorites = playlists.find(p => p.isDefault) || playlists[0];
    const detail = favorites ? await api(`/playlists/${favorites.id}`) : { id: null, tracks: [] };
    remoteFavorites = { id: detail.id, tracks: detail.tracks.map(fromTrack) };
    fetchFavoriteMusic();
}

export function clearRemoteMusic() {
    remoteFavorites = null;
    fetchFavoriteMusic();
}

function getMusicList() {
    return remoteFavorites ? [...remoteFavorites.tracks] : getStorageData(MUSIC_STORAGE_KEY, []);
}
const YT_REGEX = /(?:youtu\.be\/|youtube\.com\/(?:embed\/|v\/|watch\?v=|watch\?.+&v=|live\/|shorts\/))([\w-]{11})/;

function getMusicType(link) {
    if (link.includes('spotify.com')) return 'spotify';
    if (link.includes('youtube.com') || link.includes('youtu.be')) return 'youtube';
    return '';
}

// VI: Phân tích URL dựa theo signature của từng service. 
// EN: Parsing URL based on each service's signature.
function getEmbedUrl(link) {
    const type = getMusicType(link);
    if (type === 'spotify') {
        try {
            // VI: Bỏ tiền tố ngôn ngữ (vd: /intl-vi/track/...). EN: Strip locale prefix (e.g. /intl-vi/track/...).
            const path = new URL(link).pathname.replace(/^\/intl-[\w-]+/, '');
            return `https://open.spotify.com/embed${path}`;
        } catch (e) {
            return '';
        }
    }
    if (type === 'youtube') {
        const match = link.match(YT_REGEX);
        if (match && match[1]) {
            return `https://www.youtube.com/embed/${match[1]}?autoplay=1&origin=${window.location.origin}`;
        }
    }
    return '';
}

export function playMusic(link) {
    const container = document.getElementById('music-player-container');
    const frame = document.getElementById('music-frame');
    const embedUrl = getEmbedUrl(link);

    if (!embedUrl || !container || !frame) {
        showToast('Invalid link. Please try a valid Spotify or YouTube link.', 'error');
        return;
    }

    const iframe = document.createElement('iframe');
    iframe.src = embedUrl;
    iframe.allow = 'autoplay; encrypted-media';
    iframe.allowFullscreen = true;
    frame.replaceChildren(iframe);
    container.classList.add('active');
    container.classList.remove('minimized');
}

export function fetchFavoriteMusic() {
    renderFavoriteMusic(getMusicList());
}

// VI: Render function được tách riêng để thực hiện Data Binding một chiều. Dùng textContent để chặn XSS.
// EN: Render function isolated to perform one-way Data Binding. Uses textContent to prevent XSS.
function renderFavoriteMusic(musicList) {
    const musicListEl = document.getElementById('favorite-music-list');
    
    // VI: Guard clause bảo vệ logic thao tác DOM. 
    // EN: Guard clause protecting DOM manipulation logic.
    if (!musicListEl) return;
    musicListEl.innerHTML = '';

    if (musicList.length === 0) {
        musicListEl.innerHTML = '<li class="empty-state"><i class="ph ph-music-notes-simple"></i>No saved music yet. Paste a link and tap the heart to save it.</li>';
        return;
    }

    musicList.forEach(music => {
        const li = document.createElement('li');
        li.className = 'music-item';
        li.innerHTML = `
            <div class="music-item-info">
                <i class="ph-fill ${music.type === 'youtube' ? 'ph-youtube-logo' : 'ph-spotify-logo'}"></i>
                <div>
                    <span></span>
                    <a target="_blank" rel="noopener"></a>
                </div>
            </div>
            <div class="music-item-controls">
                <button class="play-music-item-btn" title="Play"><i class="ph-fill ph-play"></i></button>
                <button class="delete-music-btn" title="Delete"><i class="ph ph-trash"></i></button>
            </div>
        `;
        li.querySelector('.music-item-info span').textContent = music.name;
        const linkEl = li.querySelector('.music-item-info a');
        linkEl.textContent = music.url;
        if (/^https?:\/\//i.test(music.url)) linkEl.href = music.url;
        musicListEl.appendChild(li);

        li.querySelector('.play-music-item-btn').addEventListener('click', () => playMusic(music.url));
        li.querySelector('.delete-music-btn').addEventListener('click', () => {
            deleteMusic(music.id);
        });
    });
}

function deleteMusic(id) {
    if (!confirm("Are you sure you want to delete this saved music?")) return;
    if (remoteFavorites) {
        const playlistId = remoteFavorites.id;
        remoteFavorites.tracks = remoteFavorites.tracks.filter(music => music.id !== id);
        fetchFavoriteMusic();
        enqueue(() => api(`/playlists/${playlistId}/tracks/${id}`, { method: 'DELETE' }), (err) => {
            showToast(err.message, 'error');
            loadRemoteMusic().catch(() => {});
        });
        return;
    }
    const musicList = getStorageData(MUSIC_STORAGE_KEY, []).filter(music => music.id !== id);
    setStorageData(MUSIC_STORAGE_KEY, musicList);
    fetchFavoriteMusic();
}

export function initMusic() {
    const musicLinkInput = document.getElementById('music-link-input');
    const saveMusicBtn = document.getElementById('save-music-btn');
    const playMusicBtn = document.getElementById('play-music-btn');
    const container = document.getElementById('music-player-container');
    const frame = document.getElementById('music-frame');
    const minimizeBtn = document.getElementById('music-minimize-btn');
    const closeBtn = document.getElementById('music-close-btn');
    const nameModal = document.getElementById('music-name-modal');
    const nameForm = document.getElementById('music-name-form');
    const nameInput = document.getElementById('music-name-input');
    const nameCancel = document.getElementById('music-name-cancel');
    const libraryBtn = document.getElementById('music-library-btn');
    const libraryEl = document.getElementById('music-library');

    let pendingLink = '';

    if(saveMusicBtn) {
        saveMusicBtn.addEventListener('click', () => {
            const link = musicLinkInput.value.trim();
            
            // VI: Early returns để validate input ngay từ đầu. 
            // EN: Early returns to validate input upfront.
            if (!link) return showToast("Please paste a link first.", 'error');
            if (!getMusicType(link)) return showToast("Invalid link. Only Spotify or YouTube links are supported.", 'error');

            pendingLink = link;
            nameForm.reset();
            nameModal.classList.remove('hidden');
            nameInput.focus();
        });
    }

    if (nameForm) {
        nameForm.addEventListener('submit', (e) => {
            e.preventDefault();
            const name = nameInput.value.trim();
            if (!name || !pendingLink) return;

            const link = pendingLink;
            nameModal.classList.add('hidden');
            pendingLink = '';

            if (remoteFavorites) {
                const playlistId = remoteFavorites.id;
                enqueue(async () => {
                    const track = await api(`/playlists/${playlistId}/tracks`, { method: 'POST', body: { url: link, title: name } });
                    remoteFavorites?.tracks.push(fromTrack(track));
                    showToast("Music saved to favorites!", 'success');
                    fetchFavoriteMusic();
                }, (err) => showToast(err.status === 409 ? 'This song is already in your favorites.' : err.message, 'error'));
                return;
            }

            const newMusic = { id: Date.now(), name: name, url: link, type: getMusicType(link) };
            const musicList = getStorageData(MUSIC_STORAGE_KEY, []);
            musicList.push(newMusic);
            setStorageData(MUSIC_STORAGE_KEY, musicList);

            showToast("Music saved to favorites!", 'success');
            fetchFavoriteMusic();
        });
    }

    if (nameCancel) nameCancel.addEventListener('click', () => nameModal.classList.add('hidden'));

    if (playMusicBtn) {
        playMusicBtn.addEventListener('click', () => {
            const link = musicLinkInput.value.trim();
            if (link) playMusic(link);
        });
        musicLinkInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') playMusicBtn.click();
        });
    }

    // VI: Thu nhỏ giữ nguyên iframe để nhạc vẫn phát; đóng thì gỡ iframe để dừng hẳn.
    // EN: Minimize keeps the iframe so audio continues; close removes it to stop playback.
    if (minimizeBtn) {
        minimizeBtn.addEventListener('click', () => {
            const isMinimized = container.classList.toggle('minimized');
            minimizeBtn.innerHTML = `<i class="ph-bold ${isMinimized ? 'ph-arrows-out-simple' : 'ph-minus'}"></i>`;
            minimizeBtn.title = isMinimized ? 'Expand' : 'Minimize';
        });
    }

    if (closeBtn) {
        closeBtn.addEventListener('click', () => {
            frame.replaceChildren();
            container.classList.remove('active', 'minimized');
        });
    }

    // VI: Mở/đóng danh sách nhạc đã lưu; bấm ra ngoài hoặc Esc để đóng.
    // EN: Toggle the saved music popover; click outside or press Esc to close.
    function setLibraryOpen(open) {
        if (!libraryEl) return;
        libraryEl.hidden = !open;
        libraryBtn.setAttribute('aria-expanded', String(open));
        if (open) fetchFavoriteMusic();
    }

    if (libraryBtn && libraryEl) {
        libraryBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            setLibraryOpen(libraryEl.hidden);
        });
        document.addEventListener('click', (e) => {
            // VI: Bỏ qua nút xóa vì danh sách được vẽ lại sau khi xóa.
            // EN: Ignore clicks whose target was removed by a re-render (e.g. delete).
            if (!libraryEl.hidden && e.target.isConnected && !libraryEl.contains(e.target)) setLibraryOpen(false);
        });
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') setLibraryOpen(false);
        });
        libraryEl.addEventListener('click', (e) => {
            if (e.target.closest('.play-music-item-btn')) setLibraryOpen(false);
        });
    }

    fetchFavoriteMusic();
}
