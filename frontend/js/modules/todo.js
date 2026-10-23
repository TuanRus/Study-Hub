// [EN] To-do data layer, shared by the Deadlines view, the Focus view and the Today dashboard.
//      Logged in + backend up: tasks live on the server (/api/tasks), cached in memory. Otherwise: localStorage.
// [VN] Tầng dữ liệu To-do, dùng chung cho trang Deadlines, trang Focus và trang Today.
//      Đăng nhập + backend chạy: task nằm trên server (/api/tasks), giữ bản sao trong bộ nhớ. Ngược lại: localStorage.

import { getStorageData, setStorageData } from '../utils/storage.js';
import { todayKey } from '../utils/focusLog.js';
import { api, enqueue, timeZone } from '../utils/api.js';
import { showToast } from '../utils/toast.js';

export const TODOS_STORAGE_KEY = 'studyhub_todos';

const PRIORITY_ORDER = { high: 0, medium: 1, low: 2 };
const DAY_MS = 24 * 60 * 60 * 1000;

// [EN] Server tasks in the FE shape, or null when using localStorage
// [VN] Task trên server theo dạng của FE, hoặc null khi dùng localStorage
let remoteTodos = null;
// [EN] Temporary id -> server id, for tasks created before the server answered
// [VN] Id tạm -> id trên server, cho task tạo xong nhưng server chưa trả lời
const serverIds = new Map();

// [EN] Older saved tasks only have { id, text, completed }; fill in the new fields
// [VN] Task cũ chỉ có { id, text, completed }; bổ sung các trường mới
function normalize(todo) {
    return {
        deadline: null,
        time: '',
        priority: 'medium',
        ...todo,
    };
}

function notify() {
    // [EN] Let every view that shows tasks re-render
    // [VN] Báo cho mọi view hiển thị task render lại
    window.dispatchEvent(new CustomEvent('studyhub:todos'));
}

const sameId = (a, b) => String(a) === String(b);

// ---------- Server <-> FE shape ----------
function pad(n) {
    return String(n).padStart(2, '0');
}

// [EN] All-day deadlines come back as 00:00Z of the date; timed ones are converted to local time
// [VN] Deadline cả ngày trả về là 00:00Z của ngày đó; deadline có giờ được đổi sang giờ địa phương
function fromTask(task) {
    let deadline = null;
    let time = '';
    if (task.endAt) {
        if (task.isAllDay) {
            deadline = task.endAt.slice(0, 10);
        } else {
            const end = new Date(task.endAt);
            deadline = todayKey(end);
            time = `${pad(end.getHours())}:${pad(end.getMinutes())}`;
        }
    }
    return {
        id: task.id,
        text: task.title,
        completed: task.isCompleted,
        deadline,
        time,
        priority: String(task.priority || 'Medium').toLowerCase(),
        // [EN] Fields the FE doesn't edit, sent back unchanged
        // [VN] Các trường FE không sửa, gửi lại nguyên vẹn
        server: { description: task.description, color: task.color, startAt: task.startAt, endAt: task.endAt },
    };
}

function toTaskRequest(todo) {
    let endAt = null;
    let isAllDay = false;
    if (todo.deadline) {
        if (todo.time) {
            const [y, m, d] = todo.deadline.split('-').map(Number);
            const [h, min] = todo.time.split(':').map(Number);
            endAt = new Date(y, m - 1, d, h, min).toISOString();
        } else {
            endAt = `${todo.deadline}T00:00:00Z`;
            isAllDay = true;
        }
    }
    const server = todo.server || {};
    // [EN] Keep a calendar block's start only while its end is unchanged
    // [VN] Chỉ giữ giờ bắt đầu của khối lịch khi giờ kết thúc không đổi
    const sameEnd = server.endAt && endAt && new Date(server.endAt).getTime() === new Date(endAt).getTime();
    return {
        title: todo.text,
        description: server.description ?? null,
        startAt: sameEnd ? server.startAt ?? null : null,
        endAt,
        isAllDay,
        color: server.color ?? null,
        priority: todo.priority.charAt(0).toUpperCase() + todo.priority.slice(1),
        isCompleted: !!todo.completed,
    };
}

