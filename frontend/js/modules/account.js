// VI: Đổi mật khẩu cho tài khoản server (mở từ hộp "Edit profile").
//     Server đăng xuất mọi phiên sau khi đổi, nên ở đây đăng nhập lại ngay bằng mật khẩu mới để giữ phiên hiện tại.
// EN: Change the password of a server account (opened from the "Edit profile" dialog).
//     The server signs out every session after the change, so this signs in again with the new password to keep this one.

import { authApi, api, isRemote, sessionUser } from '../utils/api.js';
import { showToast } from '../utils/toast.js';

export function initAccount() {
    const modal = document.getElementById('password-modal');
    const form = document.getElementById('password-form');
    const openBtn = document.getElementById('profile-password-btn');
    const message = document.getElementById('pw-message');
    if (!modal || !form || !openBtn) return;

    const close = () => {
        modal.classList.add('hidden');
        form.reset();
        message.textContent = '';
    };

    // VI: Nút chỉ hiện khi đang dùng tài khoản server. EN: The button only shows for a server account.
    document.getElementById('profile-edit-btn')?.addEventListener('click', () => {
        openBtn.hidden = !isRemote();
    });

    openBtn.addEventListener('click', () => {
        document.getElementById('profile-modal')?.classList.add('hidden');
        message.textContent = '';
        modal.classList.remove('hidden');
        document.getElementById('pw-current').focus();
    });
    document.getElementById('password-close-btn').addEventListener('click', close);
    document.getElementById('password-cancel-btn').addEventListener('click', close);

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const currentPassword = document.getElementById('pw-current').value;
        const newPassword = document.getElementById('pw-new').value;
        const confirmPassword = document.getElementById('pw-confirm').value;

        if (newPassword.length < 8) return (message.textContent = 'New password must be at least 8 characters.');
        if (newPassword !== confirmPassword) return (message.textContent = 'New passwords do not match.');
        if (newPassword === currentPassword) return (message.textContent = 'New password must be different from the current one.');

        const user = sessionUser();
        if (!isRemote() || !user) return (message.textContent = 'Log in to your server account first.');

        const submitBtn = form.querySelector('[type="submit"]');
        submitBtn.disabled = true;
        message.textContent = '';
        try {
            await api('/auth/change-password', { method: 'POST', body: { currentPassword, newPassword } });
        } catch (err) {
            submitBtn.disabled = false;
            message.textContent = err.status === 400 && /current password/i.test(err.message)
                ? 'Current password is incorrect.'
                : err.message;
            return;
        }
        try {
            await authApi.login(user.username, newPassword);
            showToast('Password changed. Other devices were signed out.', 'success', 4000);
        } catch (err) {
            showToast('Password changed. Please log in again with the new password.', 'info', 5000);
        }
        submitBtn.disabled = false;
        close();
    });
}
