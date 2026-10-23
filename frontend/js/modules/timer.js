// VI: Module Countdown Timer, cô lập state đếm ngược và logic xử lý interval (SRP). 
// EN: Countdown Timer module, isolating countdown state and interval handling logic (SRP).

import { formatStopwatchTime } from '../utils/formatters.js';
import { playAlarm } from '../utils/sound.js';
import { showToast } from '../utils/toast.js';

export function initTimer() {
    const timerDisplay = document.getElementById('timer-display');
    const timerProgressBar = document.getElementById('timer-progress-bar');
    const timerHoursInput = document.getElementById('timer-hours');
    const timerMinutesInput = document.getElementById('timer-minutes');
    const timerSecondsInput = document.getElementById('timer-seconds');
    const timerStartBtn = document.getElementById('timer-start-pause');
    const timerResetBtn = document.getElementById('timer-reset');
    const presetButtons = document.querySelectorAll('#timer-presets .chip');

    // VI: Khởi tạo biến State cục bộ để tránh ô nhiễm Global Scope. 
    // EN: Initializing local State variables to avoid Global Scope pollution.
    let timerInterval, timerRunning = false, timerTimeLeft = 0, initialTimerTime = 0, endAt = 0;

    // VI: Helper function tính tổng số giây, áp dụng Guard clause để tránh lỗi runtime nếu thiếu node DOM. 
    // EN: Helper function computing total seconds, applying Guard clause to prevent runtime errors if DOM node is missing.
    function getTimerInputSeconds() {
        if (!timerHoursInput) return 0;
        const hours = parseInt(timerHoursInput.value) || 0;
        const minutes = parseInt(timerMinutesInput.value) || 0;
        const seconds = parseInt(timerSecondsInput.value) || 0;
        return (hours * 3600) + (minutes * 60) + seconds;
    }

    // VI: Đồng bộ State xuống UI (One-way Data Binding). 
    // EN: Syncing State to UI (One-way Data Binding).
    function updateTimerDisplay() {
        if (timerDisplay) timerDisplay.textContent = formatStopwatchTime(timerTimeLeft);
        if (timerProgressBar) {
            const elapsed = initialTimerTime > 0 ? 1 - timerTimeLeft / initialTimerTime : 0;
            timerProgressBar.style.width = `${Math.min(100, Math.max(0, elapsed * 100))}%`;
        }
    }

    function setStartIcon(isPlaying) {
        timerStartBtn.innerHTML = `<i class="ph-fill ${isPlaying ? 'ph-pause' : 'ph-play'}"></i>`;
    }

    function loadFromInputs() {
        timerTimeLeft = getTimerInputSeconds();
        initialTimerTime = timerTimeLeft;
        updateTimerDisplay();
    }

    if (timerStartBtn) {
        timerStartBtn.addEventListener('click', () => {
            // VI: State machine xử lý luân chuyển các trạng thái Play/Pause. 
            // EN: State machine handling transition between Play/Pause states.
            if (timerRunning) {
                clearInterval(timerInterval);
                timerRunning = false;
                setStartIcon(false);
            } else {
                if (timerTimeLeft === 0) loadFromInputs();
                if (timerTimeLeft > 0) {
                    timerRunning = true;
                    endAt = Date.now() + timerTimeLeft * 1000;
                    setStartIcon(true);

                    // VI: Tính theo mốc kết thúc để không bị lệch khi tab bị ẩn.
                    // EN: Derived from the end timestamp so it stays accurate in background tabs.
                    timerInterval = setInterval(() => {
                        const left = Math.max(0, Math.ceil((endAt - Date.now()) / 1000));
                        if (left === timerTimeLeft) return;
                        timerTimeLeft = left;
                        updateTimerDisplay();
                        if (timerTimeLeft <= 0) {
                            clearInterval(timerInterval);
                            timerRunning = false;
                            setStartIcon(false);
                            playAlarm(3);
                            showToast("The timer has ended!", 'success', 5000);
                        }
                    }, 250);
                } else {
                    showToast('Set a duration first.', 'error');
                }
            }
        });

        // VI: Reset state và dọn dẹp bộ nhớ (clear interval) để chặn memory leak. 
        // EN: Reset state and memory cleanup (clear interval) to prevent memory leaks.
        if (timerResetBtn) {
            timerResetBtn.addEventListener('click', () => {
                clearInterval(timerInterval);
                timerRunning = false;
                timerTimeLeft = initialTimerTime > 0 ? initialTimerTime : getTimerInputSeconds();
                updateTimerDisplay();
                setStartIcon(false);
            });
        }

        // VI: Delegate event listener cho mảng input để cập nhật State realtime (chỉ khi Timer đang Idle). 
        // EN: Delegate event listener for input array to update State in realtime (only when Timer is Idle).
        const timerInputs = [timerHoursInput, timerMinutesInput, timerSecondsInput];
        if (timerInputs.every(el => el)) {
            timerInputs.forEach(input => {
                input.addEventListener('change', () => {
                    if (!timerRunning) loadFromInputs();
                });
            });
        }

        // VI: Nút chọn nhanh thời lượng. EN: Quick duration presets.
        presetButtons.forEach(chip => {
            chip.addEventListener('click', () => {
                if (timerRunning) return showToast('Pause the timer before changing it.', 'info');
                const total = parseInt(chip.dataset.minutes) || 0;
                timerHoursInput.value = Math.floor(total / 60);
                timerMinutesInput.value = total % 60;
                timerSecondsInput.value = 0;
                presetButtons.forEach(c => c.classList.toggle('active', c === chip));
                loadFromInputs();
            });
        });

        loadFromInputs();
    }
}
