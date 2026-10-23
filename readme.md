# Study Hub

**[English](#english) · [Tiếng Việt](#tiếng-việt)**

<a id="english"></a>

## 🇬🇧 English

A study companion web app: daily overview, deadline calendar, Pomodoro timer and a floating music player. The frontend works on its own with `localStorage`; the optional ASP.NET Core API adds real accounts and server-side data.

### Features

- **Today**: clock, today's focus time and tasks due today
- **Deadlines**: month/week calendar with drag & drop and filters
- **Focus**: Pomodoro timer, stopwatch and countdown timer
- **Music**: play YouTube/Spotify links in a floating mini player
- **Profile**: bio-link card with a public `/@username` page
- **Accounts and admin page** (with the backend), light/dark theme, mobile layout

### Tech stack

- **Frontend:** HTML, CSS, vanilla JavaScript (ES modules, no build step)
- **Backend:** ASP.NET Core 10, EF Core + SQLite, JWT auth
- **Docs and tests:** OpenAPI + Scalar, xUnit

### Project structure

```text
├── frontend/            index.html, css/, js/ (modules, utils), assets/
├── backend/
│   ├── StudyHub.slnx
│   ├── src/StudyHub.Api/
│   └── tests/StudyHub.Api.Tests/
└── docs/                sample JS API client
```

### Run the frontend

ES modules need HTTP (`file://` does not work). Either:

- **VS Code Live Server:** click **Go Live** (`.vscode/settings.json` serves `frontend/`), or
- **Python:**

  ```bash
  cd frontend
  python -m http.server 5500
  ```

Then open <http://localhost:5500>. The API URL is set in `frontend/js/config.js`.

### Run the backend

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd backend
# one time: JWT signing key (kept outside the repo, at least 32 characters)
dotnet user-secrets --project src/StudyHub.Api set "Jwt:Key" "<random string, 32+ characters>"
dotnet run --project src/StudyHub.Api --launch-profile http
dotnet test
```

- No JWT key is stored in `appsettings*.json`. Without one the server stops at start-up and says how to set it.
- App (the backend also serves the frontend): <http://localhost:5080>
- API docs (Scalar, Development only): <http://localhost:5080/scalar>
- The SQLite database is created automatically on first run.
- To get an admin account, add `your-username` to `Admin:SuperAdmins` (user-secrets or env var `Admin__SuperAdmins__0`) and restart. If the account does not exist yet, the server creates it and prints a one-time password in the log; sign in and change it. That username cannot be taken through sign-up.

### API overview

Base path `/api`. Most endpoints need `Authorization: Bearer <accessToken>`; full details are in Scalar.

| Area | Routes |
|---|---|
| Auth | `/api/auth/register`, `login`, `refresh`, `logout`, `me` |
| Tasks | `/api/tasks`, `/api/tasks/calendar`, `/api/tasks/{id}` |
| Focus | `/api/pomodoro/settings`, `/api/focus-sessions`, `/api/stats/summary` |
| Music | `/api/music/resolve`, `/api/playlists` |
| Profiles | `/api/profile`, `/api/profiles/{username}` (public) |
| Admin | `/api/admin/stats`, `/api/admin/users` |
| Import | `/api/import/local` |

---

<a id="tiếng-việt"></a>

## 🇻🇳 Tiếng Việt

Web app hỗ trợ học tập: tổng quan trong ngày, lịch deadline, đồng hồ Pomodoro và trình phát nhạc nổi. Frontend tự chạy được với `localStorage`; API ASP.NET Core (tùy chọn) thêm tài khoản thật và lưu dữ liệu trên server.

### Tính năng

- **Today**: đồng hồ, thời gian tập trung và task đến hạn hôm nay
- **Deadlines**: lịch tháng/tuần, kéo thả, bộ lọc
- **Focus**: Pomodoro, bấm giờ và đếm ngược
- **Nhạc**: phát link YouTube/Spotify trong trình phát nổi
- **Hồ sơ**: thẻ bio-link với trang công khai `/@username`
- **Tài khoản và trang admin** (khi có backend), giao diện sáng/tối, bố cục điện thoại

### Công nghệ

- **Frontend:** HTML, CSS, JavaScript thuần (ES modules, không cần build)
- **Backend:** ASP.NET Core 10, EF Core + SQLite, xác thực JWT
- **Tài liệu và test:** OpenAPI + Scalar, xUnit

### Cấu trúc thư mục

```text
├── frontend/            index.html, css/, js/ (modules, utils), assets/
├── backend/
│   ├── StudyHub.slnx
│   ├── src/StudyHub.Api/
│   └── tests/StudyHub.Api.Tests/
└── docs/                client JS mẫu cho API
```

### Chạy frontend

ES modules cần chạy qua HTTP (mở bằng `file://` sẽ không chạy). Chọn một cách:

- **VS Code Live Server:** bấm **Go Live** (`.vscode/settings.json` đã trỏ vào `frontend/`), hoặc
- **Python:**

  ```bash
  cd frontend
  python -m http.server 5500
  ```

Rồi mở <http://localhost:5500>. Địa chỉ API đặt trong `frontend/js/config.js`.

### Chạy backend

Cần cài [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd backend
# một lần: khóa ký JWT (lưu ngoài repo, ít nhất 32 ký tự)
dotnet user-secrets --project src/StudyHub.Api set "Jwt:Key" "<chuỗi ngẫu nhiên, 32+ ký tự>"
dotnet run --project src/StudyHub.Api --launch-profile http
dotnet test
```

- Không có khóa JWT nào trong `appsettings*.json`. Thiếu khóa thì server dừng lúc khởi động và báo cách đặt.
- App (backend phục vụ luôn frontend): <http://localhost:5080>
- Tài liệu API (Scalar, chỉ ở Development): <http://localhost:5080/scalar>
- Database SQLite tự tạo ở lần chạy đầu.
- Muốn có tài khoản admin: thêm `your-username` vào `Admin:SuperAdmins` (user-secrets hoặc biến môi trường `Admin__SuperAdmins__0`) rồi khởi động lại. Nếu tài khoản chưa có, server tự tạo và in mật khẩu dùng một lần ra log; đăng nhập rồi đổi mật khẩu. Username đó không đăng ký công khai được.

### Tổng quan API

Đường dẫn gốc `/api`. Hầu hết endpoint cần header `Authorization: Bearer <accessToken>`; chi tiết xem trong Scalar.

| Nhóm | Route |
|---|---|
| Xác thực | `/api/auth/register`, `login`, `refresh`, `logout`, `me` |
| Task | `/api/tasks`, `/api/tasks/calendar`, `/api/tasks/{id}` |
| Tập trung | `/api/pomodoro/settings`, `/api/focus-sessions`, `/api/stats/summary` |
| Nhạc | `/api/music/resolve`, `/api/playlists` |
| Hồ sơ | `/api/profile`, `/api/profiles/{username}` (công khai) |
| Admin | `/api/admin/stats`, `/api/admin/users` |
| Nhập dữ liệu | `/api/import/local` |
