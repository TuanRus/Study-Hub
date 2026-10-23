// VI: Chuông báo dùng chung cho Pomodoro và Timer.
// EN: Shared alarm sound for Pomodoro and Timer.

const alarmSound = new Audio('assets/alarm.mp3');
alarmSound.preload = 'auto';
let stopTimeout;

export function playAlarm(seconds = 3) {
    clearTimeout(stopTimeout);
    alarmSound.currentTime = 0;
    alarmSound.play().catch(() => console.log('Audio blocked'));
    stopTimeout = setTimeout(stopAlarm, seconds * 1000);
}

export function stopAlarm() {
    clearTimeout(stopTimeout);
    alarmSound.pause();
    alarmSound.currentTime = 0;
}
