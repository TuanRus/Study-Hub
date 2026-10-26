// VI: Cỗ máy trạng thái (State Machine) quản lý phiên Pomodoro. Tách biệt Timer logic khỏi UI Event. 
// EN: State machine managing Pomodoro sessions. Isolates Timer logic from UI Events.

import { formatTime } from '../utils/formatters.js';
import { playAlarm, stopAlarm } from '../utils/sound.js';
import { logFocusSession } from '../utils/focusLog.js';
import { showToast } from '../utils/toast.js';
import { api, enqueue, isRemote } from '../utils/api.js';

const RING_CIRCUMFERENCE = 2 * Math.PI * 54;
const MODE_LABELS = { work: 'Focus', shortBreak: 'Short Break', longBreak: 'Long Break' };
const SESSIONS_PER_CYCLE = 4;

// VI: Cài đặt Pomodoro trên server (khi đăng nhập). Các trường FE chưa có UI được giữ nguyên khi lưu.
// EN: Server Pomodoro settings (when logged in). Fields without FE controls are kept as-is on save.
let remoteSettings = null;
let saveTimer;

export async function loadRemotePomodoroSettings() {
    remoteSettings = await api('/pomodoro/settings');
    const values = {
        'pomo-time-setting': remoteSettings.workMinutes,
        'short-break-setting': remoteSettings.shortBreakMinutes,
        'long-break-setting': remoteSettings.longBreakMinutes,
    };
    Object.entries(values).forEach(([id, value]) => {
        const input = document.getElementById(id);
        if (input && Number(input.value) !== value) {
            input.value = value;
            input.dispatchEvent(new Event('input'));
        }
    });
}

export function clearRemotePomodoroSettings() {
    remoteSettings = null;
}

function saveRemoteSettings() {
    if (!remoteSettings || !isRemote()) return;
    const read = (id, max) => {
        const value = Math.round(parseFloat(document.getElementById(id)?.value));
        return value >= 1 && value <= max ? value : null;
    };
    const work = read('pomo-time-setting', 180);
    const shortBreak = read('short-break-setting', 60);
    const longBreak = read('long-break-setting', 90);
    if (work === null || shortBreak === null || longBreak === null) return;
    const body = { ...remoteSettings, workMinutes: work, shortBreakMinutes: shortBreak, longBreakMinutes: longBreak };
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => enqueue(async () => {
        remoteSettings = await api('/pomodoro/settings', { method: 'PUT', body });
    }, (err) => showToast(`Settings not saved: ${err.message}`, 'error')), 600);
}

