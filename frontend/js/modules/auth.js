// VI: Module xử lý định danh, cô lập logic authentication khỏi luồng thực thi chính (SRP). 
// EN: Identity handling module, isolating authentication logic from the main execution thread (SRP).

// VI: Backend chạy: đăng nhập/đăng ký bằng tài khoản thật (JWT). Backend tắt: dùng tài khoản thử lưu trên máy như trước.
// EN: Backend up: real accounts (JWT). Backend down: falls back to the old on-device demo accounts.

import { showToast } from '../utils/toast.js';
import { authApi, checkBackend, getSession, isBackendOnline, sessionUser, setSessionExpiredHandler } from '../utils/api.js';
import { startSync, stopSync } from './sync.js';

export function initAuth() {
    const authBtn = document.getElementById('auth-btn');
    const authBtnText = document.getElementById('auth-btn-text');
    const userGreeting = document.getElementById('user-greeting');
    const authModal = document.getElementById('auth-modal');
    const closeAuthBtn = document.getElementById('close-auth-btn');
    const authTabs = document.querySelectorAll('.auth-tab');
    const authForm = document.getElementById('auth-form');
    const authUsername = document.getElementById('auth-username');
    const authPassword = document.getElementById('auth-password');
    const authSubmitBtn = document.getElementById('auth-submit-btn');
    const authMessage = document.getElementById('auth-message');
    const authEmail = document.getElementById('auth-email');
    const authConfirmPassword = document.getElementById('auth-confirm-password');
    const registerOnlyFields = document.querySelectorAll('.register-only');

    // VI: Biến trạng thái quản lý mode hiện tại (login/register). 
    // EN: State variable managing the current mode (login/register).
    let currentAuthMode = 'login'; 

    function checkLoginStatus() {
        const serverUser = sessionUser();
        const currentUser = serverUser ? (serverUser.displayName || serverUser.username) : localStorage.getItem('studyhub_currentUser');
        if (currentUser) {
            if(userGreeting) {
                userGreeting.textContent = `Hello, ${currentUser}`;
                userGreeting.title = serverUser ? `Signed in as @${serverUser.username}` : 'Offline account (this device only)';
                userGreeting.hidden = false;
            }
            if(authBtnText) authBtnText.textContent = 'Logout';
            if(authBtn) {
                authBtn.querySelector('i').className = 'ph ph-sign-out';
                authBtn.classList.remove('btn-primary');
            }
        } else {
            if(userGreeting) userGreeting.hidden = true;
            if(authBtnText) authBtnText.textContent = 'Login';
            if(authBtn) {
                authBtn.querySelector('i').className = 'ph ph-sign-in';
                authBtn.classList.add('btn-primary');
            }
        }
        // VI: Báo cho các view khác (lời chào ở trang Today). EN: Notify other views (Today greeting).
        window.dispatchEvent(new CustomEvent('studyhub:auth'));
    }

    // VI: Guard clause chặn binding nếu thiếu DOM node. 
    // EN: Guard clause preventing binding if DOM node is missing.
    if(authBtn) {
        authBtn.addEventListener('click', async () => {
            const currentUser = getSession() || localStorage.getItem('studyhub_currentUser');
            if (currentUser) {
                if (confirm('Are you sure you want to logout?')) {
                    localStorage.removeItem('studyhub_currentUser');
                    if (getSession()) {
                        await authApi.logout();
                        stopSync();
                    }
                    checkLoginStatus();
                    showToast('Logged out.', 'info');
                }
            } else {
                authModal.classList.add('active');
                authMessage.textContent = '';
                authForm.reset();
                document.querySelector('.auth-tab[data-action="login"]').click();
                authUsername.focus();
            }
        });
    }

    if(closeAuthBtn) {
        closeAuthBtn.addEventListener('click', () => authModal.classList.remove('active'));
    }

    // VI: Bấm ra ngoài hộp thoại để đóng. EN: Click the backdrop to close.
    if(authModal) {
        authModal.addEventListener('click', (e) => {
            if (e.target === authModal) authModal.classList.remove('active');
        });
    }

    // VI: Chuyển đổi linh hoạt View và Validation rules dựa trên State. 
    // EN: Dynamically switching View and Validation rules based on State.
    authTabs.forEach(tab => {
        tab.addEventListener('click', (e) => {
            authTabs.forEach(t => t.classList.remove('active'));
            tab.classList.add('active');
            currentAuthMode = tab.getAttribute('data-action');
            
            authMessage.textContent = '';
            authMessage.className = 'auth-message';

            if (currentAuthMode === 'register') {
                authUsername.placeholder = 'Choose a username...';
                authSubmitBtn.textContent = 'Sign Up';
                registerOnlyFields.forEach(field => field.style.display = 'flex');
                document.querySelectorAll('.login-only').forEach(el => el.hidden = true);
                authEmail.setAttribute('required', 'true');
                authConfirmPassword.setAttribute('required', 'true');
            } else {
                authUsername.placeholder = 'Username or email...';
                authSubmitBtn.textContent = 'Login';
                registerOnlyFields.forEach(field => field.style.display = 'none');
                document.querySelectorAll('.login-only').forEach(el => el.hidden = false);
                authEmail.removeAttribute('required');
                authConfirmPassword.removeAttribute('required');
            }
        });
    });

    function showError(message) {
        authMessage.textContent = message;
        authMessage.className = 'auth-message';
    }

    // VI: Đăng nhập / đăng ký với server. EN: Sign in / sign up against the server.
    async function submitToServer(username, password) {
        authSubmitBtn.disabled = true;
        try {
            let user;
            if (currentAuthMode === 'register') {
                if (password !== authConfirmPassword.value.trim()) return showError('Passwords do not match!');
                if (password.length < 8) return showError('Password must be at least 8 characters.');
                user = await authApi.register(username, authEmail.value.trim(), password);
            } else {
                user = await authApi.login(username, password);
            }
            authModal.classList.remove('active');
            checkLoginStatus();
            showToast(`${currentAuthMode === 'register' ? 'Welcome' : 'Welcome back'}, ${user.displayName || user.username}!`, 'success');
            await startSync();
        } catch (err) {
            showError(err.status === 429 ? 'Too many attempts. Wait a minute and try again.' : err.message);
        } finally {
            authSubmitBtn.disabled = false;
        }
    }

    if(authForm) {
        authForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const username = authUsername.value.trim();
            const password = authPassword.value.trim();

            if (isBackendOnline() || await checkBackend()) {
                await submitToServer(username, password);
                return;
            }

            // VI: Không có server: tài khoản thử trên máy (mật khẩu không được bảo vệ, chỉ để demo).
            // EN: No server: on-device demo account (the password is not protected, demo only).
            showToast('Server offline: using an account saved on this device only.', 'info', 4000);
            let users = JSON.parse(localStorage.getItem('studyhub_users')) || {};

            if (currentAuthMode === 'register') {
                const email = authEmail.value.trim();
                const confirmPassword = authConfirmPassword.value.trim();

                // VI: Early return để chặn flow nếu validation thất bại. 
                // EN: Early return to block flow if validation fails.
                if (password !== confirmPassword) {
                    authMessage.textContent = "Passwords do not match!";
                    return;
                }

                // VI: Không trùng username (không phân biệt hoa thường) và không trùng email.
                // EN: No duplicate username (case-insensitive) or email.
                const taken = Object.entries(users);
                if (taken.some(([name]) => name.toLowerCase() === username.toLowerCase())) {
                    authMessage.textContent = "Username already exists!";
                } else if (email && taken.some(([, u]) => typeof u === 'object' && u.email?.toLowerCase() === email.toLowerCase())) {
                    authMessage.textContent = "Email is already registered!";
                } else {
                    users[username] = { password: password, email: email };
                    localStorage.setItem('studyhub_users', JSON.stringify(users));
                    authMessage.textContent = "Registration successful! Logging in...";
                    authMessage.className = 'auth-message success';
                    
                    setTimeout(() => {
                        localStorage.setItem('studyhub_currentUser', username);
                        authModal.classList.remove('active');
                        checkLoginStatus();
                        showToast(`Welcome, ${username}!`, 'success');
                    }, 1000);
                }
            } else {
                const storedUser = users[username];
                let isPasswordCorrect = false;

                // VI: Xử lý tương thích ngược cho cấu trúc data cũ (string vs object). EN: Backward compatibility handling for old data structures (string vs object).
                if (storedUser) {
                    if (typeof storedUser === 'string') isPasswordCorrect = (storedUser === password);
                    else if (storedUser.password === password) isPasswordCorrect = true;
                }

                if (isPasswordCorrect) {
                    localStorage.setItem('studyhub_currentUser', username);
                    authModal.classList.remove('active');
                    checkLoginStatus();
                    showToast(`Welcome back, ${username}!`, 'success');
                } else {
                    authMessage.textContent = "Invalid username or password!";
                }
            }
        });
    }

    // VI: Refresh token hết hạn hoặc bị thu hồi. EN: The refresh token expired or was revoked.
    setSessionExpiredHandler(() => {
        stopSync();
        checkLoginStatus();
        showToast('Your session expired. Please log in again.', 'info', 5000);
    });

    checkLoginStatus();
}