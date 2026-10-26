// VI: Nút "Report" trên trang hồ sơ công khai (/@username): người đã đăng nhập báo cáo tài khoản cho admin.
// EN: The "Report" button on a public profile (/@username): signed-in users report the account to the admins.

import { api, sessionUser } from '../utils/api.js';
import { showToast } from '../utils/toast.js';

export function initReport(slug) {
    const btn = document.getElementById('profile-report-btn');
    const modal = document.getElementById('report-modal');
    const form = document.getElementById('report-form');
    const message = document.getElementById('report-message');
    if (!btn || !modal || !form) return;

    // VI: Không hiện trên hồ sơ của chính mình. EN: Hidden on your own profile.
    const me = sessionUser();
    btn.hidden = !!me && me.username?.toLowerCase() === slug.toLowerCase();

    const close = () => {
        modal.classList.add('hidden');
        form.reset();
        message.textContent = '';
    };

    btn.addEventListener('click', () => {
        if (!sessionUser()) {
            showToast('Sign in to Study Hub to report a profile.', 'info', 4000);
            return;
        }
        document.getElementById('report-title').textContent = `Report @${slug}`;
        message.textContent = '';
        modal.classList.remove('hidden');
        document.getElementById('report-reason').focus();
    });
    modal.querySelectorAll('[data-close]').forEach((b) => b.addEventListener('click', close));
    modal.addEventListener('click', (e) => { if (e.target === modal) close(); });

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const submit = document.getElementById('report-submit');
        submit.disabled = true;
        message.textContent = '';
        try {
            await api(`/profiles/${encodeURIComponent(slug)}/report`, {
                method: 'POST',
                body: {
                    reason: document.getElementById('report-reason').value,
                    details: document.getElementById('report-details').value.trim() || null,
                },
            });
            close();
            btn.hidden = true;
            showToast('Thanks, the report was sent to the admins.', 'success', 3500);
        } catch (err) {
            message.textContent = err.message;
        } finally {
            submit.disabled = false;
        }
    });
}