export function initPomodoro() {
    // VI: Khởi tạo DOM cache để tránh query lại nhiều lần. 
    // EN: Initializing DOM cache to prevent redundant queries.
    const pomoCard = document.getElementById('pomo-card');
    const pomoStatusEl = document.getElementById('pomo-status');
    const pomoTimeEl = document.getElementById('pomo-time');
    const ringEl = document.getElementById('pomo-ring-progress');
    const pomoStartBtn = document.getElementById('pomo-start-pause');
    const pomoResetBtn = document.getElementById('pomo-reset');
    const timeWorkInput = document.getElementById('pomo-time-setting');
    const timeShortBreakInput = document.getElementById('short-break-setting');
    const timeLongBreakInput = document.getElementById('long-break-setting');
    const statWorkCount = document.querySelector('#stat-work .count');
    const statShortCount = document.querySelector('#stat-short .count');
    const statLongCount = document.querySelector('#stat-long .count');
    const statItems = document.querySelectorAll('.stat-item');
    const hintEl = document.getElementById('pomo-hint-mode');
    const roundEl = document.getElementById('pomo-round');
    const learnBtn = document.getElementById('learn-pomo-btn');
    const closeBtn = document.getElementById('close-ex-btn');
    const explanationModal = document.getElementById('pomo-explanation');

    let stats = { work: 0, shortBreak: 0, longBreak: 0 };
    let pomoInterval;
    let transitionTimeout;
    let pomoIsRunning = false;
    let currentMode = 'work';
    let sessionCount = 0;

    // VI: Tính thời gian theo mốc kết thúc (Date.now) để không bị lệch khi tab bị ẩn.
    // EN: Time is derived from an end timestamp (Date.now) so it stays accurate in background tabs.
    let endAt = 0;
    let pomoTimeLeft = getModeMinutes('work') * 60;
    let phaseTotal = pomoTimeLeft;

    function getModeMinutes(mode) {
        const input = mode === 'work' ? timeWorkInput : (mode === 'shortBreak' ? timeShortBreakInput : timeLongBreakInput);
        const fallback = mode === 'work' ? 25 : (mode === 'shortBreak' ? 5 : 15);
        const value = input ? parseFloat(input.value) : NaN;
        return value > 0 ? value : fallback;
    }

    function setPhase(mode) {
        currentMode = mode;
        pomoTimeLeft = Math.round(getModeMinutes(mode) * 60);
        phaseTotal = pomoTimeLeft;
        if (pomoStatusEl) pomoStatusEl.textContent = MODE_LABELS[mode];
    }

    // VI: Hàm đồng bộ View với State hiện tại. 
    // EN: Function syncing View with the current State.
    function updateUIStats() {
        if (statWorkCount) statWorkCount.textContent = stats.work;
        if (statShortCount) statShortCount.textContent = stats.shortBreak;
        if (statLongCount) statLongCount.textContent = stats.longBreak;

        statItems.forEach(item => item.classList.remove('active'));
        const currentId = currentMode === 'work' ? 'stat-work' :
            (currentMode === 'shortBreak' ? 'stat-short' : 'stat-long');
        const activeItem = document.getElementById(currentId);
        if (activeItem) activeItem.classList.add('active');

        if (pomoCard) pomoCard.dataset.mode = currentMode;
        if (hintEl) hintEl.textContent = MODE_LABELS[currentMode];
        if (roundEl) {
            const round = currentMode === 'work'
                ? (sessionCount % SESSIONS_PER_CYCLE) + 1
                : ((sessionCount - 1 + SESSIONS_PER_CYCLE) % SESSIONS_PER_CYCLE) + 1;
            roundEl.textContent = round;
        }
    }

    function updatePomoDisplay() {
        if (pomoTimeEl) pomoTimeEl.textContent = formatTime(pomoTimeLeft);
        if (ringEl) {
            const progress = phaseTotal > 0 ? pomoTimeLeft / phaseTotal : 0;
            ringEl.style.strokeDashoffset = RING_CIRCUMFERENCE * (1 - progress);
        }
        document.title = pomoIsRunning ? `${formatTime(pomoTimeLeft)} · ${MODE_LABELS[currentMode]} | Study Hub` : 'Study Hub';
    }

    function setStartIcon(isPlaying) {
        if (pomoStartBtn) pomoStartBtn.innerHTML = `<i class="ph-fill ${isPlaying ? 'ph-pause' : 'ph-play'}"></i>`;
        if (pomoCard) pomoCard.classList.toggle('is-running', isPlaying);
    }

    function stopPomoTimer() {
        clearInterval(pomoInterval);
        clearTimeout(transitionTimeout);
        pomoIsRunning = false;
        stopAlarm();
        setStartIcon(false);
        updatePomoDisplay();
    }

    function startPomoTimer() {
        if (pomoTimeLeft <= 0) setPhase(currentMode);
        pomoIsRunning = true;
        endAt = Date.now() + pomoTimeLeft * 1000;
        setStartIcon(true);
        updatePomoDisplay();

        clearInterval(pomoInterval);
        pomoInterval = setInterval(() => {
            const left = Math.max(0, Math.ceil((endAt - Date.now()) / 1000));
            if (left !== pomoTimeLeft) {
                pomoTimeLeft = left;
                updatePomoDisplay();
            }
            if (left === 0) {
                clearInterval(pomoInterval);
                onPhaseComplete();
            }
        }, 250);
    }

    function onPhaseComplete() {
        playAlarm(3);
        if (pomoStatusEl) pomoStatusEl.textContent = 'Time is up!';

        if (currentMode === 'work') {
            logFocusSession(Math.round(phaseTotal / 60), phaseTotal);
            showToast('Focus session complete. Time for a break!', 'success');
        } else {
            showToast('Break is over. Back to focus!', 'info');
        }

        // VI: Chuyển đổi trạng thái tự động sau khoảng trễ (5s buffer). 
        // EN: Automatic state transition after a delay buffer (5s).
        transitionTimeout = setTimeout(() => {
            if (pomoIsRunning) {
                switchMode();
                startPomoTimer();
            }
        }, 5000);
    }

    // VI: State handler xử lý logic luân chuyển các phase của Pomodoro. 
    // EN: State handler managing transition logic for Pomodoro phases.
    function switchMode() {
        stats[currentMode]++;

        if (currentMode === 'work') {
            sessionCount++;
            setPhase(sessionCount % SESSIONS_PER_CYCLE === 0 ? 'longBreak' : 'shortBreak');
        } else {
            setPhase('work');
        }
        updateUIStats();
        updatePomoDisplay();
    }

    function resetPomoTimer() {
        stopPomoTimer();
        sessionCount = 0;
        setPhase('work');
        updateUIStats();
        updatePomoDisplay();
    }

    if (pomoStartBtn) {
        pomoStartBtn.addEventListener('click', () => {
            pomoIsRunning ? stopPomoTimer() : startPomoTimer();
        });

        if (pomoResetBtn) pomoResetBtn.addEventListener('click', resetPomoTimer);

        // VI: Delegate event listener cho mảng input để cập nhật cài đặt realtime. 
        // EN: Delegate event listener array for realtime settings update.
        const inputsToWatch = [
            [timeWorkInput, 'work'],
            [timeShortBreakInput, 'shortBreak'],
            [timeLongBreakInput, 'longBreak'],
        ];
        inputsToWatch.forEach(([input, mode]) => {
            if (input) {
                input.addEventListener('change', saveRemoteSettings);
                input.addEventListener('input', () => {
                    if (!pomoIsRunning && currentMode === mode) {
                        setPhase(mode);
                        updatePomoDisplay();
                    }
                });
            }
        });
        updateUIStats();
        updatePomoDisplay();
    }

    if (learnBtn && explanationModal) {
        learnBtn.addEventListener('click', () => {
            explanationModal.classList.remove('hidden');
        });
    }

    if (closeBtn && explanationModal) {
        closeBtn.addEventListener('click', () => {
            explanationModal.classList.add('hidden');
        });
    }
}
