// VI: Lưu số phiên và số phút tập trung theo từng ngày để hiển thị ở trang Today.
//     Khi đăng nhập và backend chạy: ghi phiên lên server (/api/focus-sessions), đọc thống kê từ /api/stats/summary.
// EN: Stores focus sessions and minutes per day for the Today dashboard.
//     Logged in + backend up: sessions go to the server (/api/focus-sessions), stats come from /api/stats/summary.

import { getStorageData, setStorageData } from './storage.js';
import { api, enqueue, timeZone } from './api.js';

export const FOCUS_LOG_KEY = 'studyhub_focus_log';

// VI: null = dùng localStorage. EN: null = use localStorage.
let remote = null; // { today: { sessions, minutes }, totalMinutes }

export function todayKey(date = new Date()) {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
}

function notify() {
    window.dispatchEvent(new CustomEvent('studyhub:focus'));
}

export function getTodayFocus() {
    if (remote) return { ...remote.today };
    const log = getStorageData(FOCUS_LOG_KEY, {});
    return log[todayKey()] || { sessions: 0, minutes: 0 };
}

export function getTotalFocusMinutes() {
    if (remote) return remote.totalMinutes;
    const log = getStorageData(FOCUS_LOG_KEY, {});
    return Object.values(log).reduce((sum, day) => sum + (day.minutes || 0), 0);
}

// VI: totalMinutes lấy từ thống kê hồ sơ (tính trên toàn bộ lịch sử). EN: totalMinutes comes from the profile stats (all-time).
export async function loadRemoteFocus(totalMinutes) {
    const summary = await api(`/stats/summary?days=1&timeZone=${encodeURIComponent(timeZone)}`);
    remote = {
        today: { sessions: summary.today.sessions, minutes: summary.today.focusMinutes },
        totalMinutes: totalMinutes ?? remote?.totalMinutes ?? summary.totalFocusMinutes,
    };
    notify();
}

export function clearRemoteFocus() {
    remote = null;
    notify();
}

// VI: seconds = độ dài thật của phiên (gửi lên server); minutes dùng cho log trên máy.
// EN: seconds = the real phase length (sent to the server); minutes is used by the on-device log.
export function logFocusSession(minutes, seconds = minutes * 60) {
    if (remote) {
        remote.today.sessions += 1;
        remote.today.minutes += minutes;
        remote.totalMinutes += minutes;
        notify();
        const endedAt = new Date();
        const startedAt = new Date(endedAt.getTime() - Math.max(1, seconds) * 1000);
        enqueue(() => api('/focus-sessions', {
            method: 'POST',
            body: { kind: 'Work', startedAt: startedAt.toISOString(), endedAt: endedAt.toISOString(), completed: true },
        }), (err) => console.warn('Focus session not saved:', err.message));
        return;
    }
    const log = getStorageData(FOCUS_LOG_KEY, {});
    const key = todayKey();
    const entry = log[key] || { sessions: 0, minutes: 0 };
    entry.sessions += 1;
    entry.minutes += minutes;
    log[key] = entry;
    setStorageData(FOCUS_LOG_KEY, log);
    notify();
}