function onSyncError(err) {
    showToast(err.offline ? 'Lost connection to the server. Your last change was not saved.' : err.message, 'error', 5000);
    loadRemoteTodos().catch(() => {});
}

export async function loadRemoteTodos() {
    const tasks = await api(`/tasks?timeZone=${encodeURIComponent(timeZone)}`);
    remoteTodos = tasks.map(fromTask);
    notify();
}

export function clearRemoteTodos() {
    remoteTodos = null;
    serverIds.clear();
    notify();
}

// ---------- Public API (same for both sources) ----------
export function getTodos() {
    if (remoteTodos) return remoteTodos.map(todo => ({ ...todo }));
    return getStorageData(TODOS_STORAGE_KEY, []).map(normalize);
}

export function saveTodos(todos) {
    setStorageData(TODOS_STORAGE_KEY, todos);
    notify();
}

export function addTodo({ text, deadline = null, time = '', priority = 'medium', completed = false }) {
    const todo = { id: Date.now(), text, completed, deadline: deadline || null, time: time || '', priority };
    if (!remoteTodos) {
        const todos = getTodos();
        todos.push(todo);
        saveTodos(todos);
        return;
    }
    const tempId = `tmp-${todo.id}`;
    remoteTodos.push({ ...todo, id: tempId, server: {} });
    notify();
    enqueue(async () => {
        const created = fromTask(await api('/tasks', { method: 'POST', body: toTaskRequest(todo) }));
        serverIds.set(tempId, created.id);
        const local = remoteTodos?.find(item => sameId(item.id, tempId));
        if (local) Object.assign(local, { id: created.id, server: created.server });
        notify();
    }, onSyncError);
}

export function updateTodo(id, changes) {
    if (!remoteTodos) {
        const todos = getTodos();
        const todo = todos.find(item => sameId(item.id, id));
        if (!todo) return;
        Object.assign(todo, changes);
        saveTodos(todos);
        return;
    }
    const todo = remoteTodos.find(item => sameId(item.id, id));
    if (!todo) return;
    Object.assign(todo, changes);
    notify();
    const snapshot = { ...todo };
    enqueue(async () => {
        const serverId = serverIds.get(String(id)) || snapshot.id;
        const saved = fromTask(await api(`/tasks/${serverId}`, { method: 'PUT', body: toTaskRequest(snapshot) }));
        const current = remoteTodos?.find(item => sameId(item.id, saved.id));
        if (current) current.server = saved.server;
    }, onSyncError);
}

export function toggleTodoStatus(id) {
    const todo = getTodos().find(item => sameId(item.id, id));
    if (todo) updateTodo(id, { completed: !todo.completed });
}

export function deleteTodo(id) {
    if (!confirm("Are you sure you want to delete this task?")) return false;
    if (!remoteTodos) {
        saveTodos(getTodos().filter(todo => !sameId(todo.id, id)));
        return true;
    }
    remoteTodos = remoteTodos.filter(todo => !sameId(todo.id, id));
    notify();
    enqueue(async () => {
        const serverId = serverIds.get(String(id)) || id;
        await api(`/tasks/${serverId}`, { method: 'DELETE' });
    }, onSyncError);
    return true;
}

function parseDateKey(key) {
    const [y, m, d] = key.split('-').map(Number);
    return new Date(y, m - 1, d);
}

// [EN] Describe how close a task is to its deadline
// [VN] Mô tả task còn cách hạn chót bao xa
export function getDueInfo(todo) {
    if (!todo.deadline) return { state: 'none', label: 'No deadline' };

    const today = parseDateKey(todayKey());
    const due = parseDateKey(todo.deadline);
    const days = Math.round((due - today) / DAY_MS);
    const timeLabel = todo.time ? ` · ${todo.time}` : '';

    if (todo.completed) {
        return { state: 'done', label: due.toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) + timeLabel };
    }

    let isPastToday = false;
    if (days === 0 && todo.time) {
        const [h, m] = todo.time.split(':').map(Number);
        const now = new Date();
        isPastToday = now.getHours() * 60 + now.getMinutes() > h * 60 + m;
    }

    if (days < 0 || isPastToday) {
        const late = Math.abs(days);
        return { state: 'overdue', label: days === 0 ? `Overdue${timeLabel}` : `Overdue ${late} day${late > 1 ? 's' : ''}` };
    }
    if (days === 0) return { state: 'today', label: `Today${timeLabel}` };
    if (days === 1) return { state: 'soon', label: `Tomorrow${timeLabel}` };
    if (days <= 7) return { state: 'soon', label: `In ${days} days${timeLabel}` };
    return { state: 'later', label: due.toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) + timeLabel };
}

