// VI: Module Stopwatch quản lý thời gian đếm tiến, cô lập logic xử lý interval và state. 
// EN: Stopwatch module managing count-up time, isolating interval logic and state.

import { formatStopwatchTime } from '../utils/formatters.js';

export function initStopwatch() {
    // VI: Khởi tạo DOM cache để tối ưu hiệu suất truy xuất phần tử.
    //  EN: Initializing DOM cache to optimize element retrieval performance.
    const stopwatchDisplay = document.getElementById('stopwatch-display');
    const stopwatchStartBtn = document.getElementById('stopwatch-start-pause');
    const stopwatchResetBtn = document.getElementById('stopwatch-reset');
    
    // VI: State cục bộ. Thời gian = phần đã cộng dồn + (now - mốc bắt đầu), không phụ thuộc độ chính xác của setInterval.
    // EN: Local state. Elapsed = accumulated + (now - start), independent of setInterval drift.
    let stopwatchInterval;
    let stopwatchRunning = false;
    let accumulatedMs = 0;
    let startedAt = 0;

    function render() {
        const elapsedMs = accumulatedMs + (stopwatchRunning ? Date.now() - startedAt : 0);
        if (stopwatchDisplay) stopwatchDisplay.textContent = formatStopwatchTime(Math.floor(elapsedMs / 1000));
    }

    // VI: Guard clause chặn binding nếu thiếu node điều khiển chính. 
    // EN: Guard clause preventing binding if the main control node is missing.
    if (stopwatchStartBtn) {
        stopwatchStartBtn.addEventListener('click', () => {
            // VI: Cỗ máy trạng thái (State Machine) điều khiển luồng Start/Pause. 
            // EN: State machine controlling the Start/Pause flow.
            if (stopwatchRunning) {
                clearInterval(stopwatchInterval);
                accumulatedMs += Date.now() - startedAt;
                stopwatchStartBtn.innerHTML = '<i class="ph-fill ph-play"></i>';
            } else {
                startedAt = Date.now();
                stopwatchInterval = setInterval(render, 250);
                stopwatchStartBtn.innerHTML = '<i class="ph-fill ph-pause"></i>';
            }
            // VI: Chuyển đổi trạng thái nhị phân. 
            // EN: Toggling binary state.
            stopwatchRunning = !stopwatchRunning;
            render();
        });
        
        // VI: Logic dọn dẹp và đưa State về giá trị mặc định (Idle state). 
        // EN: Cleanup logic and resetting State to default values (Idle state).
        if(stopwatchResetBtn) {
            stopwatchResetBtn.addEventListener('click', () => {
                clearInterval(stopwatchInterval);
                stopwatchRunning = false; 
                accumulatedMs = 0;
                render();
                stopwatchStartBtn.innerHTML = '<i class="ph-fill ph-play"></i>';
            });
        }
    }
}
