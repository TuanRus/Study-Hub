// VI: Quên mật khẩu (chỉ với tài khoản server): nhập email → server gửi mã 6 số → nhập mã + mật khẩu mới.
//     Server luôn trả lời giống nhau dù email có tồn tại hay không, nên ở đây cũng không báo "email không tồn tại".
// EN: Forgot password (server accounts only): enter email → the server mails a 6-digit code → enter code + new password.
//     The server answers the same whether or not the email exists, so this never says "email not found" either.

import { api, checkBackend, isBackendOnline } from '../utils/api.js';
import { showToast } from '../utils/toast.js';

const RESEND_SECONDS = 60;

export function initForgotPassword() {
    const authForm = document.getElementById('auth-form');
    const form = document.getElementById('forgot-form');
    const openBtn = document.getElementById('auth-forgot-btn');
    if (!authForm || !form || !openBtn) return;

    const emailInput = document.getElementById('forgot-email');
    const codeStep = form.querySelector('.forgot-step-code');
    const codeInput = document.getElementById('forgot-code');
    const newInput = document.getElementById('forgot-new');
    const confirmInput = document.getElementById('forgot-confirm');
    const hint = document.getElementById('forgot-hint');
    const message = document.getElementById('forgot-message');
    const submitBtn = document.getElementById('forgot-submit-btn');
    const resendBtn = document.getElementById('forgot-resend-btn');
    const tabs = document.querySelector('.auth-tabs');

    let codeSent = false;
    let resendTimer;

    const showError = (text) => {
        message.textContent = text;
        message.className = 'auth-message';
    };

    function setStep(sent) {
        codeSent = sent;
        codeStep.hidden = !sent;
        resendBtn.hidden = !sent;
        emailInput.readOnly = sent;
        submitBtn.textContent = sent ? 'Reset password' : 'Send code';
        hint.textContent = sent
            ? `If ${emailInput.value.trim()} belongs to an account, a code is on its way. Check spam too. It expires in 10 minutes.`
            : 'Enter your account email. We will send you a 6-digit code.';
    }

    function startResendCountdown() {
        clearInterval(resendTimer);
        let left = RESEND_SECONDS;
        resendBtn.disabled = true;
        const tick = () => {
            resendBtn.textContent = left > 0 ? `Resend code (${left}s)` : 'Resend code';
            resendBtn.disabled = left > 0;
            if (left-- <= 0) clearInterval(resendTimer);
        };
        tick();
        resendTimer = setInterval(tick, 1000);
    }

    function open() {
        form.reset();
        message.textContent = '';
        setStep(false);
        const typed = document.getElementById('auth-username').value.trim();
        if (typed.includes('@')) emailInput.value = typed;
        authForm.hidden = true;
        if (tabs) tabs.hidden = true;
        form.hidden = false;
        emailInput.focus();
    }

    function close() {
        clearInterval(resendTimer);
        form.hidden = true;
        authForm.hidden = false;
        if (tabs) tabs.hidden = false;
    }

    async function sendCode() {
        const email = emailInput.value.trim();
        if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) return showError('Enter a valid email address.');
        if (!isBackendOnline() && !(await checkBackend()))
            return showError('Password reset needs the Study Hub server, and it is offline right now.');

        submitBtn.disabled = true;
        message.textContent = '';
        try {
            await api('/auth/forgot-password', { method: 'POST', body: { email }, retry: false });
            setStep(true);
            startResendCountdown();
            codeInput.focus();
        } catch (err) {
            showError(err.status === 429 ? 'Too many attempts. Wait a minute and try again.' : err.message);
        } finally {
            submitBtn.disabled = false;
        }
    }

    async function resetPassword() {
        const code = codeInput.value.replace(/\s/g, '');
        const newPassword = newInput.value;
        if (!/^\d{6}$/.test(code)) return showError('The code has 6 digits.');
        if (newPassword.length < 8) return showError('New password must be at least 8 characters.');
        if (newPassword !== confirmInput.value) return showError('New passwords do not match.');

        submitBtn.disabled = true;
        message.textContent = '';
        try {
            await api('/auth/reset-password', {
                method: 'POST', body: { email: emailInput.value.trim(), code, newPassword }, retry: false,
            });
        } catch (err) {
            showError(err.status === 429 ? 'Too many attempts. Wait a minute and try again.' : err.message);
            submitBtn.disabled = false;
            return;
        }
        submitBtn.disabled = false;
        const email = emailInput.value.trim();
        close();
        // VI: Quay về form đăng nhập, điền sẵn email. EN: Back to the login form with the email filled in.
        document.getElementById('auth-username').value = email;
        document.getElementById('auth-password').value = '';
        document.getElementById('auth-password').focus();
        const authMessage = document.getElementById('auth-message');
        authMessage.textContent = 'Password changed. Log in with your new password.';
        authMessage.className = 'auth-message success';
        showToast('Password changed. Other devices were signed out.', 'success', 4000);
    }

    openBtn.addEventListener('click', open);
    document.getElementById('forgot-back-btn').addEventListener('click', close);
    resendBtn.addEventListener('click', async () => {
        setStep(false);
        await sendCode();
    });
    codeInput.addEventListener('input', () => {
        codeInput.value = codeInput.value.replace(/\D/g, '').slice(0, 6);
    });
    form.addEventListener('submit', (e) => {
        e.preventDefault();
        codeSent ? resetPassword() : sendCode();
    });
    // VI: Mở lại hộp đăng nhập hoặc đổi tab thì quay về form thường. EN: Reopening the dialog or switching tabs goes back to the normal form.
    document.querySelectorAll('.auth-tab').forEach(tab => tab.addEventListener('click', close));
}
