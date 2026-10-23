// VI: Cấu hình kết nối backend. Sửa API_BASE_URL nếu backend chạy ở địa chỉ khác (vd. khi deploy).
//     Để trống = tự chọn: nếu trang do chính backend phục vụ (cổng 5080/7080) thì gọi cùng origin,
//     còn lại (Live Server :5500, python http.server...) gọi http://localhost:5080.
//     Có thể ghi đè tạm trong trình duyệt: localStorage.setItem('studyhub_api_base', 'http://192.168.1.5:5080')
// EN: Backend connection settings. Set API_BASE_URL when the backend runs elsewhere (e.g. once deployed).
//     Empty = automatic: same origin when the page is served by the backend itself (port 5080/7080),
//     otherwise (Live Server :5500, python http.server...) http://localhost:5080.
//     Per-browser override: localStorage.setItem('studyhub_api_base', 'http://192.168.1.5:5080')

export const API_BASE_URL = '';

const DEFAULT_BACKEND = 'http://localhost:5080';
const BACKEND_PORTS = ['5080', '7080'];

function resolveBaseUrl() {
    let override = '';
    try { override = localStorage.getItem('studyhub_api_base') || ''; } catch (e) { /* ignore */ }
    const base = override || API_BASE_URL ||
        (BACKEND_PORTS.includes(location.port) ? location.origin : DEFAULT_BACKEND);
    return base.replace(/\/+$/, '');
}

export const BACKEND_URL = resolveBaseUrl();

// VI: true khi trang được backend phục vụ, nên link /@username hoạt động. Live Server thì dùng ?u=username.
// EN: true when the backend serves this page, so /@username links work. On Live Server use ?u=username.
export const SERVED_BY_BACKEND = BACKEND_URL === location.origin;
