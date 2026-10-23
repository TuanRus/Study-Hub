// VI: Điều phối nguồn dữ liệu: khi đăng nhập và backend chạy thì tải mọi thứ từ server,
//     khi đăng xuất / mất kết nối thì quay về localStorage. Đồng thời đề nghị chuyển dữ liệu cũ lên tài khoản.
// EN: Coordinates the data source: logged in + backend up loads everything from the server,
//     logging out / losing the backend falls back to localStorage. Also offers to move old local data to the account.

import { api, getSession, setSession, isRemote, sessionUser, checkBackend } from '../utils/api.js';
import { getStorageData } from '../utils/storage.js';
import { showToast } from '../utils/toast.js';
import { FOCUS_LOG_KEY, loadRemoteFocus, clearRemoteFocus } from '../utils/focusLog.js';
import { TODOS_STORAGE_KEY, loadRemoteTodos, clearRemoteTodos } from './todo.js';
import { MUSIC_STORAGE_KEY, loadRemoteMusic, clearRemoteMusic } from './music.js';
import { PROFILE_KEY, loadRemoteProfile, clearRemoteProfile } from './profile.js';
import { loadRemotePomodoroSettings, clearRemotePomodoroSettings } from './pomodoro.js';
import { remindDueTasks } from './deadlines.js';

const IMPORT_KEY_PREFIX = 'studyhub_import_';

// VI: Tải toàn bộ dữ liệu của tài khoản. Trả về false nếu không dùng được server.
// EN: Loads all account data. Returns false when the server can't be used.
export async function startSync({ offerImport = true } = {}) {
    if (!isRemote()) return false;
    try {
        const [profile] = await Promise.all([
            loadRemoteProfile(),
            loadRemoteTodos(),
            loadRemoteMusic(),
            loadRemotePomodoroSettings(),
        ]);
        await loadRemoteFocus(profile.stats.totalFocusMinutes);
        // VI: Cập nhật quyền (vd. vừa được nâng Admin) mà không cần đăng nhập lại. EN: Pick up role changes without signing in again.
        const me = await api('/auth/me');
        const session = getSession();
        if (session) setSession({ ...session, user: me });
    } catch (err) {
        console.warn('Sync failed, using local data:', err);
        stopSync();
        if (getSession()) showToast('Could not load your account data. Showing data saved on this device.', 'error', 5000);
        return false;
    }
    window.dispatchEvent(new CustomEvent('studyhub:auth'));
    window.dispatchEvent(new CustomEvent('studyhub:synced'));
    remindDueTasks();
    if (offerImport) offerLocalImport();
    return true;
}

export function stopSync() {
    clearRemoteTodos();
    clearRemoteMusic();
    clearRemoteFocus();
    clearRemoteProfile();
    clearRemotePomodoroSettings();
    window.dispatchEvent(new CustomEvent('studyhub:auth'));
}

// VI: Gọi khi mở app: kiểm tra backend rồi tải dữ liệu nếu có phiên đăng nhập.
// EN: Called on startup: checks the backend and loads data when there is a session.
export async function bootSync() {
    const online = await checkBackend();
    if (!getSession()) return;
    if (!online) {
        showToast('Study Hub server is offline. Showing data saved on this device.', 'info', 5000);
        remindDueTasks();
        return;
    }
    await startSync();
}

// ---------- Moving localStorage data to the account ----------
function localData() {
    const todos = getStorageData(TODOS_STORAGE_KEY, []);
    const music = getStorageData(MUSIC_STORAGE_KEY, []);
    const focusLog = getStorageData(FOCUS_LOG_KEY, {});
    const profile = getStorageData(PROFILE_KEY, null);
    return { todos, music, focusLog, profile };
}

function describe({ todos, music, focusLog, profile }) {
    const parts = [];
    const days = Object.keys(focusLog).length;
    if (todos.length) parts.push(`${todos.length} task${todos.length > 1 ? 's' : ''}`);
    if (music.length) parts.push(`${music.length} saved song${music.length > 1 ? 's' : ''}`);
    if (days) parts.push(`${days} day${days > 1 ? 's' : ''} of focus history`);
    if (profile) parts.push('your About profile');
    return parts;
}

function offerLocalImport() {
    const user = sessionUser();
    if (!user) return;
    const key = IMPORT_KEY_PREFIX + user.id;
    if (localStorage.getItem(key)) return;

    const data = localData();
    const parts = describe(data);
    if (!parts.length) return;

    const modal = document.getElementById('import-modal');
    if (!modal) return;
    document.getElementById('import-summary').textContent = parts.join(', ');
    modal.dataset.openedAt = Date.now();
    modal.classList.remove('hidden');

    const yes = document.getElementById('import-confirm');
    const no = document.getElementById('import-skip');
    const close = (answer) => {
        modal.classList.add('hidden');
        yes.onclick = no.onclick = null;
        try { localStorage.setItem(key, answer); } catch (e) { /* ignore */ }
    };

    no.onclick = () => close('skipped');
    yes.onclick = async () => {
        yes.disabled = true;
        try {
            const result = await api('/import/local', { method: 'POST', body: data });
            close('done');
            const songs = result.tracksImported;
            showToast(`Uploaded ${result.tasksImported} tasks, ${songs} songs, ${result.focusSessionsImported} focus sessions` +
                (result.profileImported ? ' and your profile.' : '.'), 'success', 5000);
            await startSync({ offerImport: false });
        } catch (err) {
            showToast(`Upload failed: ${err.message}`, 'error', 5000);
        } finally {
            yes.disabled = false;
        }
    };
}