// [EN] Pending first, then nearest deadline, then priority; tasks without a deadline go last
// [VN] Chưa xong lên trước, rồi hạn gần nhất, rồi độ ưu tiên; task không có hạn xếp cuối
export function sortTodos(todos) {
    return [...todos].sort((a, b) => {
        if (a.completed !== b.completed) return a.completed ? 1 : -1;
        const aKey = a.deadline ? `${a.deadline} ${a.time || '99:99'}` : '9999';
        const bKey = b.deadline ? `${b.deadline} ${b.time || '99:99'}` : '9999';
        if (aKey !== bKey) return aKey < bKey ? -1 : 1;
        return PRIORITY_ORDER[a.priority] - PRIORITY_ORDER[b.priority];
    });
}

// [EN] Build one task row. textContent prevents HTML injection from task text
// [VN] Tạo một dòng task. Dùng textContent để chặn chèn HTML từ nội dung task
export function createTodoItem(todo, { compact = false } = {}) {
    const due = getDueInfo(todo);
    const li = document.createElement('li');
    li.className = `todo-item ${todo.completed ? 'completed' : ''} priority-${todo.priority}`;
    li.setAttribute('data-id', todo.id);

    li.innerHTML = `
        <button class="complete-btn" title="${todo.completed ? 'Mark as not done' : 'Mark as done'}"><i class="${todo.completed ? 'ph-fill ph-check-circle' : 'ph ph-circle'}"></i></button>
        <div class="todo-body">
            <span class="todo-text"></span>
            <div class="todo-meta">
                <span class="due-tag due-${due.state}"><i class="ph ph-calendar-blank"></i><span></span></span>
                ${compact ? '' : `<span class="priority-tag">${todo.priority}</span>`}
            </div>
        </div>
        ${compact ? '' : '<button class="delete-btn" title="Delete"><i class="ph ph-trash"></i></button>'}
    `;
    li.querySelector('.todo-text').textContent = todo.text;
    li.querySelector('.due-tag span').textContent = due.label;
    if (due.state === 'none') li.querySelector('.due-tag').hidden = compact;

    li.querySelector('.complete-btn').addEventListener('click', () => toggleTodoStatus(todo.id));
    const deleteBtn = li.querySelector('.delete-btn');
    if (deleteBtn) deleteBtn.addEventListener('click', () => deleteTodo(todo.id));
    return li;
}

// [EN] Compact list of pending tasks used in the Focus view (quick add, no date)
// [VN] Danh sách rút gọn các task chưa xong ở trang Focus (thêm nhanh, không cần ngày)
export function fetchTodos() {
    const todoListEl = document.getElementById('todo-list');
    const todoCountEl = document.getElementById('todo-count');
    const todos = getTodos();
    const pending = sortTodos(todos.filter(todo => !todo.completed));

    if (todoCountEl) todoCountEl.textContent = `${pending.length} left`;
    if (!todoListEl) return;
    todoListEl.innerHTML = '';

    if (pending.length === 0) {
        todoListEl.innerHTML = '<li class="empty-state"><i class="ph ph-note-blank"></i>Nothing pending. Add a task above.</li>';
        return;
    }
    pending.forEach(todo => todoListEl.appendChild(createTodoItem(todo, { compact: true })));
}

export function initTodo() {
    const todoInput = document.getElementById('todo-input');
    const addTodoBtn = document.getElementById('add-todo-btn');

    if(addTodoBtn) {
        addTodoBtn.addEventListener('click', () => {
            const text = todoInput.value.trim();
            if (text) {
                addTodo({ text });
                todoInput.value = '';
            }
            todoInput.focus();
        });
    }

    if(todoInput) {
        todoInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') addTodoBtn.click();
        });
    }

    window.addEventListener('studyhub:todos', fetchTodos);
    fetchTodos();
}
