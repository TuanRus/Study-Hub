// VI: Trang "Today": lời chào, thống kê tập trung trong ngày và các task sắp tới.
// EN: "Today" page: greeting, today's focus stats and upcoming tasks.

import { getTodayFocus } from '../utils/focusLog.js';
import { getTodos, sortTodos, getDueInfo, createTodoItem } from './todo.js';
import { currentUserName } from '../utils/api.js';

const MAX_PREVIEW_TASKS = 5;

function formatMinutes(total) {
    if (total < 60) return `${total}m`;
    const h = Math.floor(total / 60);
    const m = total % 60;
    return m ? `${h}h ${m}m` : `${h}h`;
}

export function updateGreeting() {
    const greetingEl = document.getElementById('home-greeting');
    if (!greetingEl) return;
    const hour = new Date().getHours();
    const part = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
    const user = currentUserName();
    greetingEl.textContent = user ? `${part}, ${user}` : part;
}

function renderFocusStats() {
    const { sessions, minutes } = getTodayFocus();
    const sessionsEl = document.getElementById('home-sessions');
    const minutesEl = document.getElementById('home-minutes');
    if (sessionsEl) sessionsEl.textContent = sessions;
    if (minutesEl) minutesEl.textContent = formatMinutes(minutes);
}

function renderTasks() {
    const todos = getTodos();
    const done = todos.filter(todo => todo.completed).length;
    const pending = todos.filter(todo => !todo.completed);
    const overdue = pending.filter(todo => getDueInfo(todo).state === 'overdue').length;
    const dueToday = pending.filter(todo => getDueInfo(todo).state === 'today').length;

    const tasksEl = document.getElementById('home-tasks');
    if (tasksEl) tasksEl.textContent = `${done}/${todos.length}`;
    const dueEl = document.getElementById('home-due');
    const dueLabelEl = document.getElementById('home-due-label');
    if (dueEl) dueEl.textContent = dueToday;
    if (dueLabelEl) dueLabelEl.textContent = overdue ? `Due today · ${overdue} overdue` : 'Due today';

    const listEl = document.getElementById('home-task-list');
    if (!listEl) return;
    listEl.innerHTML = '';

    if (pending.length === 0) {
        listEl.innerHTML = todos.length
            ? '<li class="empty-state"><i class="ph ph-confetti"></i>All tasks done. Nice work!</li>'
            : '<li class="empty-state"><i class="ph ph-note-blank"></i>No tasks yet. Add deadlines in the Deadlines tab.</li>';
        return;
    }

    sortTodos(pending).slice(0, MAX_PREVIEW_TASKS)
        .forEach(todo => listEl.appendChild(createTodoItem(todo, { compact: true })));
}

export function renderDashboard() {
    updateGreeting();
    renderFocusStats();
    renderTasks();
}

export function initDashboard() {
    window.addEventListener('studyhub:todos', renderTasks);
    window.addEventListener('studyhub:focus', renderFocusStats);
    window.addEventListener('studyhub:auth', updateGreeting);
    renderDashboard();
}
