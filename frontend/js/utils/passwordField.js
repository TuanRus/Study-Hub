// VI: Ô mật khẩu: nút con mắt để ẩn/hiện, và danh sách quy tắc mật khẩu mạnh cập nhật khi gõ.
//     Độ dài tối thiểu lấy từ server (Auth:PasswordMinLength), server tắt thì dùng 8.
// EN: Password fields: an eye button to show/hide, and a strong-password checklist that updates while typing.
//     The minimum length comes from the server (Auth:PasswordMinLength), 8 when the server is down.

import { BACKEND_URL } from '../config.js';

const DEFAULT_MIN_LENGTH = 8;
const MAX_LENGTH = 100;

let minLength = DEFAULT_MIN_LENGTH;
let policyRequest = null;
const listeners = new Set();

// VI: Cùng quy tắc với PasswordPolicy ở backend. EN: Same rules as PasswordPolicy on the backend.
function rules() {
    return [
        { label: `At least ${minLength} characters`, test: (pw) => pw.length >= minLength },
        { label: 'An uppercase letter (A-Z)', test: (pw) => /\p{Lu}/u.test(pw) },
        { label: 'A lowercase letter (a-z)', test: (pw) => /\p{Ll}/u.test(pw) },
        { label: 'A digit (0-9)', test: (pw) => /\p{Nd}/u.test(pw) },
        { label: 'A special character (!@#$%...)', test: (pw) => /[^\p{L}\p{Nd}\s]/u.test(pw) },
    ];
}

export function loadPasswordPolicy() {
    policyRequest ??= fetch(`${BACKEND_URL}/api/auth/password-policy`, { cache: 'no-store' })
        .then((res) => (res.ok ? res.json() : null))
        .then((policy) => {
            if (policy?.minLength > 0) {
                minLength = Math.min(policy.minLength, MAX_LENGTH);
                listeners.forEach((fn) => fn());
            }
        })
        .catch(() => { policyRequest = null; });
    return policyRequest;
}

// VI: Câu báo lỗi cho quy tắc đầu tiên chưa đạt, null nếu mật khẩu đủ mạnh.
// EN: Error text for the first rule not met, null when the password is strong enough.
export function passwordProblem(password) {
    const failed = rules().find((rule) => !rule.test(password));
    return failed ? `Password needs: ${failed.label.toLowerCase()}.` : null;
}

// VI: Thêm nút con mắt cho mọi ô mật khẩu trong root. EN: Adds an eye button to every password input under root.
export function addPasswordToggles(root = document) {
    root.querySelectorAll('input[type="password"]').forEach((input) => {
        if (input.closest('.password-field')) return;
        const wrap = document.createElement('div');
        wrap.className = 'password-field';
        input.replaceWith(wrap);
        wrap.appendChild(input);

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'password-toggle';
        btn.innerHTML = '<i class="ph ph-eye"></i>';
        const sync = () => {
            const shown = input.type === 'text';
            btn.querySelector('i').className = shown ? 'ph ph-eye-slash' : 'ph ph-eye';
            btn.title = shown ? 'Hide password' : 'Show password';
            btn.setAttribute('aria-label', btn.title);
            btn.setAttribute('aria-pressed', String(shown));
        };
        btn.addEventListener('click', () => {
            input.type = input.type === 'password' ? 'text' : 'password';
            sync();
            input.focus();
        });
        // VI: Reset form thì ẩn lại mật khẩu. EN: Resetting the form hides the password again.
        input.form?.addEventListener('reset', () => { input.type = 'password'; sync(); });
        sync();
        wrap.appendChild(btn);
    });
}

// VI: Gắn checklist trực tiếp vào ô mật khẩu mới; list là phần tử <ul> có sẵn trong HTML.
// EN: Attaches a live checklist to a new-password input; list is a <ul> already in the HTML.
//     Pass { placeholder: false } for an input that is also used to log in (its placeholder stays).
export function attachPasswordChecklist(input, list, { placeholder = true } = {}) {
    if (!input || !list) return;
    const render = () => {
        const value = input.value;
        list.replaceChildren(...rules().map((rule) => {
            const ok = rule.test(value);
            const li = document.createElement('li');
            li.className = ok ? 'ok' : '';
            li.innerHTML = `<i class="ph${ok ? '-fill ph-check-circle' : ' ph-circle'}"></i>`;
            li.append(rule.label);
            return li;
        }));
        if (placeholder) input.placeholder = `At least ${minLength} characters`;
    };
    input.addEventListener('input', render);
    input.form?.addEventListener('reset', () => setTimeout(render));
    listeners.add(render);
    render();
    loadPasswordPolicy();
}
