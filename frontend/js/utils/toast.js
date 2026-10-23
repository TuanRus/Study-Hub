// VI: Thông báo nhỏ (toast) thay cho alert(), tự ẩn sau vài giây.
// EN: Small toast notifications replacing alert(), auto-dismissed after a few seconds.

const ICONS = {
    info: 'ph-info',
    success: 'ph-check-circle',
    error: 'ph-warning-circle',
};

export function showToast(message, type = 'info', duration = 3000) {
    const root = document.getElementById('toast-root');
    if (!root) return;

    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.setAttribute('role', type === 'error' ? 'alert' : 'status');

    const icon = document.createElement('i');
    icon.className = `ph-fill ${ICONS[type] || ICONS.info}`;
    const text = document.createElement('span');
    text.textContent = message;
    toast.append(icon, text);
    root.appendChild(toast);

    setTimeout(() => {
        toast.classList.add('leaving');
        setTimeout(() => toast.remove(), 220);
    }, duration);
}
